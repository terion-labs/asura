#!/usr/bin/env python3
"""Exercise the actual archive verification/extraction block without building Xcode."""

import hashlib
import io
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest


class AvaloniaSourceArchiveTests(unittest.TestCase):
    def run_fixture(self, corrupt_before_verification=False):
        script = Path(__file__).with_name("prepare-avalonia-native-macos.sh").read_text()
        # Run production shell statements with real checksum and extraction tools.
        block = script[script.index('    archive="'):script.index('    for patch_file in')]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = root / ".deps"
            cache.mkdir()
            stage = root / "private-stage"
            stage.mkdir(mode=0o700)
            archive = cache / "avalonia-native-fixture.tar.gz"
            replacement = root / "replacement.tar.gz"
            for path, contents in ((archive, b"verified source"), (replacement, b"changed source")):
                with tarfile.open(path, "w:gz") as output:
                    member = tarfile.TarInfo("source/payload.txt")
                    member.size = len(contents)
                    output.addfile(member, io.BytesIO(contents))
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            if corrupt_before_verification:
                archive.write_bytes(replacement.read_bytes())
            environment = os.environ.copy()
            environment.update({
                "repository_dir": str(root), "stage": str(stage),
                "revision": "fixture", "archive_sha": digest,
                "ASURA_TEST_REPLACEMENT": str(replacement),
            })
            result = subprocess.run(["bash", "-c", '''set -euo pipefail
shasum() {
    command shasum "$@" || return "$?"
    cp "$ASURA_TEST_REPLACEMENT" "$archive"
}
''' + block], env=environment, capture_output=True, text=True, timeout=10)
            payload = stage / "source/payload.txt"
            return result, payload.read_bytes() if payload.exists() else None

    def test_replacing_shared_archive_after_verification_does_not_change_extracted_source(self):
        result, payload = self.run_fixture()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(b"verified source", payload)

    def test_corrupt_cached_source_is_rejected_before_extraction(self):
        result, payload = self.run_fixture(corrupt_before_verification=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIsNone(payload)


if __name__ == "__main__":
    unittest.main()
