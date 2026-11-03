#!/usr/bin/env bash
# Provision the asura-desktop Lima guest: GNOME, KDE Plasma and XFCE side by
# side, GDM autologin for the Lima user, Asura's runtime libraries, the
# packaged linux-arm64 build under /opt/asura, and a passwordless login
# keyring so the Secret Service never prompts.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
user=${1:?lima user}
package_source=${2:?package directory on the host mount}
home=$(getent passwd "$user" | cut -d: -f6)

apt-get update -q
apt-get install -y -q --no-install-recommends \
  ubuntu-desktop-minimal gdm3 gnome-keyring libsecret-tools dbus-x11 \
  xdotool x11-utils x11-xserver-utils imagemagick wl-clipboard \
  fonts-noto-cjk fonts-noto-color-emoji fonts-jetbrains-mono
apt-get install -y -q --no-install-recommends kde-plasma-desktop plasma-workspace spectacle || true
apt-get install -y -q --no-install-recommends xfce4 xfce4-terminal xfce4-screenshooter || true
# Chromium and Avalonia runtime libraries; names vary by release, so each is optional.
for p in libnss3 libnspr4 libatk1.0-0t64 libatk1.0-0 libatk-bridge2.0-0t64 libatk-bridge2.0-0 \
         libatspi2.0-0t64 libatspi2.0-0 libcups2t64 libcups2 libxkbcommon0 libgbm1 libasound2t64 \
         libasound2 libxtst6 libxcomposite1 libxdamage1 libxrandr2 libxfixes3 libxcursor1 libxi6 \
         libxrender1 libice6 libsm6 libfontconfig1 libdrm2 libgl1 libexpat1 libpango-1.0-0 libcairo2; do
  apt-get install -y -q --no-install-recommends "$p" >/dev/null 2>&1 || true
done

mkdir -p /opt/asura
cp -a "$package_source/." /opt/asura/
chown -R root:root /opt/asura
chmod 4755 /opt/asura/chrome-sandbox
chmod 755 /opt/asura/Asura

cat >/etc/gdm3/custom.conf <<EOF
[daemon]
AutomaticLoginEnable=true
AutomaticLogin=$user
EOF

install -d -m 700 -o "$user" -g "$user" "$home/.local" "$home/.local/share" "$home/.local/share/keyrings"
printf 'login' >"$home/.local/share/keyrings/default"
printf '[keyring]\ndisplay-name=login\nctime=0\nmtime=0\nlock-on-idle=false\nlock-after=false\n' \
  >"$home/.local/share/keyrings/login.keyring"
chown -R "$user:$user" "$home/.local/share/keyrings"
chmod 600 "$home/.local/share/keyrings/"*

systemctl set-default graphical.target
systemctl enable gdm3
echo PROVISIONED
