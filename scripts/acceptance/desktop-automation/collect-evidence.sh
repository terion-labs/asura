#!/usr/bin/env bash
set -euo pipefail

# Collects the ADR 0061 acceptance evidence that a machine can collect:
# verifies the staged agent-desktop builds, builds the fixture, runs
# tests/Asura.DesktopAutomationAcceptance.Tests with the fixture enabled, and
# writes a machine-readable receipt. Evidence that needs the Accessibility
# grant is skipped with that reason when the grant is absent; it never counts
# as passed. The signed update check is a manual procedure and is listed in
# the receipt as such (docs/acceptance/adr-0061-desktop-automation.md).

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_dir="$(cd -- "${script_dir}/../../.." && pwd -P)"
project="${repository_dir}/tests/Asura.DesktopAutomationAcceptance.Tests/Asura.DesktopAutomationAcceptance.Tests.csproj"
output=""
artifacts_path=""

usage() {
    cat >&2 <<'EOF'
Usage: ./scripts/acceptance/desktop-automation/collect-evidence.sh [--output <directory>] [--artifacts-path <directory>]

Writes receipt.json, observations.jsonl and the test results to the output
directory, by default .test-results/desktop-automation-evidence/<UTC time>.
--artifacts-path is passed to dotnet test for a private build directory.
Exit status: 0 when no evidence test failed (skips allowed), 1 when one
failed, 2 when the host cannot collect evidence, 64 for usage errors.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output | --artifacts-path)
            [[ $# -ge 2 && -n "$2" ]] || { usage; exit 64; }
            case "$1" in
                --output) output="$2" ;;
                --artifacts-path) artifacts_path="$2" ;;
            esac
            shift 2
            ;;
        --help | -h)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage
            exit 64
            ;;
    esac
done

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
    echo "ADR 0061 evidence is collected on macOS on Apple Silicon, the release target." >&2
    exit 2
fi

if [[ -n "${ASURA_DOTNET:-}" ]]; then
    dotnet="${ASURA_DOTNET}"
elif [[ -x "${repository_dir}/.dotnet/dotnet" ]]; then
    dotnet="${repository_dir}/.dotnet/dotnet"
else
    echo "The pinned .NET SDK is unavailable. Run ./scripts/bootstrap.sh first." >&2
    exit 2
fi

if [[ -z "${output}" ]]; then
    output="${repository_dir}/.test-results/desktop-automation-evidence/$(date -u +%Y%m%dT%H%M%SZ)"
fi
mkdir -p -- "${output}"
output="$(cd -- "${output}" && pwd -P)"
results="${output}/results"
observations="${output}/observations.jsonl"
log="${output}/collect.log"
: > "${observations}"
: > "${log}"

# The staged release build must match its receipt and the catalog before
# anything is said about it. The stub and panic builds are test fixtures
# with receipts of their own, which the receipt below records.
staged_verified=true
if ! "${repository_dir}/scripts/build-agent-desktop.sh" --verify-staged --rid osx-arm64 >> "${log}" 2>&1; then
    staged_verified=false
    echo "The staged agent-desktop release build did not verify; see ${log}." >&2
fi

fixture_built=true
if ! "${script_dir}/build.sh" >> "${log}" 2>&1; then
    fixture_built=false
    echo "The fixture did not build; the evidence that launches it will be skipped. See ${log}." >&2
fi

# The evidence runs in the test assembly's own evidence host, not under
# dotnet test: agent-desktop sees an application launched after its first
# inventory read only while AppKit's main run loop is serviced, as it is in
# Asura, and the host's main thread does exactly that (EvidenceHost.cs).
build_arguments=(--configuration Release --nologo)
property_arguments=(-p:Configuration=Release)
if [[ -n "${artifacts_path}" ]]; then
    build_arguments+=(--artifacts-path "${artifacts_path}")
    property_arguments+=("-p:ArtifactsPath=${artifacts_path}" -p:UseArtifactsOutput=true)
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
"${dotnet}" build "${project}" "${build_arguments[@]}" >> "${log}" 2>&1
test_assembly="$("${dotnet}" msbuild "${project}" -nologo -getProperty:TargetPath "${property_arguments[@]}")"
if [[ ! -f "${test_assembly}" ]]; then
    echo "The evidence assembly was not built at '${test_assembly}'; see ${log}." >&2
    exit 1
fi

mkdir -p -- "${results}"
trx="${results}/desktop-automation-evidence.trx"
set +e
ASURA_DESKTOP_EVIDENCE=1 \
ASURA_DESKTOP_EVIDENCE_OBSERVATIONS="${observations}" \
    "${dotnet}" "${test_assembly}" --evidence-host --trx "${trx}" >> "${log}" 2>&1
test_status=$?
set -e

if [[ ! -f "${trx}" ]]; then
    echo "The evidence host produced no results (exit ${test_status}); see ${log}." >&2
    exit 1
fi

python3 -I "${script_dir}/build_evidence_receipt.py" \
    --repository "${repository_dir}" \
    --trx "${trx}" \
    --observations "${observations}" \
    --staged-verified "${staged_verified}" \
    --fixture-built "${fixture_built}" \
    --output "${output}/receipt.json"
python3 -I "${script_dir}/validate_evidence_receipt.py" "${output}/receipt.json"
python3 -I "${script_dir}/build_evidence_receipt.py" --summary "${output}/receipt.json"

if [[ "${test_status}" -ne 0 ]] || ! python3 -I "${script_dir}/validate_evidence_receipt.py" --require-no-failures "${output}/receipt.json"; then
    exit 1
fi
