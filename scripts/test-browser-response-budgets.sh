#!/usr/bin/env bash
set -euo pipefail
repository_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime="${1:-${repository_dir}/native/artifacts/osx-arm64/cef}"
[[ -f "${runtime}/cef-runtime-build-receipt.json" ]] || { echo "Build the verified CEF runtime first." >&2; exit 1; }
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/asura-response-budget-probe.XXXXXX")"
trap 'rm -rf -- "${test_dir}"' EXIT
app="${test_dir}/SessionCookies.app"
mkdir -p "${app}/Contents/MacOS" "${app}/Contents/Frameworks" "${test_dir}/small" "${test_dir}/large"
cp "${repository_dir}/native/browser-profile-tests/Info.plist" "${app}/Contents/Info.plist"
ditto "${runtime}/Chromium Embedded Framework.framework" "${app}/Contents/Frameworks/Chromium Embedded Framework.framework"
for helper in "${runtime}/"*.app; do
    ditto "${helper}" "${app}/Contents/Frameworks/$(basename "${helper}")"
done
cp "${runtime}/libexclr8cef.dylib" "${app}/Contents/Frameworks/"
xcrun clang -std=c11 -Wall -Wextra -Werror \
    -I "${repository_dir}/vendor/exclr8cef/native/shim" \
    "${repository_dir}/native/browser-profile-tests/response_budgets.c" \
    -L "${app}/Contents/Frameworks" -lexclr8cef -framework CoreFoundation -lz \
    -Wl,-rpath,@executable_path/../Frameworks -o "${app}/Contents/MacOS/SessionCookies"
codesign --force --sign - "${app}"
for mode in small large; do
    "${app}/Contents/MacOS/SessionCookies" "${test_dir}/${mode}" \
        "${app}/Contents/Frameworks/Asura Helper.app/Contents/MacOS/Asura Helper" "${mode}"
done
