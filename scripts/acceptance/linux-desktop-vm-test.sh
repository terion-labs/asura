#!/usr/bin/env bash
# Run inside the guest as root: switch the autologin user's session to the
# named desktop, restart GDM, wait for the session, launch Asura, screenshot.
#   vm-desktop-test.sh <user> <session> <out-dir> [extra seconds]
# session: gnome | gnome-xorg | plasma | plasmax11 | xfce
set -uo pipefail
user=${1:?user}; session=${2:?session}; out=${3:?out}; settle=${4:-25}
home=$(getent passwd "$user" | cut -d: -f6)
uid=$(id -u "$user")
rm -rf "$out"; mkdir -p "$out"; chown "$user" "$out"

# Session choice lives in AccountsService; both keys so Wayland and X11 pick it up.
mkdir -p /var/lib/AccountsService/users
cat >/var/lib/AccountsService/users/"$user" <<EOF
[User]
Session=$session
XSession=$session
SystemAccount=false
EOF

# accounts-daemon caches the record; set it through its API as well so GDM's
# autologin sees the new session choice immediately.
busctl call org.freedesktop.Accounts /org/freedesktop/Accounts/User$uid org.freedesktop.Accounts.User SetSession s "$session" >/dev/null 2>&1 || true
busctl call org.freedesktop.Accounts /org/freedesktop/Accounts/User$uid org.freedesktop.Accounts.User SetXSession s "$session" >/dev/null 2>&1 || true
busctl get-property org.freedesktop.Accounts /org/freedesktop/Accounts/User$uid org.freedesktop.Accounts.User Session >"$out/accounts-session.txt" 2>&1 || true
pkill -u "$user" -x Asura 2>/dev/null || true
# End every graphical session of the user first; a GDM restart alone leaves
# the old compositor and its X authority behind, and the scan below would
# borrow a dead display from them. The SSH session driving this stays.
for old in $(loginctl list-sessions --no-legend | awk -v u="$user" '$3==u && $4=="seat0"{print $1}'); do
  loginctl terminate-session "$old" 2>/dev/null || true
done
sleep 3
# Desktop services (gnome-session, ksmserver, gsd-*) live under the user's
# systemd manager, not the login session, and a leftover session manager
# makes the next desktop refuse to start. Kill everything of the user except
# the SSH session this script runs in.
mine=$(grep -o 'session-[0-9]*\.scope' /proc/$$/cgroup | head -1)
for p in $(pgrep -u "$user"); do
  grep -q "${mine:-no-such-scope}" /proc/"$p"/cgroup 2>/dev/null && continue
  kill "$p" 2>/dev/null || true
done
sleep 3
for p in $(pgrep -u "$user"); do
  grep -q "${mine:-no-such-scope}" /proc/"$p"/cgroup 2>/dev/null && continue
  kill -9 "$p" 2>/dev/null || true
done
sleep 2
systemctl restart gdm3
# Wait for the compositor of the chosen session.
case "$session" in
  ubuntu|gnome|gnome-xorg) proc=gnome-shell ;;
  plasma|plasmax11) proc=plasmashell ;;
  xfce) proc=xfwm4 ;;
esac
for i in $(seq 1 90); do pgrep -u "$user" -x "$proc" >/dev/null && break; sleep 1; done
sleep 8
pid=$(pgrep -u "$user" -x "$proc" | head -1)
if [[ -z "$pid" ]]; then echo "session $session did not start" >>"$out/status.txt"; exit 1; fi
# The compositor's own environment (bus, runtime dir, desktop names) plus
# the display and authority of the X server that serves this session: under
# Wayland that is the newest Xwayland, on X11 the newest Xorg, each started
# after the old sessions were torn down above.
envfile="$out/session.env"
tr '\0' '\n' </proc/"$pid"/environ | grep -E '^(WAYLAND_DISPLAY|DBUS_SESSION_BUS_ADDRESS|XDG_RUNTIME_DIR|XDG_SESSION_TYPE|XDG_CURRENT_DESKTOP|XDG_SESSION_DESKTOP|DESKTOP_SESSION|GNOME_KEYRING_CONTROL|LANG)=' >"$envfile" || true
xserver=""
for attempt in $(seq 1 30); do
  xserver=$(pgrep -u "$user" -n -f '(^|/)(Xwayland|Xorg)( |$)' || true)
  [[ -n "$xserver" ]] && break
  # XWayland starts on demand; poke it once so it exists.
  sudo -u "$user" env -i $(cat "$envfile" | tr '\n' ' ') DISPLAY=:0 timeout 3 xdpyinfo >/dev/null 2>&1 || true
  sleep 2
done
if [[ -n "$xserver" ]]; then
  display=$(tr '\0' '\n' </proc/"$xserver"/cmdline | grep -m1 '^:[0-9]')
  auth=$(tr '\0' '\n' </proc/"$xserver"/cmdline | grep -A1 '^-auth$' | tail -1)
  echo "DISPLAY=${display:-:0}" >>"$envfile"
  [[ -n "$auth" ]] && echo "XAUTHORITY=$auth" >>"$envfile"
  echo "xserver pid=$xserver display=${display:-:0} auth=$auth" >>"$out/status.txt"
