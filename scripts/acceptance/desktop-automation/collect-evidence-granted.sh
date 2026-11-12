#!/usr/bin/env bash
set -euo pipefail

# Runs collect-evidence.sh inside the evidence runner app, so the native
# library and the fixture-driving tests run under the runner's Accessibility
# grant instead of the grant of whatever terminal or IDE started this script.
# Arguments are passed to collect-evidence.sh. Exit codes are the collector's,
# or 3 when the runner holds no grant yet; macOS then shows its grant prompt
# for the runner.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/acceptance/desktop-automation/fixture-paths.sh
source "${script_dir}/fixture-paths.sh"

"${script_dir}/build-runner.sh" >/dev/null

run_dir="${runner_dir}/run"
mkdir -p -- "${run_dir}"
status_file="${run_dir}/status"
log_file="${run_dir}/collect-evidence.log"
rm -f -- "${status_file}" "${log_file}"

# LaunchServices does not pass this shell's environment on; the one variable
# the evidence reads from it is forwarded explicitly.
open_arguments=(-W -n)
if [[ -n "${ASURA_DESKTOP_EVIDENCE_KEEP_FIXTURE_LOGS:-}" ]]; then
    open_arguments+=(--env "ASURA_DESKTOP_EVIDENCE_KEEP_FIXTURE_LOGS=${ASURA_DESKTOP_EVIDENCE_KEEP_FIXTURE_LOGS}")
fi
open "${open_arguments[@]}" "${runner_bundle}" --args \
    --status "${status_file}" \
    --log "${log_file}" \
    --cwd "${repository_dir}" \
    --prompt \
    -- /bin/bash "${script_dir}/collect-evidence.sh" "$@"

if [[ -f "${log_file}" ]]; then
    cat -- "${log_file}"
fi
status="$(cat -- "${status_file}" 2>/dev/null || echo missing)"
case "${status}" in
    untrusted)
        echo "The evidence runner holds no Accessibility grant; macOS has asked for it." >&2
        exit 3
        ;;
    "exit "*)
        exit "${status#exit }"
        ;;
    *)
        echo "The evidence runner reported no outcome." >&2
        exit 1
        ;;
esac
