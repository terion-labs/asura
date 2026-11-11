#!/usr/bin/env python3
"""Write, or summarize, the ADR 0061 evidence receipt.

collect-evidence.sh runs tests/Asura.DesktopAutomationAcceptance.Tests and
hands this script the TRX result file. Every test belongs to exactly one
evidence item below; a test that belongs to none is an error, so a new test
cannot drop out of the receipt unnoticed. An item passes only when it ran at
least one test and every one of them passed. A skipped test keeps its item
skipped with the reason xunit reported, never passed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import platform
import subprocess
import sys
import xml.etree.ElementTree as ElementTree
from datetime import datetime, timezone
from pathlib import Path


FORMAT = "asura-adr-0061-desktop-evidence-v1"
TRX_NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
TEST_NAMESPACE = "Asura.DesktopAutomationAcceptance.Tests."
RID = "osx-arm64"

# (id, ADR evidence it serves, title, [(test class, method name prefix)])
ITEMS = [
    (
        "A0-library-identity",
        "Packaging and provenance; decision 6",
        "The receipted release library is the one loaded, from the application directory",
        [("LibraryIdentityEvidenceTests", "")],
    ),
    (
        "A1-policy-matrix",
        "Evidence 1: policy Off, Ask, Auto and confirmed YOLO",
        "Policy matrix through the real runtime, broker, audit store, host and library, without the grant",
        [("PolicyMatrixEvidenceTests", "")],
    ),
    (
        "A2-self-operation",
        "Evidence 2: proposals naming Asura fail before an approval card",
        "Asura's pid, a child helper and a non-child process under the install root are refused and audited denied",
        [
            ("SelfOperationEvidenceTests", "A_"),
            ("SelfOperationEvidenceTests", "The_guard_"),
            ("SelfOperationEvidenceTests", "The_same_program"),
        ],
    ),
    (
        "A3-panic-boundary",
        "Evidence 5: panic-injection build",
        "AD_RESULT_ERR_INTERNAL cancels the run, the process survives, the next run works",
        [("LibraryFlavorEvidenceTests", "A_panic")],
    ),
    (
        "A4-stub-flavor",
        "Closed failure codes: desktop_unavailable",
        "The stub build is never advertised and every call it receives is refused with a closed code",
        [("LibraryFlavorEvidenceTests", "The_stub"), ("LibraryFlavorEvidenceTests", "Every_call")],
    ),
    (
        "A5-audit-secrecy",
        "Authorization and dispatch: audit details",
        "No planted application name, window title, element name or typed text reaches the audit store",
        [("AuditSecrecyEvidenceTests", "")],
    ),
    (
        "B6-list-apps-omits-asura",
        "Evidence 2: desktop.list_apps omits Asura",
        "The live application list omits Asura and an Asura process, and lists the fixture once",
        [("SelfOperationEvidenceTests", "The_application_list_omits")],
    ),
    (
        "B6-fixture-end-to-end",
        "Evidence 1: the fixture driven end to end, every closed failure code",
        "Live policy matrix, exactly-once delivery, secure field, unsaved-changes sheet, failure recipes, two windows",
        [
            ("LiveFixtureEvidenceTests", "The_policy"),
            ("LiveFixtureEvidenceTests", "Each_"),
            ("LiveFixtureEvidenceTests", "Focus_"),
            ("LiveFixtureEvidenceTests", "Text_entry"),
            ("LiveFixtureEvidenceTests", "Close_is"),
            ("LiveFixtureEvidenceTests", "A_snapshot_of"),
        ],
    ),
    (
        "B6-stale-ref",
        "Evidence 3: stale-ref run",
        "Snapshot, remove the element out of band, act: desktop_stale_ref and no delivered input",
        [("LiveFixtureEvidenceTests", "A_ref_whose")],
    ),
    (
        "B6-outcome-unknown",
        "Evidence 4: outcome-unknown run",
        "A timed-out action is desktop_action_outcome_unknown, the batch remainder is skipped, no second dispatch",
        [("LiveFixtureEvidenceTests", "An_action_with")],
    ),
]

MANUAL_ITEMS = [
    (
        "C-signed-update-survival",
        "Evidence 6: Accessibility grant survives an in-app update",
        "manual",
        "Needs the owner's Developer ID identity and notarization; follow the procedure in "
        "docs/acceptance/adr-0061-desktop-automation.md.",
    ),
    (
        "C-repository-gate",
        "Evidence 7: ./scripts/check.sh --full green",
        "not-run",
        "This runner does not run the repository gate; run ./scripts/check.sh --full.",
    ),
]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", type=Path, help="print a summary of an existing receipt")
    parser.add_argument("--repository", type=Path)
    parser.add_argument("--trx", type=Path)
    parser.add_argument("--observations", type=Path)
    parser.add_argument("--staged-verified", choices=["true", "false"])
    parser.add_argument("--fixture-built", choices=["true", "false"])
    parser.add_argument("--output", type=Path)
    arguments = parser.parse_args()
    if arguments.summary is not None:
        print_summary(json.loads(arguments.summary.read_text(encoding="utf-8")))
        return 0

    required = ["repository", "trx", "observations", "staged_verified", "fixture_built", "output"]
    missing = [name for name in required if getattr(arguments, name) is None]
    if missing:
        parser.error("missing " + ", ".join("--" + name.replace("_", "-") for name in missing))

    repository = arguments.repository.resolve()
    results = read_trx(arguments.trx)
    receipt = {
        "format": FORMAT,
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "source": source_state(repository),
        "host": {
            "system": platform.system(),
            "release": platform.mac_ver()[0] or platform.release(),
            "machine": platform.machine(),
            "accessibility": accessibility(arguments.observations),
        },
        "libraries": {
            "stagedReleaseVerified": arguments.staged_verified == "true",
            "flavors": library_receipts(repository),
        },
        "fixture": fixture_state(repository, arguments.fixture_built == "true"),
        "tests": {
            "total": len(results),
            "passed": sum(1 for result in results if result["outcome"] == "passed"),
            "failed": sum(1 for result in results if result["outcome"] == "failed"),
            "skipped": sum(1 for result in results if result["outcome"] == "skipped"),
        },
        "items": build_items(results),
        "observations": read_observations(arguments.observations),
    }
    arguments.output.write_text(json.dumps(receipt, indent=2, sort_keys=False) + "\n", encoding="utf-8")
    return 0


def read_trx(path: Path) -> list[dict]:
    root = ElementTree.parse(path).getroot()
    results = []
    for result in root.iterfind(".//t:Results/t:UnitTestResult", TRX_NAMESPACE):
        outcome = {"Passed": "passed", "Failed": "failed", "NotExecuted": "skipped"}.get(
            result.get("outcome", ""), "failed"
        )
        name = result.get("testName", "")
        message = " ".join(
            text.strip()
            for text in (element.text for element in result.iterfind(".//t:Output//*", TRX_NAMESPACE))
            if text and text.strip()
        )
        results.append({"name": name, "outcome": outcome, "message": message})
    if not results:
        raise SystemExit(f"{path} holds no test results.")
    return results


def item_of(test_name: str) -> str:
    if not test_name.startswith(TEST_NAMESPACE):
        raise SystemExit(f"Unexpected test outside the evidence project: {test_name}")
    class_name, _, method = test_name[len(TEST_NAMESPACE):].partition(".")
    owners = [
        item_id
        for item_id, _, _, patterns in ITEMS
        if any(class_name == owner and method.startswith(prefix) for owner, prefix in patterns)
    ]
    if len(owners) != 1:
        raise SystemExit(f"{test_name} belongs to {len(owners)} evidence items; it must belong to exactly one.")
    return owners[0]


def build_items(results: list[dict]) -> list[dict]:
    grouped: dict[str, list[dict]] = {item_id: [] for item_id, _, _, _ in ITEMS}
    for result in results:
        grouped[item_of(result["name"])].append(result)

    items = []
    for item_id, adr, title, _ in ITEMS:
        members = grouped[item_id]
        passed = sum(1 for result in members if result["outcome"] == "passed")
        failed = sum(1 for result in members if result["outcome"] == "failed")
        skipped = sum(1 for result in members if result["outcome"] == "skipped")
        reasons = sorted({result["message"] or "no reason reported" for result in members if result["outcome"] == "skipped"})
        if failed or not members:
            status = "failed"
        elif skipped:
            status = "skipped"
        else:
            status = "passed"
        item = {
            "id": item_id,
            "adrEvidence": adr,
            "title": title,
            "status": status,
            "passed": passed,
            "failed": failed,
            "skipped": skipped,
            "skipReasons": reasons,
            "failedTests": sorted(result["name"] for result in members if result["outcome"] == "failed"),
        }
        if not members:
            item["failedTests"] = ["no test of this item ran"]
        items.append(item)

    for item_id, adr, status, reason in MANUAL_ITEMS:
        items.append(
            {
                "id": item_id,
                "adrEvidence": adr,
                "title": reason,
                "status": status,
                "passed": 0,
                "failed": 0,
                "skipped": 0,
                "skipReasons": [reason],
                "failedTests": [],
            }
        )
    return items


def git(repository: Path, *arguments: str) -> bytes:
    return subprocess.run(
        ["git", "-C", str(repository), *arguments], check=True, capture_output=True
    ).stdout


def source_state(repository: Path) -> dict:
    status = git(repository, "status", "--porcelain=v1", "-z")
    untracked = sorted(
        name for name in git(repository, "ls-files", "--others", "--exclude-standard", "-z").split(b"\0") if name
    )
    untracked_digest = hashlib.sha256()
    for name in untracked:
        path = repository / name.decode("utf-8", "surrogateescape")
        untracked_digest.update(name + b"\0")
        if path.is_file():
            untracked_digest.update(hashlib.sha256(path.read_bytes()).digest())
    return {
        "commit": git(repository, "rev-parse", "HEAD").decode("ascii").strip(),
        "dirty": bool(status),
        "changedPaths": len([entry for entry in status.split(b"\0") if entry]),
        "diffSha256": hashlib.sha256(git(repository, "diff", "HEAD", "--binary")).hexdigest(),
        "untrackedSha256": untracked_digest.hexdigest(),
    }


def library_receipts(repository: Path) -> dict:
    artifacts = repository / "native" / "artifacts" / RID
    directories = {
        "release": artifacts / "agent-desktop",
        "stub": artifacts / "agent-desktop-evidence" / "stub",
        "panic": artifacts / "agent-desktop-evidence" / "panic",
    }
    flavors = {}
    for flavor, directory in directories.items():
        receipt_path = directory / "agent-desktop-build-receipt.json"
        if not receipt_path.is_file():
            flavors[flavor] = None
            continue
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        flavors[flavor] = {
            "receiptSha256": hashlib.sha256(receipt_path.read_bytes()).hexdigest(),
            "flavor": receipt["flavor"],
            "commit": receipt["source"]["commit"],
            "abiMajor": receipt["abiMajor"],
            "panicInjectionExport": receipt["abi"]["panicInjectionExport"],
            "librarySha256": receipt["library"]["sha256"],
            "helperSha256": receipt["helper"]["sha256"],
        }
    return flavors


def fixture_state(repository: Path, built: bool) -> dict:
    binary = (
        repository / "native" / "artifacts" / RID / "agent-desktop-evidence" / "fixture"
        / "Desktop Automation Fixture.app" / "Contents" / "MacOS" / "DesktopAutomationFixture"
    )
    return {
        "built": built and binary.is_file(),
        "binarySha256": hashlib.sha256(binary.read_bytes()).hexdigest() if built and binary.is_file() else None,
    }


def read_observations(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def accessibility(path: Path) -> str:
    for observation in read_observations(path):
        if observation.get("item") == "A0" and observation.get("name") == "host":
            return {"Granted": "granted", "NotGranted": "not_granted"}.get(observation.get("accessibility"), "unknown")
    return "unknown"


def print_summary(receipt: dict) -> None:
    source = receipt["source"]
    tests = receipt["tests"]
    print(f"ADR 0061 desktop automation evidence, {receipt['generatedAtUtc']}")
    print(
        f"source {source['commit'][:12]}{' (dirty, ' + str(source['changedPaths']) + ' paths)' if source['dirty'] else ''}"
        f", host macOS {receipt['host']['release']} {receipt['host']['machine']}"
        f", Accessibility {receipt['host']['accessibility']}"
    )
    release = receipt["libraries"]["flavors"].get("release") or {}
    print(
        f"agent-desktop {release.get('commit', 'missing')[:12]} ABI {release.get('abiMajor', '?')}"
        f", staged release verified: {receipt['libraries']['stagedReleaseVerified']}"
        f", fixture built: {receipt['fixture']['built']}"
    )
    print(
        f"tests: {tests['total']} total, {tests['passed']} passed, {tests['failed']} failed, {tests['skipped']} skipped"
    )
    for item in receipt["items"]:
        counts = f"{item['passed']}/{item['failed']}/{item['skipped']}"
        print(f"  {item['status']:<8} {item['id']:<28} pass/fail/skip {counts:<9} {item['adrEvidence']}")
        if item["status"] in ("skipped", "manual", "not-run"):
            for reason in item["skipReasons"]:
                print(f"           reason: {reason}")
        for failed in item["failedTests"]:
            print(f"           failed: {failed}")


if __name__ == "__main__":
    sys.exit(main())