else
  echo "no X server found for the session" >>"$out/status.txt"
fi
run_in_session() { sudo -u "$user" env -i HOME="$home" USER="$user" PATH=/usr/local/bin:/usr/bin:/bin SHELL=/bin/bash $(cat "$envfile" | tr '\n' ' ') "$@"; }

run_in_session bash -c 'echo "desktop=$XDG_CURRENT_DESKTOP session=$XDG_SESSION_TYPE display=$DISPLAY wayland=$WAYLAND_DISPLAY"; for k in color-scheme accent-color font-name monospace-font-name text-scaling-factor scaling-factor cursor-size; do printf "%s=" "$k"; gsettings get org.gnome.desktop.interface $k 2>/dev/null || echo; done; printf "mutter-experimental="; gsettings get org.gnome.mutter experimental-features 2>/dev/null || echo; echo "-- xrandr:"; xrandr 2>/dev/null | head -3; echo "-- xrdb:"; xrdb -query 2>/dev/null | grep -i -E "dpi|scale"; echo "-- xdpyinfo:"; xdpyinfo 2>/dev/null | grep -E "dimensions|resolution"; echo "-- portal:"; timeout 5 gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop --method org.freedesktop.portal.Settings.ReadOne org.freedesktop.appearance accent-color 2>&1 | head -2; timeout 5 gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop --method org.freedesktop.portal.Settings.ReadOne org.freedesktop.appearance color-scheme 2>&1 | head -1' >"$out/desktop-info.txt" 2>&1
run_in_session bash -c 'timeout 10 secret-tool store --label=probe asura probe <<<probe-value && timeout 5 secret-tool lookup asura probe' >"$out/secret-service.txt" 2>&1; echo "exit=$?" >>"$out/secret-service.txt"

( cd /opt/asura && run_in_session bash -c 'exec /opt/asura/Asura' >"$out/asura.log" 2>&1 ) &
sleep "$settle"

shot() {
  local file=$1
  case "$session" in
    ubuntu|gnome)
      # Shell.Screenshot is denied to ordinary callers; GNOME's own Shift+Print
      # keybinding saves a full-screen capture under ~/Pictures/Screenshots.
      pgrep -x ydotoold >/dev/null || { ydotoold >/dev/null 2>&1 & sleep 2; }
      mkdir -p "$home/Pictures/Screenshots"; chown -R "$user" "$home/Pictures"
      before=$(ls -1 "$home/Pictures/Screenshots" 2>/dev/null | wc -l)
      ydotool key 42:1 99:1 99:0 42:0; sleep 4
      newest=$(ls -1t "$home/Pictures/Screenshots"/*.png 2>/dev/null | head -1)
      if [[ -n "$newest" && $(ls -1 "$home/Pictures/Screenshots" | wc -l) -gt $before ]]; then mv "$newest" "$file"; else echo "gnome keybinding screenshot failed" >>"$out/status.txt"; fi ;;
    plasma|plasmax11)
      run_in_session bash -c "timeout 60 spectacle -b -n -o '$file'" >>"$out/spectacle.log" 2>&1 ;;
    *)
      run_in_session bash -c "import -window root '$file'" >/dev/null 2>&1 ;;
  esac
  [[ -f "$file" ]] && echo "screenshot $file" >>"$out/status.txt"
}
shot "$out/asura-light.png"
run_in_session bash -c 'xwininfo -root -tree 2>/dev/null | grep -i -E "asura|main ·" | head -3' >"$out/window-geometry.txt" 2>&1
# A non-orange accent, so following the desktop accent is distinguishable from the bronze fallback.
if run_in_session gsettings set org.gnome.desktop.interface accent-color blue >/dev/null 2>&1; then sleep 5; shot "$out/asura-accent-blue.png"; run_in_session gsettings set org.gnome.desktop.interface accent-color orange >/dev/null 2>&1; fi
# Flip to dark and capture again so live theme following is visible.
run_in_session gsettings set org.gnome.desktop.interface color-scheme prefer-dark >/dev/null 2>&1 || true
run_in_session bash -c 'command -v plasma-apply-colorscheme >/dev/null && plasma-apply-colorscheme BreezeDark' >/dev/null 2>&1 || true
sleep 6
shot "$out/asura-dark.png"
run_in_session gsettings set org.gnome.desktop.interface color-scheme default >/dev/null 2>&1 || true
run_in_session bash -c 'command -v plasma-apply-colorscheme >/dev/null && plasma-apply-colorscheme BreezeLight' >/dev/null 2>&1 || true
pgrep -u "$user" -x Asura >/dev/null && echo "asura running" >>"$out/status.txt" || echo "asura not running" >>"$out/status.txt"
pkill -u "$user" -x Asura 2>/dev/null || true
chown -R "$user" "$out"
cat "$out/status.txt"
