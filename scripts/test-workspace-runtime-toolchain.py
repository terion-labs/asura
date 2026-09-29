#!/usr/bin/env python3
"""Exercise Swift SDK selection without compiling or changing host toolchains."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class WorkspaceRuntimeToolchainTests(unittest.TestCase):
    def run_fixture(self, inherited_sdk, resolver_fails=False,
                    script_name="build-workspace-runtime.sh"):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            scripts = root / "scripts"
            scripts.mkdir()
            script = scripts / script_name
            shutil.copyfile(Path(__file__).with_name(script.name), script)
            configuration = "configure-macos-toolchain.sh"
            shutil.copyfile(Path(__file__).with_name(configuration), scripts / configuration)
            binaries = root / "bin"
            binaries.mkdir()
            uname = binaries / "uname"
            uname.write_text("#!/bin/sh\nprintf 'Darwin\\n'\n")
            uname.chmod(0o755)
            xcrun = binaries / "xcrun"
            xcrun.write_text('''#!/bin/sh
set -eu
case "$*" in
    "--sdk macosx --show-sdk-path")
        if [ "$ASURA_TEST_SDK_FAILURE" = 1 ]; then exit 42; fi
        printf '%s\\n' "$DEVELOPER_DIR/SDKs/MacOSX.sdk"
        ;;
    "swift "*)
        printf '%s\\n' "${SDKROOT:-missing}" >> "$ASURA_TEST_SDK_LOG"
        test "${SDKROOT:-}" = "$DEVELOPER_DIR/SDKs/MacOSX.sdk"
        ;;
    *) exit 64 ;;
esac
''')
            xcrun.chmod(0o755)
            log = root / "swift-sdk.log"
            developer = root / "Custom Xcode.app/Contents/Developer"
            environment = os.environ.copy()
            environment.update({
                "PATH": f"{binaries}:{environment['PATH']}",
                "DEVELOPER_DIR": str(developer),
                "ASURA_TEST_SDK_FAILURE": "1" if resolver_fails else "0",
                "ASURA_TEST_SDK_LOG": str(log),
            })
            environment.pop("SDKROOT", None)
            if inherited_sdk is not None:
                environment["SDKROOT"] = inherited_sdk
            result = subprocess.run(
                ["bash", str(script), "--test" if script_name == "build-workspace-runtime.sh" else "--full"],
                env=environment,
                capture_output=True, text=True, timeout=10)
            calls = log.read_text().splitlines() if log.exists() else []
            return result, calls, str(developer / "SDKs/MacOSX.sdk")

    def test_stale_or_missing_sdk_uses_selected_xcode_for_every_swift_call(self):
        for sdk in (None, "/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk"):
            with self.subTest(sdk=sdk):
                result, calls, expected = self.run_fixture(sdk)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual([expected, expected], calls)

    def test_all_entry_points_stop_if_sdk_lookup_fails(self):
        for script in ("build-workspace-runtime.sh", "check-network-native.sh", "check.sh"):
            with self.subTest(script=script):
                result, calls, _ = self.run_fixture(
                    "/stale/SDK", resolver_fails=True, script_name=script)
                self.assertEqual(42, result.returncode, result.stderr)
                self.assertEqual([], calls)


if __name__ == "__main__":
    unittest.main()
