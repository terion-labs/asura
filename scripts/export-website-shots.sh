#!/usr/bin/env bash
# Regenerates the marketing site's screenshots.
#
# Builds the design QA harness, exports the named routes (or every route the
# site uses) as Retina window frames into artifacts/design-qa/website, and
# writes 1760px-wide WebP copies to website/public/shots. Needs cwebp
# (`brew install webp`) and SF Pro Text on this Mac; see
# tools/Asura.DesignQa/README.md, "Website assets".
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_dir="$(cd "${script_dir}/.." && pwd)"
dotnet="${ASURA_DOTNET:-${repository_dir}/.dotnet/dotnet}"
export_dir="${repository_dir}/artifacts/design-qa/website"
shots_dir="${repository_dir}/website/public/shots"

# One entry per image the site references: website/app/**/*.vue.
site_routes=(
    workspace
    workspace-terminals
    workspace-agent
    workspace-agent-reasoning
    workspace-agent-approval
    workspace-browser
    workspace-browser-remote
    workspace-database
    workspace-redis
    workspace-docker-logs
    workspace-kubernetes
    workspace-kubernetes-logs
    workspace-kubernetes-helm
    workspace-kubernetes-agent
    workspace-git
    workspace-file-viewer
    workspace-process-monitor
    workspace-statistics
    settings-workspaces
    settings-networking
    settings-workspace-editor-isolated
    overlay-command-palette
    overlay-new-panel
)

if [[ $# -gt 0 ]]; then
    routes=("$@")
else
    routes=("${site_routes[@]}")
fi

command -v cwebp >/dev/null || {
    echo "cwebp is missing: brew install webp" >&2
    exit 1
}

cd "${repository_dir}"
"${dotnet}" build tools/Asura.DesignQa/Asura.DesignQa.csproj --configuration Release --nologo -v q
"${dotnet}" run \
    --project tools/Asura.DesignQa \
    --configuration Release \
    --no-build \
    -- \
    --website "${export_dir}" "${routes[@]}"

mkdir -p "${shots_dir}"
scaled="$(mktemp -d "${TMPDIR:-/tmp}/asura-website-shots.XXXXXX")"
trap 'rm -rf "${scaled}"' EXIT
for route in "${routes[@]}"; do
    frame="${export_dir}/${route}.png"
    [[ -f "${frame}" ]] || {
        echo "No frame for ${route}; the export above should say why." >&2
        exit 1
    }
    sips -Z 1760 "${frame}" --out "${scaled}/${route}.png" >/dev/null
    cwebp -quiet -q 82 -m 6 "${scaled}/${route}.png" -o "${shots_dir}/${route}.webp"
    echo "SHOT ${route} -> website/public/shots/${route}.webp"
done
