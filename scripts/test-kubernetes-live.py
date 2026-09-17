#!/usr/bin/env python3
"""Read-only acceptance through Asura's actual private Kubernetes worker.

Build Asura.Backend first. Credential command execution requires an explicit flag;
the exact command is parsed and fingerprinted before the worker executes it. No
credentials, resource bodies, cluster endpoints or log text are printed.
"""

import argparse
import datetime
import json
import pathlib
import re
import select
import struct
import subprocess
import time
import uuid


class Worker:
    def __init__(self, dotnet, assembly):
        self.command = [str(dotnet), str(assembly)]
        self.operation = uuid.uuid4().hex
        subprocess.run(self.command + ["prepare", self.operation], check=True, capture_output=True)
        self.process = subprocess.Popen(self.command + ["kubernetes", self.operation],
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        self.sequence = 0

    def invoke(self, operation, **values):
        self.sequence += 1
        payload = json.dumps(dict(id=self.sequence, operation=operation, **values)).encode()
        self.process.stdin.write(struct.pack("<i", len(payload)) + payload)
        self.process.stdin.flush()
        deadline = time.monotonic() + 90
        length = struct.unpack("<i", self.read(4, deadline))[0]
        if length < 0 or length > 16 * 1024 * 1024:
            raise RuntimeError("invalid_frame_size")
        result = json.loads(self.read(length, deadline))
        if result.get("id") != self.sequence or result.get("isResponse") is not True:
            raise RuntimeError("invalid_sequence")
        if result.get("error") is not None:
            raise RuntimeError("worker_error_" + str(result["error"]))
        return result

    def read(self, length, deadline):
        result = bytearray()
        while len(result) < length:
            timeout = deadline - time.monotonic()
            if timeout <= 0 or not select.select([self.process.stdout], [], [], timeout)[0]:
                raise RuntimeError("worker_timeout")
            # read1 avoids waiting for more than the currently available pipe data.
            part = self.process.stdout.read1(length - len(result))
            if not part:
                raise RuntimeError("worker_closed")
            result.extend(part)
        return result

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=5)
        self.process.stdout.close()
        subprocess.run(self.command + ["cleanup", self.operation], check=True, capture_output=True, timeout=20)


def prometheus_service(value):
    match = re.fullmatch(r"([a-z0-9][a-z0-9.-]*)/([a-z0-9][a-z0-9.-]*):([0-9]{1,5})", value)
    if match is None or not 1 <= int(match[3]) <= 65535:
        raise argparse.ArgumentTypeError("Use an in-cluster namespace/service:port")
    return dict(namespace=match[1], service=match[2], port=int(match[3]))


