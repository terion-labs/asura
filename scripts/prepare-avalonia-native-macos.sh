#!/usr/bin/env bash
set -euo pipefail

# Build the exact NuGet source revision with macOS native repairs.
# Keep managed Avalonia and the native COM ABI at the same version.
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_dir="$(cd -- "${script_dir}/.." && pwd -P)"
component="${repository_dir}/native/avalonia-native"
revision="fee9c561ce036e8a3e8cee2397c75ca599b4790d"
archive_sha="604dff46924f4811ea11f60c07b2bae947c4c68c10fac80f7dde81f1be894155"

if [[ $# -gt 1 || "$(uname -s)" != Darwin ]]; then
    echo "Usage (macOS): $0 [copied-libAvaloniaNative.dylib]" >&2
    exit 64
fi
if [[ $# -eq 1 && ( ! -f "$1" || -L "$1" ) ]]; then
    echo "The copied Avalonia Native library is missing or linked: $1" >&2
    exit 1
fi
source "${script_dir}/configure-macos-toolchain.sh"

# Include the toolchain and every build input in the cache identity. Refuse an
# ABI mismatch when the managed dependency is upgraded, rather than silently
# installing the old native bridge into a new application.
key="$(python3 - "${repository_dir}" "$(xcodebuild -version)" "${SDKROOT}" <<'PY'
import hashlib
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
root = Path(sys.argv[1])
packages = ET.parse(root / 'Directory.Packages.props')
for name in ('Avalonia', 'Avalonia.Desktop'):
    package = packages.find(f'.//PackageVersion[@Include="{name}"]')
    if package is None or package.get('Version') != '12.0.5':
        raise SystemExit('Review the native Avalonia patch when upgrading its managed ABI.')
digest = hashlib.sha256('\n'.join(sys.argv[2:]).encode())
paths = sorted((root / 'native/avalonia-native').rglob('*')) + [
    root / 'scripts/prepare-avalonia-native-macos.sh',
    root / 'scripts/namespace-avalonia-native-macos.sh',
]
for path in paths:
    if path.is_file():
        if path.is_symlink():
            raise SystemExit(f'Linked native build input: {path}')
        digest.update(str(path.relative_to(root)).encode())
        digest.update(path.read_bytes())
print(digest.hexdigest())
PY
)"
cache_root="${repository_dir}/.deps/avalonia-native-build"
cache="${cache_root}/${key}"
mkdir -p "${cache_root}"

if [[ ! -d "${cache}" ]]; then
    stage="$(mktemp -d "${cache_root}/build.XXXXXX")"
    trap 'rm -rf -- "${stage}"' EXIT
    archive="${repository_dir}/.deps/avalonia-native-${revision}.tar.gz"
    if [[ ! -f "${archive}" ]]; then
        curl --fail --location --retry 3 \
            "https://codeload.github.com/AvaloniaUI/Avalonia/tar.gz/${revision}" \
            --output "${stage}/source.tar.gz"
        mv "${stage}/source.tar.gz" "${archive}"
    fi
    printf '%s  %s\n' "${archive_sha}" "${archive}" | shasum -a 256 -c -
    mkdir "${stage}/source"
    tar -xzf "${archive}" --strip-components=1 -C "${stage}/source"
    for patch_file in "${component}/patches/"*.patch; do
        git -C "${stage}/source" apply --check "${patch_file}"
        git -C "${stage}/source" apply "${patch_file}"
    done
    native_source="${stage}/source/native/Avalonia.Native"
    cp "${component}/avalonia-native.h" "${native_source}/inc/avalonia-native.h"
    cp "${component}/LICENSE" "${stage}/AVALONIA-NATIVE-LICENSE.txt"
    if ! xcodebuild \
        -project "${native_source}/src/OSX/Avalonia.Native.OSX.xcodeproj" \
        -scheme Avalonia.Native.OSX -configuration Release \
        -derivedDataPath "${stage}/build" -jobs 4 \
        'ARCHS=arm64 x86_64' ONLY_ACTIVE_ARCH=NO CODE_SIGNING_ALLOWED=NO \
        PRODUCT_NAME=AvaloniaNative INSTALL_PATH=@rpath build > "${stage}/build.log" 2>&1; then
        cat "${stage}/build.log" >&2
        exit 1
    fi
    cp "${stage}/build/Build/Products/Release/libAvaloniaNative.dylib" "${stage}/"
    "${script_dir}/namespace-avalonia-native-macos.sh" "${stage}/libAvaloniaNative.dylib"
    # Upstream headers use their own warning policy; the first-party lifetime
    # test is compiled warning-free and calls the actual packaged bridge.
    xcrun clang++ -std=c++11 -fobjc-arc -Wall -Wextra -Werror \
        -isystem "${native_source}/inc" -isystem "${native_source}/src/OSX" \
        "${component}/tests/lifetime.mm" "${stage}/libAvaloniaNative.dylib" \
        -framework Cocoa -Wl,-rpath,@executable_path -o "${stage}/lifetime-test"
    "${stage}/lifetime-test"
    xcrun clang++ -std=c++11 -fobjc-arc -Wall -Wextra -Werror \
        -isystem "${native_source}/inc" \
        "${component}/tests/metal-resize.mm" "${stage}/libAvaloniaNative.dylib" \
        -framework Cocoa -framework Metal -framework QuartzCore \
        -Wl,-rpath,@executable_path -o "${stage}/metal-resize-test"
    "${stage}/metal-resize-test"
    python3 - "${stage}" "${key}" "${revision}" "${archive_sha}" <<'PY'
import hashlib
import json
from pathlib import Path
import sys
root = Path(sys.argv[1])
files = {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in (
    'libAvaloniaNative.dylib', 'lifetime-test', 'metal-resize-test', 'AVALONIA-NATIVE-LICENSE.txt')}
(root / 'avalonia-native-build-receipt.json').write_text(json.dumps({
    'version': '12.0.5', 'sourceRevision': sys.argv[3], 'sourceArchiveSha256': sys.argv[4],
    'buildInputsSha256': sys.argv[2], 'files': files,
}, indent=2) + '\n')
PY
    # Publish only complete builds. Another concurrent build of the same inputs
    # can win the rename; its receipt is still checked below.
    python3 - "${stage}" "${cache}" <<'PY'
import errno
import os
import sys
try:
    os.rename(sys.argv[1], sys.argv[2])
except OSError as error:
    if error.errno not in (errno.EEXIST, errno.ENOTEMPTY):
        raise
PY
fi

python3 - "${cache}" "${key}" <<'PY'
import hashlib
import json
from pathlib import Path
import sys
root = Path(sys.argv[1])
receipt = json.loads((root / 'avalonia-native-build-receipt.json').read_text())
if receipt['buildInputsSha256'] != sys.argv[2]:
    raise SystemExit('Avalonia Native build inputs do not match their receipt.')
for name in ('libAvaloniaNative.dylib', 'lifetime-test', 'metal-resize-test', 'AVALONIA-NATIVE-LICENSE.txt'):
    path = root / name
    if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != receipt['files'][name]:
        raise SystemExit(f'Avalonia Native cached payload changed: {name}')
PY
"${cache}/lifetime-test"
"${cache}/metal-resize-test"
if [[ $# -eq 1 ]]; then
    cp "${cache}/libAvaloniaNative.dylib" "$1"
    cp "${cache}/avalonia-native-build-receipt.json" "${cache}/AVALONIA-NATIVE-LICENSE.txt" "$(dirname "$1")/"
fi
