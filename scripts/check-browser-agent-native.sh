#!/usr/bin/env bash
set -euo pipefail
repository_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
    echo "This native browser acceptance runner requires macOS arm64." >&2
    exit 1
fi
cd "${repository_dir}"
dotnet="${ASURA_DOTNET:-${repository_dir}/.dotnet/dotnet}"
export DOTNET_ROOT="$(dirname "${dotnet}")"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${repository_dir}/.nuget/packages}"
project="scripts/acceptance/browser-agent/Asura.BrowserAgentAcceptance.csproj"
if [[ $# -gt 1 || ( $# -eq 1 && "$1" != "--aot" ) ]]; then
    echo "Usage: $0 [--aot]" >&2
    exit 64
fi
work_root="${ASURA_BUILD_ARTIFACTS_ROOT:-${repository_dir}/src/Asura.Desktop/obj}"
mkdir -p "${work_root}"
work="$(mktemp -d "${work_root}/browser-agent-native.XXXXXX")"
"${dotnet}" restore "${project}" --locked-mode --artifacts-path "${work}/dotnet"
"${dotnet}" build "${project}" -c Release --no-restore --nologo --artifacts-path "${work}/dotnet"
app="${work}/Asura.dev.app"
./scripts/run-macos-development.sh \
    --target-directory "${work}/dotnet/bin/Asura.BrowserAgentAcceptance/release" \
    --cef-runtime-root native/artifacts/osx-arm64/cef \
    --app "${app}" \
    --info-plist-template tools/Asura.Packaging/MacOS/Info.plist.template \
    --runtime-identifier osx-arm64 --assemble-only
if [[ "${1:-}" == "--aot" ]]; then
    export SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"
    # Publish only the acceptance executable as Native AOT, then reuse the
    # verified CEF bundle. The probe exercises the production Browser assembly.
    "${dotnet}" publish "${project}" -c Release -r osx-arm64 \
        --artifacts-path "${work}/dotnet" \
        -p:AsuraBrowserAcceptanceNativeAot=true -p:RestoreLockedMode=true \
        -p:AsuraNativeAotLinker="${ASURA_NATIVE_AOT_LINKER:-}" \
        -o "${work}/aot"
    /usr/bin/codesign --force --sign - "${work}/aot/Asura"
    cp "${work}/aot/"*.dylib "${app}/Contents/MacOS/"
    ./scripts/prepare-avalonia-native-macos.sh "${app}/Contents/MacOS/libAvaloniaNative.dylib"
    cp "${work}/aot/Asura" "${app}/Contents/MacOS/Asura"
fi
node scripts/acceptance/browser-shell-smoke-fixture.mjs > "${work}/fixture.log" 2>&1 &
fixture_pid=$!
trap 'kill "${fixture_pid}" 2>/dev/null || true' EXIT
for ((attempt=0; attempt<100; attempt++)); do
    [[ -s "${work}/fixture.log" ]] && break
    sleep 0.1
done
fixture_origin="$(python3 -c 'import json,sys; print(json.loads(open(sys.argv[1]).readline())["origin"].rstrip("/"))' "${work}/fixture.log")"
fixture_proxy="$(python3 -c 'import json,sys; print(json.loads(open(sys.argv[1]).readline())["proxy"])' "${work}/fixture.log")"
"${app}/Contents/MacOS/Asura" "${fixture_origin}" "${work}/profile-direct" | tee "${work}/direct.log"
"${app}/Contents/MacOS/Asura" "${fixture_origin}" "${work}/profile-proxy" "${fixture_proxy}" | tee "${work}/proxy.log"
echo "Native browser acceptance passed. Evidence: ${work}"
