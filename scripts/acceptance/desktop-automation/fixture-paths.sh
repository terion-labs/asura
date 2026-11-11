#!/usr/bin/env bash

# Sourced by the fixture scripts: where the fixture bundle is built and where a
# scripted run keeps its files. Everything lives under the gitignored
# native/artifacts tree, next to the agent-desktop evidence libraries.
#
# shellcheck disable=SC2034  # every variable is read by the sourcing script

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "The desktop automation fixture is a macOS application." >&2
    exit 2
fi

case "$(uname -m)" in
    arm64)
        fixture_rid="osx-arm64"
        fixture_swift_architecture="arm64"
        ;;
    x86_64)
        fixture_rid="osx-x64"
        fixture_swift_architecture="x86_64"
        ;;
    *)
        echo "Unsupported fixture build host: $(uname -m)" >&2
        exit 1
        ;;
esac

fixture_bundle_identifier="dev.asura-fixture.desktop-automation"
fixture_display_name="Desktop Automation Fixture"
fixture_executable="DesktopAutomationFixture"
# Asura's own macOS floor.
fixture_minimum_macos="13.0"
fixture_swift_target="${fixture_swift_architecture}-apple-macos${fixture_minimum_macos}"

repository_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
fixture_dir="${repository_dir}/native/artifacts/${fixture_rid}/agent-desktop-evidence/fixture"
# The bundle's file name is its display name; LaunchServices reports any other
# file name in place of CFBundleDisplayName.
fixture_bundle_name="${fixture_display_name}.app"
fixture_bundle="${fixture_dir}/${fixture_bundle_name}"
fixture_binary="${fixture_bundle}/Contents/MacOS/${fixture_executable}"
fixture_run_dir="${fixture_dir}/run"

# The evidence runner holds the Accessibility grant for evidence runs; see
# EvidenceRunner.swift. Its identifier is outside sh.asura on purpose: it is a
# test tool, not Asura, and the self-target guard must treat it as foreign.
runner_bundle_identifier="dev.asura-fixture.evidence-runner"
runner_display_name="Asura Desktop Evidence Runner"
runner_executable="AsuraDesktopEvidenceRunner"
runner_dir="${repository_dir}/native/artifacts/${fixture_rid}/agent-desktop-evidence/runner"
runner_bundle_name="${runner_display_name}.app"
runner_bundle="${runner_dir}/${runner_bundle_name}"

# Prints the pid recorded in a pid file when that process is a running fixture,
# and nothing otherwise. A recycled pid that now belongs to another program is
# never reported, so callers cannot signal a stranger.
running_fixture_pid() {
    local pid_file="$1"
    local pid command
    [[ -f "${pid_file}" ]] || return 0
    pid="$(tr -d '[:space:]' < "${pid_file}")"
    [[ "${pid}" =~ ^[1-9][0-9]*$ ]] || return 0
    command="$(ps -p "${pid}" -o comm= 2>/dev/null || true)"
    if [[ "${command}" == */Contents/MacOS/"${fixture_executable}" ]]; then
        printf '%s\n' "${pid}"
    fi
}
