#!/usr/bin/env bash
set -euo pipefail

# Builds the ADR 0061 fixture application as an ad-hoc signed .app bundle under
# native/artifacts/<rid>/agent-desktop-evidence/fixture. The bundle is a test
# fixture: it is never packaged and never signed with a release identity.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/acceptance/desktop-automation/fixture-paths.sh
source "${script_dir}/fixture-paths.sh"

if [[ $# -gt 0 ]]; then
    echo "Usage: ./scripts/acceptance/desktop-automation/build.sh" >&2
    exit 64
fi

if [[ -L "${repository_dir}/native" || -L "${repository_dir}/native/artifacts" ]]; then
    echo "The native artifact directory must not be a symbolic link." >&2
    exit 1
fi
if ! xcrun --find swiftc >/dev/null 2>&1; then
    echo "swiftc was not found. Install the Xcode command line tools." >&2
    exit 1
fi

mkdir -p -- "${fixture_dir}"
staging_dir="$(mktemp -d "${fixture_dir}/build.XXXXXX")"
cleanup() {
    rm -rf -- "${staging_dir}"
}
trap cleanup EXIT

staged_bundle="${staging_dir}/${fixture_bundle_name}"
mkdir -p -- "${staged_bundle}/Contents/MacOS"

xcrun swiftc \
    -parse-as-library \
    -warnings-as-errors \
    -O \
    -target "${fixture_swift_target}" \
    "${script_dir}/DesktopAutomationFixture.swift" \
    -o "${staged_bundle}/Contents/MacOS/${fixture_executable}"

cat > "${staged_bundle}/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>${fixture_display_name}</string>
    <key>CFBundleExecutable</key>
    <string>${fixture_executable}</string>
    <key>CFBundleIdentifier</key>
    <string>${fixture_bundle_identifier}</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>${fixture_display_name}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>${fixture_minimum_macos}</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
EOF
plutil -lint "${staged_bundle}/Contents/Info.plist" >/dev/null

codesign --force --sign - --identifier "${fixture_bundle_identifier}" "${staged_bundle}"
codesign --verify --strict "${staged_bundle}"

rm -rf -- "${fixture_bundle}"
mv -- "${staged_bundle}" "${fixture_bundle}"

echo "Built ${fixture_bundle}"
