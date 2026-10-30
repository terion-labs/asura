#!/usr/bin/env bash
set -euo pipefail

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(cd -- "$script_directory/.." && pwd)
run_id=$(date -u +%Y%m%dT%H%M%SZ)
output_directory=${1:-"$repository_root/artifacts/platform-acceptance/$run_id-$$-$RANDOM-linux-arm64-xvfb"}
image=mcr.microsoft.com/dotnet/sdk:10.0
container_name="asura-linux-xvfb-$RANDOM-$$"
staging_directory=$(mktemp -d)
image_reference=$image
source_digest=unavailable
failure_stage=coordinator-initialization

output_parent=$(dirname -- "$output_directory")
mkdir -p "$output_parent"
output_parent=$(cd -- "$output_parent" && pwd)
output_directory="$output_parent/$(basename -- "$output_directory")"
if ! mkdir "$output_directory"; then
  echo "Refusing to merge acceptance evidence into existing path: $output_directory" >&2
  rm -rf -- "$staging_directory"
  exit 73
fi

cleanup() {
  docker rm --force "$container_name" >/dev/null 2>&1 || true
  rm -rf -- "$staging_directory"
}

finalize() {
  status=$?
  trap - EXIT
  set +e
  if [[ -f "$staging_directory/source-snapshot.sha256" ]]; then
    source_digest=$(tr -d '\n' <"$staging_directory/source-snapshot.sha256")
  fi
  if [[ ! -f "$staging_directory/evidence.json" ]]; then
    python3 "$repository_root/scripts/acceptance/linux_x11_xvfb_acceptance.py" \
      --output "$staging_directory" \
      --source-digest "$source_digest" \
      --image-reference "$image_reference" \
      --infrastructure-failure-stage "$failure_stage" \
      --infrastructure-failure-exit-code "$status" >/dev/null 2>&1
  fi

  copy_status=0
  find "$staging_directory" -mindepth 1 -maxdepth 1 \
    ! -name evidence.json \
    -exec cp -a {} "$output_directory/" \; || copy_status=$?
  if [[ -f "$staging_directory/evidence.json" && $copy_status -eq 0 ]]; then
    receipt_temporary="$output_directory/.evidence.json.$$.tmp"
    cp "$staging_directory/evidence.json" "$receipt_temporary" \
      && mv "$receipt_temporary" "$output_directory/evidence.json" \
      || copy_status=$?
  fi
  cleanup
  echo "Linux Xvfb acceptance evidence: $output_directory"
  if ((copy_status != 0)); then
    exit "$copy_status"
  fi
  exit "$status"
}
trap finalize EXIT

failure_stage=docker-image-pull
docker pull "$image" >/dev/null
failure_stage=docker-daemon-inspection
daemon_platform=$(docker info --format '{{.OSType}}/{{.Architecture}}')
if [[ "$daemon_platform" != "linux/aarch64" && "$daemon_platform" != "linux/arm64" ]]; then
  echo "This acceptance run requires a native Linux arm64 Docker daemon; found $daemon_platform." >&2
  exit 69
fi
failure_stage=docker-image-fingerprint
image_reference=$(docker image inspect \
  --format '{{if .RepoDigests}}{{index .RepoDigests 0}}{{else}}{{.Id}}{{end}}' \
  "$image")
docker version --format '{{json .Server}}' >"$staging_directory/docker-server.json"
printf '%s\n' "$daemon_platform" >"$staging_directory/docker-daemon-platform.txt"

failure_stage=container-startup
docker run \
  --detach \
  --name "$container_name" \
  --platform linux/arm64 \
  --volume "$repository_root:/repo:ro" \
  --volume "$staging_directory:/host-output" \
  "$image" \
  sleep infinity >/dev/null

failure_stage=container-dependency-install
docker exec "$container_name" bash -lc '
  apt-get update >/dev/null
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    dbus-x11 file fontconfig fonts-dejavu-core fonts-jetbrains-mono \
    fonts-noto-cjk fonts-noto-color-emoji fonts-noto-core imagemagick jq less \
    libdbus-1-3 libdrm2 libfontconfig1 \
    libfreetype6 libgbm1 libgl1 libice6 libsm6 libx11-6 libxcomposite1 \
    libxcursor1 libxext6 libxfixes3 libxi6 libxinerama1 libxrandr2 \
    libxrender1 ncurses-bin openbox procps python3 util-linux x11-utils \
    x11-xserver-utils xclip xdotool xterm xvfb \
    libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libatspi2.0-0 libcups2 \
    libxkbcommon0 libxdamage1 libpango-1.0-0 libcairo2 libasound2t64 \
    libxtst6 libexpat1 libglib2.0-0 \
    dbus-daemon gnome-keyring libsecret-tools >/dev/null
'

# global.json pins one exact SDK and the floating image tag moves past it.
# Install the pinned SDK beside the image's own, through the same reviewed
# installer bootstrap.sh uses, so the muxer resolves global.json.
failure_stage=container-sdk-install
docker exec "$container_name" bash -lc '
  set -euo pipefail
  sdk_version=$(sed -n "s/.*\"version\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" /repo/global.json | head -n 1)
  installer_sha256=$(sed -n "s/^installer_sha256=\"\([0-9a-f]*\)\"$/\1/p" /repo/scripts/bootstrap.sh)
  if [[ "$(dotnet --version 2>/dev/null)" != "$sdk_version" ]]; then
    installer=$(mktemp)
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    echo "$installer_sha256  $installer" | sha256sum -c - >/dev/null
    bash "$installer" --version "$sdk_version" --install-dir /usr/share/dotnet --no-path >/dev/null
    rm -f "$installer"
  fi
'

failure_stage=source-copy
docker exec "$container_name" bash -lc '
  mkdir -p /work/source
  # Anchored, so excluding the top-level artifacts tree does not also drop
  # native/artifacts, which carries the payloads the publish refuses to run
  # without. Other targets'"'"' native payloads and local caches stay behind.
  tar -C /repo \
    --anchored \
    --exclude=./.dotnet \
    --exclude=./.nuget \
    --exclude=./.deps \
    --exclude=./.test-results \
    --exclude=./.codex-audit \
    --exclude=./artifacts \
    --exclude=./tmp \
    --exclude=./docs/acceptance \
    --exclude=./native/artifacts/osx-arm64 \
    --exclude=./native/artifacts/osx-x64 \
    --exclude=./native/artifacts/win-x64 \
    --exclude=./website/node_modules \
    --exclude="*/bin" \
    --exclude="*/bin/*" \
    --exclude="*/obj" \
    --exclude="*/obj/*" \
    -cf - . | tar -C /work/source -xf -
'

set +e
failure_stage=container-acceptance
docker exec "$container_name" \
  bash /work/source/scripts/acceptance/run_linux_x11_xvfb.sh \
  /work/source \
  /host-output \
  "$image_reference"
acceptance_status=$?
set -e

exit "$acceptance_status"
