#!/usr/bin/env bash
set -euo pipefail

if (($# != 3)); then
  echo "usage: $0 SOURCE_ROOT OUTPUT_DIRECTORY IMAGE_REFERENCE" >&2
  echo "  ASURA_LINUX_RID      linux-x64 or linux-arm64 (default: this host)" >&2
  echo "  ASURA_LINUX_PACKAGE  an already published package to run instead of publishing" >&2
  echo "  ASURA_LINUX_WORK     scratch root for the package, profile and runtime dirs (default /work)" >&2
  echo "  ASURA_ACCEPTANCE_SYSTEM_NAME / ASURA_ACCEPTANCE_ENVIRONMENT  receipt naming" >&2
  exit 64
fi

source_root=$1
output_directory=$2
image_reference=$3
work_root=${ASURA_LINUX_WORK:-/work}
case "${ASURA_LINUX_RID:-}" in
  linux-x64|linux-arm64) rid=$ASURA_LINUX_RID ;;
  '')
    case "$(uname -m)" in
      x86_64) rid=linux-x64 ;;
      aarch64|arm64) rid=linux-arm64 ;;
      *) echo "Unsupported host architecture $(uname -m); set ASURA_LINUX_RID." >&2; exit 64 ;;
    esac
    ;;
  *) echo "ASURA_LINUX_RID must be linux-x64 or linux-arm64." >&2; exit 64 ;;
esac
system_name=${ASURA_ACCEPTANCE_SYSTEM_NAME:-"docker-${rid}-xvfb-openbox"}
environment_description=${ASURA_ACCEPTANCE_ENVIRONMENT:-"Docker Linux ${rid#linux-} under Xvfb with Openbox (synthetic display)"}

mkdir -p "$work_root"
package_directory=${ASURA_LINUX_PACKAGE:-}
runtime_evidence=$(mktemp -d "$work_root/asura-runtime-evidence.XXXXXX")
home_directory="$work_root/home"
runtime_directory="$work_root/runtime"
xvfb_log="$output_directory/xvfb.log"
openbox_log="$output_directory/openbox.log"

mkdir -p "$output_directory" "$runtime_evidence" "$home_directory" "$runtime_directory"
chmod 700 "$runtime_directory"

# The SDK muxer resolves global.json from the working directory, not from
# the project path, so every dotnet command runs from the source root.
(cd "$source_root" && dotnet --info) >"$output_directory/dotnet-info.txt"
cat /etc/os-release >"$output_directory/os-release.txt"
uname -a >"$output_directory/uname.txt"
{
  fc-match 'JetBrains Mono'
  fc-match 'Noto Sans CJK JP'
  fc-match 'Noto Color Emoji'
} >"$output_directory/fontconfig-matches.txt"

find "$source_root" -type f \
  ! -path '*/bin/*' \
  ! -path '*/obj/*' \
  ! -path '*/docs/acceptance/*' \
  -print0 \
  | sort -z \
  | xargs -0 sha256sum \
  | sha256sum \
  | awk '{print $1}' >"$output_directory/source-snapshot.sha256"
source_digest=$(tr -d '\n' <"$output_directory/source-snapshot.sha256")

if [[ -z "$package_directory" ]]; then
  package_directory=$(mktemp -d "$work_root/asura-package.XXXXXX")
  set +e
  (cd "$source_root" && dotnet restore "$source_root/src/Asura.Desktop/Asura.Desktop.csproj" \
    --runtime "$rid" \
    --locked-mode \
    --disable-build-servers) \
    >"$output_directory/restore.log" 2>&1
  restore_status=$?
  if ((restore_status != 0)); then
    set -e
    echo "$rid locked restore failed; see $output_directory/restore.log" >&2
    exit "$restore_status"
  fi
  (cd "$source_root" && dotnet publish "$source_root/src/Asura.Desktop/Asura.Desktop.csproj" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    --no-restore \
    -p:RestoreLockedMode=true \
    --output "$package_directory" \
    --disable-build-servers) \
    >"$output_directory/publish.log" 2>&1
  publish_status=$?
  set -e
  if ((publish_status != 0)); then
    echo "$rid self-contained publish failed; see $output_directory/publish.log" >&2
    exit "$publish_status"
  fi
