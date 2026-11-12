#!/usr/bin/env bash
set -euo pipefail

# Stops the fixture a pid file names. SIGTERM lets it record its exit in the
# event log; SIGKILL follows only if it has not gone within the grace period.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/acceptance/desktop-automation/fixture-paths.sh
source "${script_dir}/fixture-paths.sh"

pid_file="${fixture_run_dir}/fixture.pid"
grace_seconds=5

usage() {
    cat >&2 <<EOF
Usage: ./scripts/acceptance/desktop-automation/stop.sh [--pid-file <path>]

Stops the fixture recorded in the pid file (default ${pid_file}).
Succeeds when no fixture is running.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --pid-file)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            pid_file="$2"
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

pid="$(running_fixture_pid "${pid_file}")"
if [[ -z "${pid}" ]]; then
    echo "No fixture is running for ${pid_file}."
    exit 0
fi

is_running() {
    kill -0 "${pid}" 2>/dev/null
}

kill -TERM "${pid}" 2>/dev/null || true
deadline=$((SECONDS + grace_seconds))
while is_running && ((SECONDS < deadline)); do
    sleep 0.1
done

if is_running; then
    kill -KILL "${pid}" 2>/dev/null || true
    sleep 0.2
    if is_running; then
        echo "Fixture pid ${pid} did not exit." >&2
        exit 1
    fi
    # A killed fixture cannot remove its own pid file.
    rm -f -- "${pid_file}"
    echo "Killed fixture pid ${pid} after it ignored SIGTERM for ${grace_seconds} seconds."
    exit 0
fi

echo "Stopped fixture pid ${pid}."
