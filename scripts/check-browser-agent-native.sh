#!/usr/bin/env bash
set -euo pipefail
repository_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
    echo "This native browser acceptance runner requires macOS arm64." >&2
    exit 1
fi
cd "${repository_dir}"
export DOTNET_ROOT="${repository_dir}/.dotnet"
export NUGET_PACKAGES="${repository_dir}/.nuget/packages"
dotnet="${DOTNET_ROOT}/dotnet"
project="scripts/acceptance/browser-agent/Asura.BrowserAgentAcceptance.csproj"
"${dotnet}" restore "${project}" --locked-mode
"${dotnet}" build "${project}" -c Release --no-restore --nologo
mkdir -p src/Asura.Desktop/obj
work="$(mktemp -d "${repository_dir}/src/Asura.Desktop/obj/browser-agent-native.XXXXXX")"
app="${work}/Asura.dev.app"
./scripts/run-macos-development.sh \
    --target-directory scripts/acceptance/browser-agent/bin/Release/net10.0 \
    --cef-runtime-root native/artifacts/osx-arm64/cef \
    --app "${app}" \
    --info-plist-template tools/Asura.Packaging/MacOS/Info.plist.template \
    --runtime-identifier osx-arm64 --assemble-only
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
