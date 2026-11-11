#!/usr/bin/env bash
set -euo pipefail

# Launches the built fixture through LaunchServices, the way a user or
# desktop.launch_app starts an application, and waits until it reports ready.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/acceptance/desktop-automation/fixture-paths.sh
source "${script_dir}/fixture-paths.sh"

event_log="${fixture_run_dir}/events.jsonl"
pid_file="${fixture_run_dir}/fixture.pid"
control_file="${fixture_run_dir}/control"
quit_after="600"
slow_action_seconds="8"
ready_timeout_seconds=15

usage() {
    cat >&2 <<EOF
Usage: ./scripts/acceptance/desktop-automation/run.sh [--event-log <path>] [--pid-file <path>]
           [--control-file <path>] [--quit-after <seconds>] [--slow-action-seconds <seconds>]

Launches "${fixture_display_name}" and prints its pid and file paths once the
window is up. The fixture quits by itself after --quit-after seconds (default
${quit_after}, at most 86400). Its Slow Action button and its stall command hold
the main thread for --slow-action-seconds (default ${slow_action_seconds}, at most 60).
The files default to ${fixture_run_dir}.
EOF
}

absolute_path() {
    case "$1" in
        /*) printf '%s\n' "$1" ;;
        *) printf '%s/%s\n' "$(pwd -P)" "$1" ;;
    esac
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --event-log | --pid-file | --control-file | --quit-after | --slow-action-seconds)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            case "$1" in
                --event-log) event_log="$(absolute_path "$2")" ;;
                --pid-file) pid_file="$(absolute_path "$2")" ;;
                --control-file) control_file="$(absolute_path "$2")" ;;
                --quit-after) quit_after="$2" ;;
                --slow-action-seconds) slow_action_seconds="$2" ;;
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

# The fixture enforces the same ranges, but a launch through LaunchServices has
# no terminal to report a usage error to, so they are checked here first.
require_seconds() {
    local option="$1" value="$2" maximum="$3"
    if [[ ! "${value}" =~ ^[1-9][0-9]{0,4}$ ]] || ((value > maximum)); then
        echo "${option} requires whole seconds from 1 to ${maximum}: ${value}" >&2
        exit 64
    fi
}
require_seconds --quit-after "${quit_after}" 86400
require_seconds --slow-action-seconds "${slow_action_seconds}" 60
if [[ ! -x "${fixture_binary}" ]]; then
    echo "The fixture is not built. Run ./scripts/acceptance/desktop-automation/build.sh first." >&2
    exit 1
fi

running_pid="$(running_fixture_pid "${pid_file}")"
if [[ -n "${running_pid}" ]]; then
    echo "A fixture is already running as pid ${running_pid} with ${pid_file}." >&2
    echo "Stop it with ./scripts/acceptance/desktop-automation/stop.sh first." >&2
    exit 1
fi
# What is left is a pid file from a fixture that was killed before it could
# remove it. The fixture's readiness signal is this file appearing again.
if [[ -e "${pid_file}" ]]; then
    if [[ ! -f "${pid_file}" || ! "$(tr -d '[:space:]' < "${pid_file}")" =~ ^[0-9]*$ ]]; then
        echo "${pid_file} is not a fixture pid file; refusing to replace it." >&2
        exit 1
    fi
    rm -f -- "${pid_file}"
fi
mkdir -p -- "$(dirname -- "${event_log}")" "$(dirname -- "${pid_file}")" "$(dirname -- "${control_file}")"

open -n "${fixture_bundle}" --args \
    --event-log "${event_log}" \
    --pid-file "${pid_file}" \
    --control-file "${control_file}" \
    --quit-after "${quit_after}" \
    --slow-action-seconds "${slow_action_seconds}"

deadline=$((SECONDS + ready_timeout_seconds))
until [[ -s "${pid_file}" ]]; do
    if ((SECONDS >= deadline)); then
        echo "The fixture did not report ready within ${ready_timeout_seconds} seconds." >&2
        echo "It needs a logged-in graphical session; its event log is ${event_log}." >&2
        exit 1
    fi
    sleep 0.1
done

echo "pid=$(tr -d '[:space:]' < "${pid_file}")"
echo "event_log=${event_log}"
echo "pid_file=${pid_file}"
echo "control_file=${control_file}"