elif [[ ! -x "$package_directory/Asura" ]]; then
  echo "ASURA_LINUX_PACKAGE does not hold an executable Asura: $package_directory" >&2
  exit 66
fi

file "$package_directory/Asura" >"$output_directory/package-file.txt"
ldd "$package_directory/Asura" >"$output_directory/package-ldd.txt"
find "$package_directory" -maxdepth 1 -type f -print0 \
  | sort -z \
  | xargs -0 sha256sum >"$output_directory/package-files.sha256"

Xvfb :99 -screen 0 1440x900x24 -nolisten tcp >"$xvfb_log" 2>&1 &
xvfb_pid=$!
openbox_pid=''
cleanup() {
  if [[ -n "$openbox_pid" ]]; then
    kill "$openbox_pid" 2>/dev/null || true
    wait "$openbox_pid" 2>/dev/null || true
  fi
  kill "$xvfb_pid" 2>/dev/null || true
  wait "$xvfb_pid" 2>/dev/null || true
}
trap cleanup EXIT

export DISPLAY=:99
export XDG_SESSION_TYPE=x11
for _ in {1..100}; do
  if xdpyinfo >/dev/null 2>&1; then
    break
  fi
  sleep 0.1
done
xdpyinfo >"$output_directory/xdpyinfo.txt"
xmodmap -pm >"$output_directory/xmodmap-modifiers.txt"
openbox --sm-disable >"$openbox_log" 2>&1 &
openbox_pid=$!
for _ in {1..100}; do
  if xprop -root _NET_SUPPORTING_WM_CHECK 2>/dev/null | grep -q 'window id'; then
    break
  fi
  sleep 0.1
done
if ! kill -0 "$openbox_pid" 2>/dev/null \
  || ! xprop -root _NET_SUPPORTING_WM_CHECK >"$output_directory/window-manager.txt" 2>&1 \
  || ! grep -q 'window id' "$output_directory/window-manager.txt"; then
  echo 'Openbox did not publish the X11 window-manager boundary.' >&2
  exit 70
fi

# A fresh profile bootstraps application encryption by storing its keys in
# the Secret Service, so the desktop needs one to start at all. The driver
# gives the app its own HOME; a passwordless login keyring there, served by
# gnome-keyring on the driver's session bus, stands in for the desktop's.
keyring_data="$home_directory/.local/share"
mkdir -p "$keyring_data/keyrings" "$home_directory/.cache"
printf 'login' >"$keyring_data/keyrings/default"
printf '[keyring]\ndisplay-name=login\nctime=0\nmtime=0\nlock-on-idle=false\nlock-after=false\n' \
  >"$keyring_data/keyrings/login.keyring"

set +e
ASURA_ACCEPTANCE_HOME="$home_directory" \
ASURA_ACCEPTANCE_RUNTIME_DIR="$runtime_directory" \
dbus-run-session -- bash -c '
  eval "$(env HOME="$ASURA_ACCEPTANCE_HOME" XDG_DATA_HOME="$ASURA_ACCEPTANCE_HOME/.local/share" XDG_CACHE_HOME="$ASURA_ACCEPTANCE_HOME/.cache" \
    XDG_RUNTIME_DIR="$ASURA_ACCEPTANCE_RUNTIME_DIR" gnome-keyring-daemon --start --components=secrets 2>/dev/null)"
  export GNOME_KEYRING_CONTROL
  exec python3 "$@"
' _ "$source_root/scripts/acceptance/linux_x11_xvfb_acceptance.py" \
  --package "$package_directory" \
  --source-root "$source_root" \
  --output "$output_directory" \
  --runtime-evidence "$runtime_evidence" \
  --source-digest "$source_digest" \
  --image-reference "$image_reference" \
  --home "$home_directory" \
  --runtime-dir "$runtime_directory" \
  --system-name "$system_name" \
  --environment "$environment_description"
acceptance_status=$?
set -e

exit "$acceptance_status"