def read_prometheus(worker, provider, namespace, pod_reference):
    result = {}
    for kind, name in [(0, "pods"), (1, "nodes")]:
        request = dict(kind=kind, provider=provider)
        if kind == 0:
            request["namespace"] = namespace
        snapshot = worker.invoke(16, metrics=request)["metrics"]
        entries = snapshot["entries"]
        if snapshot["availability"] != 0 or not entries:
            raise RuntimeError("prometheus_" + name + "_unavailable")
        if kind == 0 and any(entry.get("namespace") != namespace for entry in entries):
            raise RuntimeError("prometheus_scope_mismatch")
        result["prometheus_" + name] = dict(
            entries=len(entries), cpu_samples=sum(entry.get("cpuCores") is not None for entry in entries),
            memory_samples=sum(entry.get("memoryBytes") is not None for entry in entries))
    if pod_reference is not None:
        end = datetime.datetime.now(datetime.timezone.utc).replace(second=0, microsecond=0)
        for metric, name in [(0, "cpu"), (1, "memory")]:
            request = dict(service=provider, namespace=namespace, pod=pod_reference["name"], metric=metric,
                           start=(end - datetime.timedelta(hours=1)).isoformat(), end=end.isoformat(), stepSeconds=60)
            history = worker.invoke(17, metricHistory=request)["metricHistory"]
            if history["availability"] != 0:
                raise RuntimeError("prometheus_history_" + name + "_unavailable")
            result["prometheus_history_" + name] = dict(series=len(history["series"]),
                samples=sum(sample.get("value") is not None for series in history["series"] for sample in series["samples"]))
    return result


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("contexts", nargs="+")
    parser.add_argument("--kubeconfig", default=str(pathlib.Path.home() / ".kube/config"))
    parser.add_argument("--namespace", default="default")
    parser.add_argument("--trust-existing-exec", action="store_true")
    parser.add_argument("--read-logs", action="store_true")
    parser.add_argument("--read-observability", action="store_true",
                        help="Read optional pod metrics and Helm release metadata in the selected namespace")
    parser.add_argument("--prometheus", type=prometheus_service,
                        help="Read bulk pod/node usage and first-pod history through namespace/service:port API proxy")
    args = parser.parse_args()
    dotnet = root / ".dotnet/dotnet"
    assembly = root / "src/Asura.Backend/bin/Release/net10.0/Asura.Backend.dll"
    failed = False
    for context in args.contexts:
        worker = None
        try:
            configuration = dict(contextName=context, namespace=args.namespace,
                                 kubeconfigPath=str(pathlib.Path(args.kubeconfig).expanduser().resolve()))
            worker = Worker(dotnet, assembly)
            review = worker.invoke(7, open=configuration)["review"]
            selected = next(item for item in review["contexts"] if item["contextName"] == context)
            worker.close()
            worker = None
            if selected.get("execFingerprint"):
                if not args.trust_existing_exec:
                    raise RuntimeError("exec_trust_required")
                configuration["trustedExecFingerprint"] = selected["execFingerprint"]
            worker = Worker(dotnet, assembly)
            worker.invoke(0, open=configuration)
            discovery = worker.invoke(1)["discovery"]
            kinds = discovery["resources"]
            pods = next(item for item in kinds if item["group"] == "" and item["resource"] == "pods")
            page = worker.invoke(2, list=dict(apiResource=pods, namespace=args.namespace, limit=25))["page"]
            result = dict(context=context, result="passed", served_resources=len(kinds),
                          unavailable_groups=len(discovery["unavailableGroups"]), pods=len(page["items"]),
                          has_next_page=page.get("continueToken") is not None)
            reference = None
            if page["items"]:
                reference = page["items"][0]["reference"]
                inspected = worker.invoke(3, resource=reference)["resource"]
                assert inspected["reference"]["uid"] == reference["uid"]
                result["inspect"] = "passed"
                if args.read_logs:
                    logs = worker.invoke(4, logs=dict(pod=reference, tailLines=5, maximumBytes=1024, timestamps=True))["logs"]
                    result["log_characters"] = len(logs["text"])
            converted = worker.invoke(8, manifest="apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: asura-read-only-conversion\n")["manifestJson"]
            assert json.loads(converted)["kind"] == "ConfigMap"
            result["manifest_conversion"] = "passed"
            if args.read_observability:
                metrics = worker.invoke(16, metrics=dict(kind=0, namespace=args.namespace))["metrics"]
                result["metrics_availability"] = metrics["availability"]
                result["metric_entries"] = len(metrics["entries"])
                releases = worker.invoke(18, helmList=dict(namespace=args.namespace, limit=10))["helmReleases"]
                result["helm_releases"] = len(releases["releases"])
                if releases["releases"]:
                    release = releases["releases"][0]
                    history = worker.invoke(19, helmHistory=dict(namespace=args.namespace, release=release["name"], maximumRevisions=5))["helmHistory"]
                    result["helm_history_revisions"] = len(history["revisions"])
            if args.prometheus is not None:
                result.update(read_prometheus(worker, args.prometheus, args.namespace, reference))
            print(json.dumps(result), flush=True)
        except Exception as error:
            failed = True
            category = str(error) if isinstance(error, RuntimeError) else type(error).__name__
            print(json.dumps(dict(context=context, result="failed", category=category)), flush=True)
        finally:
            if worker is not None:
                worker.close()
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
