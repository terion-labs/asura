#!/usr/bin/env bash
set -euo pipefail

# Builds the ADR 0061 evidence runner: a one-purpose, ad-hoc signed app that
# holds the macOS Accessibility grant for evidence runs, so no terminal or IDE
# needs it. See EvidenceRunner.swift. The bundle is a test tool under the
# gitignored native/artifacts tree; it is never packaged.
#
# macOS binds the grant to the bundle's code signature, and an ad-hoc signature
# changes with every build, so this script does not rebuild an existing runner
# unless --force is given. A rebuilt runner needs the grant again.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/acceptance/desktop-automation/fixture-paths.sh
source "${script_dir}/fixture-paths.sh"

force=false
if [[ $# -eq 1 && "$1" == "--force" ]]; then
    force=true
elif [[ $# -gt 0 ]]; then
    echo "Usage: ./scripts/acceptance/desktop-automation/build-runner.sh [--force]" >&2
    exit 64
fi

if [[ -d "${runner_bundle}" && "${force}" == false ]]; then
    echo "Kept ${runner_bundle}; pass --force to rebuild it (the grant must then be given again)."
    exit 0
fi
if [[ -L "${repository_dir}/native" || -L "${repository_dir}/native/artifacts" ]]; then
    echo "The native artifact directory must not be a symbolic link." >&2
    exit 1
fi

mkdir -p -- "${runner_dir}"
staging_dir="$(mktemp -d "${runner_dir}/build.XXXXXX")"
cleanup() {
    rm -rf -- "${staging_dir}"
}
trap cleanup EXIT

staged_bundle="${staging_dir}/${runner_bundle_name}"
mkdir -p -- "${staged_bundle}/Contents/MacOS"
xcrun swiftc \
    -parse-as-library \
    -warnings-as-errors \
    -O \
    -target "${fixture_swift_target}" \
    "${script_dir}/EvidenceRunner.swift" \
    -o "${staged_bundle}/Contents/MacOS/${runner_executable}"

cat > "${staged_bundle}/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key>
    <string>${runner_display_name}</string>
    <key>CFBundleExecutable</key>
    <string>${runner_executable}</string>
    <key>CFBundleIdentifier</key>
    <string>${runner_bundle_identifier}</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>${runner_display_name}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>${fixture_minimum_macos}</string>
    <key>LSUIElement</key>
    <true/>
</dict>
</plist>
EOF
plutil -lint "${staged_bundle}/Contents/Info.plist" >/dev/null

codesign --force --sign - --identifier "${runner_bundle_identifier}" "${staged_bundle}"
codesign --verify --strict "${staged_bundle}"

rm -rf -- "${runner_bundle}"
mv -- "${staged_bundle}" "${runner_bundle}"
echo "Built ${runner_bundle}"
