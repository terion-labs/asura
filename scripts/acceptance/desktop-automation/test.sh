#!/usr/bin/env bash
set -euo pipefail

# Builds the fixture and checks its event log contract by letting it activate
# its own controls in-process. Needs a logged-in graphical session and no
# Accessibility access; nothing here observes or drives another application.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/acceptance/desktop-automation/fixture-paths.sh
source "${script_dir}/fixture-paths.sh"

if [[ $# -gt 0 ]]; then
    echo "Usage: ./scripts/acceptance/desktop-automation/test.sh" >&2
    exit 64
fi

if command -v shellcheck >/dev/null 2>&1; then
    (cd -- "${repository_dir}" && shellcheck -x scripts/acceptance/desktop-automation/*.sh)
else
    echo "shellcheck is not installed; the fixture scripts were not linted." >&2
fi

"${script_dir}/build.sh"

exercise_dir="$(mktemp -d "${fixture_dir}/self-exercise.XXXXXX")"
cleanup() {
    rm -rf -- "${exercise_dir}"
}
trap cleanup EXIT

# Run directly rather than through LaunchServices so the exit status and any
# usage error reach this script. The timer bounds a run that cannot reach the
# window server.
"${fixture_binary}" \
    --self-exercise \
    --event-log "${exercise_dir}/events.jsonl" \
    --pid-file "${exercise_dir}/fixture.pid" \
    --control-file "${exercise_dir}/control" \
    --quit-after 120 \
    --slow-action-seconds 1

python3 "${script_dir}/check_self_exercise.py" "${exercise_dir}/events.jsonl"
if [[ -e "${exercise_dir}/fixture.pid" ]]; then
    echo "The fixture left its pid file behind after exiting." >&2
    exit 1
fi

echo "desktop-automation fixture checks passed"
