#!/usr/bin/env python3
"""Integrity checks for the pinned gVisor forwarding preparation."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile
import sys

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location(
    "forwarding", Path(__file__).with_name("prepare-gvisor-forwarding.py"))
forwarding = importlib.util.module_from_spec(spec)
spec.loader.exec_module(forwarding)


class ForwardingIntegrityTests(unittest.TestCase):
    def test_changed_upstream_source_cannot_be_patched(self):
        with self.assertRaisesRegex(ValueError, "reviewed pin"):
            forwarding.patch_forwarding(b"unreviewed source")

    def test_modified_archive_is_rejected_before_patching(self):
        with tempfile.TemporaryDirectory() as root:
            archive = Path(root) / "module.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr(f"{forwarding.MODULE}@{forwarding.VERSION}/go.mod", "modified")
            with self.assertRaisesRegex(ValueError, "go.sum"):
                forwarding.verified_sources(archive)

    def test_archive_cannot_escape_its_module_root(self):
        with tempfile.TemporaryDirectory() as root:
            archive = Path(root) / "module.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr(f"{forwarding.MODULE}@{forwarding.VERSION}/../escape.go", "x")
            with self.assertRaisesRegex(ValueError, "Invalid path"):
                forwarding.verified_sources(archive)

    def test_cache_requires_exact_contents(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source.go"
            source.write_bytes(b"verified")
            expected = {"source.go": b"verified"}
            forwarding.verify_cache(root, expected)
            source.write_bytes(b"modified")
            with self.assertRaisesRegex(ValueError, "differs"):
                forwarding.verify_cache(root, expected)
            source.write_bytes(b"verified")
            (root / "extra.go").write_bytes(b"injected")
            with self.assertRaisesRegex(ValueError, "differs"):
                forwarding.verify_cache(root, expected)

    def test_cache_rejects_links_to_other_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source.go"
            source.symlink_to(root / "missing")
            with self.assertRaisesRegex(ValueError, "symbolic link"):
                forwarding.verify_cache(root, {"source.go": b"verified"})


if __name__ == "__main__":
    unittest.main()
