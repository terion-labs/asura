#!/usr/bin/env python3
"""Check the shape and internal consistency of an ADR 0061 evidence receipt.

Exit status 0 when the receipt is well formed (and, with
--require-no-failures, no item failed), 1 otherwise. A skipped item is well
formed only with a reason, and a passed item only when its tests ran and none
was skipped or failed, so a receipt cannot claim evidence it does not hold.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


FORMAT = "asura-adr-0061-desktop-evidence-v1"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
COMMIT = re.compile(r"^[0-9a-f]{40}$")
TIMESTAMP = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")
TEST_STATUSES = {"passed", "failed", "skipped"}
MANUAL_STATUSES = {"manual", "not-run"}
REQUIRED_ITEMS = {
    "A0-library-identity",
    "A1-policy-matrix",
    "A2-self-operation",
    "A3-panic-boundary",
    "A4-stub-flavor",
    "A5-audit-secrecy",
    "B6-list-apps-omits-asura",
    "B6-fixture-end-to-end",
    "B6-stale-ref",
    "B6-outcome-unknown",
    "C-signed-update-survival",
    "C-repository-gate",
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("receipt", type=Path)
    parser.add_argument("--require-no-failures", action="store_true")
    arguments = parser.parse_args()
    try:
        receipt = json.loads(arguments.receipt.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"{arguments.receipt}: unreadable receipt: {error}", file=sys.stderr)
        return 1

    errors = validate(receipt)
    if not errors and arguments.require_no_failures:
        errors = [f"item {item['id']} failed" for item in receipt["items"] if item["status"] == "failed"]
    for error in errors:
        print(f"{arguments.receipt}: {error}", file=sys.stderr)
    return 1 if errors else 0


def validate(receipt: object) -> list[str]:
    errors: list[str] = []

    def require(condition: bool, message: str) -> bool:
        if not condition:
            errors.append(message)
        return condition

    if not require(isinstance(receipt, dict), "the receipt is not an object"):
        return errors
    require(receipt.get("format") == FORMAT, "format is not " + FORMAT)
    require(receipt.get("schemaVersion") == 1, "schemaVersion is not 1")
    require(isinstance(receipt.get("generatedAtUtc"), str) and TIMESTAMP.match(receipt["generatedAtUtc"]) is not None,
            "generatedAtUtc is not a UTC timestamp")

    source = receipt.get("source")
    if require(isinstance(source, dict), "source is missing"):
        require(isinstance(source.get("commit"), str) and COMMIT.match(source["commit"]) is not None,
                "source.commit is not a commit id")
        require(isinstance(source.get("dirty"), bool), "source.dirty is not a boolean")
        require(isinstance(source.get("changedPaths"), int) and source["changedPaths"] >= 0,
                "source.changedPaths is not a count")
        for key in ("diffSha256", "untrackedSha256"):
            require(isinstance(source.get(key), str) and SHA256.match(source[key]) is not None,
                    f"source.{key} is not a SHA-256")

    host = receipt.get("host")
    if require(isinstance(host, dict), "host is missing"):
        require(host.get("accessibility") in {"granted", "not_granted", "unknown"}, "host.accessibility is invalid")

    libraries = receipt.get("libraries")
    if require(isinstance(libraries, dict), "libraries is missing"):
        require(isinstance(libraries.get("stagedReleaseVerified"), bool), "libraries.stagedReleaseVerified is not a boolean")
        flavors = libraries.get("flavors")
        if require(isinstance(flavors, dict) and set(flavors) == {"release", "stub", "panic"},
                   "libraries.flavors must name release, stub and panic"):
            require(flavors["release"] is not None, "the release library has no receipt")
            for name, flavor in flavors.items():
                if flavor is None:
                    continue
                require(flavor.get("flavor") == name, f"libraries.flavors.{name} names another flavor")
                require(isinstance(flavor.get("commit"), str) and COMMIT.match(flavor["commit"]) is not None,
                        f"libraries.flavors.{name}.commit is not a commit id")
                require(isinstance(flavor.get("abiMajor"), int), f"libraries.flavors.{name}.abiMajor is not a number")
                require(flavor.get("panicInjectionExport") is (name == "panic"),
                        f"libraries.flavors.{name}.panicInjectionExport is wrong for its flavor")
                for key in ("receiptSha256", "librarySha256", "helperSha256"):
                    require(isinstance(flavor.get(key), str) and SHA256.match(flavor[key]) is not None,
                            f"libraries.flavors.{name}.{key} is not a SHA-256")

    fixture = receipt.get("fixture")
    if require(isinstance(fixture, dict) and isinstance(fixture.get("built"), bool), "fixture.built is missing"):
        digest = fixture.get("binarySha256")
        require((digest is None) != fixture["built"] and (digest is None or SHA256.match(digest) is not None),
                "fixture.binarySha256 must be a SHA-256 exactly when the fixture is built")

    tests = receipt.get("tests")
    items = receipt.get("items")
    if not (require(isinstance(tests, dict), "tests is missing") and require(isinstance(items, list), "items is missing")):
        return errors
    for key in ("total", "passed", "failed", "skipped"):
        require(isinstance(tests.get(key), int) and tests[key] >= 0, f"tests.{key} is not a count")
    if errors:
        return errors
    require(tests["total"] == tests["passed"] + tests["failed"] + tests["skipped"], "tests.total is not the sum of its parts")
    require(tests["total"] > 0, "no test ran")

    ids = [item.get("id") for item in items if isinstance(item, dict)]
    require(len(ids) == len(items) and len(set(ids)) == len(ids), "items must be objects with unique ids")
    require(set(ids) == REQUIRED_ITEMS, "items must be exactly " + ", ".join(sorted(REQUIRED_ITEMS)))
    sums = {"passed": 0, "failed": 0, "skipped": 0}
    for item in items:
        if not isinstance(item, dict):
            continue
        name = item.get("id", "?")
        counts = {key: item.get(key) for key in sums}
        if not all(isinstance(value, int) and value >= 0 for value in counts.values()):
            errors.append(f"item {name} has invalid counts")
            continue
        for key, value in counts.items():
            sums[key] += value
        status = item.get("status")
        reasons = item.get("skipReasons")
        failed_tests = item.get("failedTests")
        require(isinstance(item.get("title"), str) and item["title"], f"item {name} has no title")
        require(isinstance(item.get("adrEvidence"), str) and item["adrEvidence"], f"item {name} names no ADR evidence")
        require(isinstance(reasons, list) and all(isinstance(reason, str) for reason in reasons),
                f"item {name} skipReasons is not a list of text")
        require(isinstance(failed_tests, list), f"item {name} failedTests is not a list")
        if str(name).startswith("C-"):
            require(status in MANUAL_STATUSES, f"item {name} must be manual or not-run")
            require(bool(reasons), f"item {name} must say why it was not collected")
            continue
        require(status in TEST_STATUSES, f"item {name} has an invalid status {status!r}")
        if status == "passed":
            require(counts["passed"] > 0 and counts["failed"] == 0 and counts["skipped"] == 0,
                    f"item {name} claims passed without a clean run")
        elif status == "skipped":
            require(counts["failed"] == 0 and counts["skipped"] > 0 and bool(reasons),
                    f"item {name} is skipped without a skipped test and a reason")
        elif status == "failed":
            require(counts["failed"] > 0 or bool(failed_tests), f"item {name} is failed without a failure")

    for key, value in sums.items():
        require(value == tests[key], f"the items' {key} counts do not add up to tests.{key}")

    observations = receipt.get("observations")
    require(isinstance(observations, list) and all(isinstance(entry, dict) for entry in observations),
            "observations is not a list of objects")
    return errors


if __name__ == "__main__":
    sys.exit(main())
