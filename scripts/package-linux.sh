#!/usr/bin/env bash
set -euo pipefail
umask 022

# Turns one self-contained Linux publish of Asura.Desktop into the four
# release downloads for its runtime identifier: the portable archive, a
# Debian package, an RPM package and an AppImage. The packages install the
# publish under /opt/asura, put `asura` on the PATH, register the desktop
# entry, icons and AppStream metadata, and give Chromium's sandbox helper its
# setuid bit, so the browser panel runs on kernels that restrict user
# namespaces. The AppImage is built by the pinned Velopack, which also gives
# it in-app updates from the release's `<rid>-stable` channel feed; a
# mounted AppImage cannot carry a setuid helper, so its browser panel needs
# unprivileged user namespaces, and the app says so when they are missing.
#
# The package dependency lists are reviewed by hand. They are the libraries
# the launcher, CEF and the .NET runtime link or dlopen on the supported
# baseline (glibc 2.38: Ubuntu 24.04, Debian 13, Fedora 39 and newer, which
# the CEF shim's __isoc23_strtol binding sets). The .deb names Debian
# packages; the .rpm names sonames, which every RPM distribution provides.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_dir="$(cd -- "${script_dir}/.." && pwd -P)"
assets_dir="${repository_dir}/assets/linux"
rid=""
version=""
publish_dir=""
output_dir=""
install_root="/opt/asura"
app_id="sh.asura.Asura"

usage() {
    cat >&2 <<'EOF'
Usage: ./scripts/package-linux.sh --rid <linux-x64|linux-arm64> --version <x.y.z> --publish <dir> --output <dir>

Packages a self-contained `dotnet publish` output of src/Asura.Desktop into
  Asura-<rid>.tar.gz    the portable archive, unpacking to Asura-<version>-<rid>/
  Asura-<rid>.deb       for Debian 13, Ubuntu 24.04 and newer
  Asura-<rid>.rpm       for Fedora 39, RHEL 10 and newer
  Asura-<rid>.AppImage  for any distribution, with in-app updates, plus the
                        Velopack full package and channel feed it updates from
each with a .sha256 file beside it, in the output directory.

The AppImage is built with the repository's pinned Velopack tool through the
repository SDK (./.dotnet/dotnet, or ASURA_DOTNET); Velopack needs a version
of 0.0.1 or higher and mksquashfs on the PATH.

The publish must have been made with -p:DebugType=None -p:DebugSymbols=false
and a stripped CEF payload (scripts/build-cef-runtime.sh does that), which
this script verifies, so the packages carry no debug symbols.

The publish must also carry the desktop automation library and its helper
from this checkout's native/artifacts/<rid>/agent-desktop
(scripts/build-agent-desktop.sh stages them); the packages add its license
and notices from there.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            rid="$2"
            shift 2
            ;;
        --version)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            version="$2"
            shift 2
            ;;
        --publish)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            publish_dir="$2"
            shift 2
            ;;
        --output)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            output_dir="$2"
            shift 2
            ;;
        --help|-h)
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

case "${rid}" in
    linux-x64)
        deb_arch="amd64"
        rpm_arch="x86_64"
        ;;
    linux-arm64)
        deb_arch="arm64"
        rpm_arch="aarch64"
        ;;
    *)
        echo "--rid must be linux-x64 or linux-arm64." >&2
        usage
        exit 64
        ;;
esac
if [[ ! "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || "${version}" == 0.0.0 ]]; then
    echo "--version must be a release version such as 0.1.96 (Velopack rejects 0.0.0)." >&2
    exit 64
fi
if [[ -z "${publish_dir}" || -z "${output_dir}" ]]; then
    usage
    exit 64
fi
if [[ "$(uname -s)" != "Linux" ]]; then
    echo "Linux packages are built on Linux; dpkg-deb and rpmbuild produce them." >&2
    exit 1
fi
for command_name in dpkg-deb rpmbuild mksquashfs tar sha256sum file ldd cmp python3; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Packaging requires ${command_name}." >&2
        exit 1
    fi
done
dotnet="${ASURA_DOTNET:-${repository_dir}/.dotnet/dotnet}"
if [[ ! -x "${dotnet}" ]]; then
    echo "The AppImage needs the repository SDK at ${dotnet}; run scripts/bootstrap.sh or set ASURA_DOTNET." >&2
    exit 1
fi

publish_dir="$(cd -- "${publish_dir}" && pwd -P)"
# agent-desktop's library is loaded into Asura itself and launches its helper
# from its own directory, so both sit beside the launcher (ADR 0061). The test
# builds under agent-desktop-evidence are never read here.
agent_desktop_dir="${repository_dir}/native/artifacts/${rid}/agent-desktop"
agent_desktop_code_files=(
    libagent_desktop_ffi.so
    agent-desktop-linux-clipboard-owner
)
agent_desktop_legal_files=(
    AGENT-DESKTOP-LICENSE
    AGENT-DESKTOP-THIRD-PARTY-NOTICES.md
    RUST-STANDARD-LIBRARY-COPYRIGHT.html
)
for required in \
    Asura \
    libcef.so \
    libexclr8cef.so \
    asura-cef-helper \
    chrome-sandbox \
    libghostty-vt.so \
    libasura_pty.so \
    "${agent_desktop_code_files[@]}" \
    "runtimes/${rid}/native/asura-sql-language" \
    THIRD-PARTY-NOTICES.md; do
    if [[ ! -f "${publish_dir}/${required}" ]]; then
        echo "The publish at ${publish_dir} is missing ${required}." >&2
        exit 1
    fi
done
if [[ ! -x "${publish_dir}/Asura" ]]; then
    echo "The publish's Asura launcher is not executable." >&2
    exit 1
fi
"${script_dir}/build-agent-desktop.sh" --verify-staged --rid "${rid}"
for required in "${agent_desktop_code_files[@]}"; do
    if [[ -L "${publish_dir}/${required}" || ! -x "${publish_dir}/${required}" ]] \
        || ! cmp -s "${agent_desktop_dir}/${required}" "${publish_dir}/${required}"; then
        echo "The published desktop automation file ${required} is linked, not executable, or not the verified build." >&2
        exit 1
    fi
done
if find "${publish_dir}" -name 'agent-desktop-evidence*' -print -quit | grep -q .; then
    echo "An agent-desktop test build leaked into the publish." >&2
    exit 1
fi
if find "${publish_dir}" -name '*.pdb' -print -quit | grep -q .; then
    echo "The publish carries .pdb files; publish with -p:DebugType=None -p:DebugSymbols=false." >&2
    exit 1
fi
if ! file "${publish_dir}/libcef.so" | grep -q ', stripped'; then
    echo "libcef.so still carries its debug symbols; rebuild the CEF payload with scripts/build-cef-runtime.sh." >&2
    exit 1
fi
# Every library the launcher and the browser engine link must resolve on this
# host, which installs the same packages the dependency lists name. The CEF
# shim links only libcef.so, which it finds beside itself, and libraries
# libcef.so needs too.
for linked in Asura libcef.so asura-cef-helper; do
    if ldd "${publish_dir}/${linked}" | grep -q 'not found'; then
        ldd "${publish_dir}/${linked}" >&2
        echo "${linked} links a library this host does not have." >&2
        exit 1
    fi
done
# The desktop automation library and its helper need the C library alone,
# which their build verified; this holds the published copies to this host.
for linked in "${agent_desktop_code_files[@]}"; do
    if ldd "${publish_dir}/${linked}" | grep -q 'not found'; then
        ldd "${publish_dir}/${linked}" >&2
        echo "${linked} links a library this host does not have." >&2
        exit 1
    fi
done

mkdir -p "${output_dir}"
output_dir="$(cd -- "${output_dir}" && pwd -P)"
work_dir="$(mktemp -d)"
trap 'rm -rf -- "${work_dir}"' EXIT
stage="${work_dir}/root"
release_date="$(date -u +%Y-%m-%d)"

# One staged file system tree feeds both packages, so they install the same
# files with the same modes.
mkdir -p \
    "${stage}${install_root}" \
    "${stage}/usr/bin" \
    "${stage}/usr/share/applications" \
    "${stage}/usr/share/metainfo" \
    "${stage}/usr/share/doc/asura"
cp -a "${publish_dir}/." "${stage}${install_root}/"
find "${stage}" -type d -exec chmod 0755 {} +
find "${stage}" -type f -perm /022 -exec chmod go-w {} +
chmod 4755 "${stage}${install_root}/chrome-sandbox"
# The license of the desktop automation library and the notices of the crates
# and standard library linked into it travel beside it.
for legal in "${agent_desktop_legal_files[@]}"; do
    cp "${agent_desktop_dir}/${legal}" "${stage}${install_root}/${legal}"
    chmod 0644 "${stage}${install_root}/${legal}"
done
ln -s "../..${install_root}/Asura" "${stage}/usr/bin/asura"
cp "${assets_dir}/${app_id}.desktop" "${stage}/usr/share/applications/"
sed -e "s/@VERSION@/${version}/g" -e "s/@DATE@/${release_date}/g" \
    "${assets_dir}/${app_id}.metainfo.xml" > "${stage}/usr/share/metainfo/${app_id}.metainfo.xml"
for icon in "${assets_dir}"/icons/hicolor/*/apps/"${app_id}.png"; do
    size="$(basename "$(dirname "$(dirname "${icon}")")")"
    mkdir -p "${stage}/usr/share/icons/hicolor/${size}/apps"
    cp "${icon}" "${stage}/usr/share/icons/hicolor/${size}/apps/"
done
cp "${repository_dir}/LICENSE" "${stage}/usr/share/doc/asura/copyright"
chmod 0644 \
    "${stage}/usr/share/applications/${app_id}.desktop" \
    "${stage}/usr/share/metainfo/${app_id}.metainfo.xml" \
    "${stage}/usr/share/doc/asura/copyright" \
    "${stage}"/usr/share/icons/hicolor/*/apps/"${app_id}.png"

checksum() {
    (cd "$(dirname "$1")" && sha256sum "$(basename "$1")" > "$(basename "$1").sha256")
}

# The archive name carries no version so the release's "latest" download URL
# is stable, like Asura-macOS-arm64.zip; the directory it unpacks to is
# versioned. It holds the application directory only; the README's install
# steps do what the packages do.
archive="${output_dir}/Asura-${rid}.tar.gz"
tar -C "${stage}/opt" \
    --owner=0 --group=0 --numeric-owner \
    --transform "s,^asura,Asura-${version}-${rid}," \
    -czf "${archive}" asura
checksum "${archive}"

summary="Terminal workspace with an AI agent, browser, and developer tools"
read -r -d '' description <<'EOF' || true
Asura is a native desktop terminal workspace with an integrated AI agent,
embedded browser, and developer tools. One window holds your terminals,
remote servers, Docker containers, Kubernetes clusters, databases, files,
Git repositories, and system monitors.
EOF

deb_root="${work_dir}/deb"
mkdir -p "${deb_root}/DEBIAN"
chmod 0755 "${deb_root}" "${deb_root}/DEBIAN"
cp -a "${stage}/." "${deb_root}/"
installed_size="$(du -sk "${stage}" | cut -f1)"
{
    cat <<EOF
Package: asura
Version: ${version}
Section: devel
Priority: optional
Architecture: ${deb_arch}
Installed-Size: ${installed_size}
Maintainer: Terion Labs <terion-labs@users.noreply.github.com>
Homepage: https://asura.sh
Depends: libc6 (>= 2.38), libgcc-s1, libstdc++6, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74, libudev1, libxcb1, libx11-6, libxext6, libxi6, libxcursor1, libxrandr2, libxfixes3, libxinerama1, libxrender1, libxcomposite1, libxdamage1, libxtst6, libxkbcommon0, libice6, libsm6, libgl1, libgbm1, libdrm2, libdbus-1-3, libglib2.0-0t64 | libglib2.0-0, libnss3, libnspr4, libatk1.0-0t64 | libatk1.0-0, libatk-bridge2.0-0t64 | libatk-bridge2.0-0, libatspi2.0-0t64 | libatspi2.0-0, libcups2t64 | libcups2, libpango-1.0-0, libcairo2, libasound2t64 | libasound2, libexpat1, fontconfig, libfontconfig1, libfreetype6, libsecret-tools
Recommends: libfido2-1, libgssapi-krb5-2
Description: ${summary}
EOF
    printf '%s\n' "${description}" | sed 's/^/ /'
    cat <<EOF
 .
 This package installs the self-contained build under ${install_root} and
 puts the asura command on the PATH. It has no updater: install a newer
 release the same way. Third-party notices are in
 ${install_root}/THIRD-PARTY-NOTICES.md.
EOF
} > "${deb_root}/DEBIAN/control"
deb="${output_dir}/Asura-${rid}.deb"
dpkg-deb --root-owner-group -Zxz --build "${deb_root}" "${deb}" >/dev/null
checksum "${deb}"

rpm_top="${work_dir}/rpm"
mkdir -p "${rpm_top}/SPECS"
{
    cat <<EOF
# The payload is a self-contained .NET and Chromium build: nothing in it is
# a system library, so rpmbuild neither strips it, nor derives provides and
# requires from it, nor splits debug packages from it.
%global __os_install_post %{nil}
%global debug_package %{nil}
%global _build_id_links none
%global _missing_build_ids_terminate_build 0

Name: asura
Version: ${version}
Release: 1
Summary: ${summary}
License: MIT
URL: https://asura.sh
AutoReqProv: no
EOF
    # The launcher, CEF and the .NET runtime link these by soname, or dlopen
    # them (ICU, OpenSSL, OpenGL, the X11 input and session libraries,
    # freetype and the fido2/Kerberos extras).
    echo "Requires: libc.so.6(GLIBC_2.38)(64bit)"
    for soname in \
        libc.so.6 libm.so.6 libpthread.so.0 libgcc_s.so.1 libdl.so.2 \
        libstdc++.so.6 librt.so.1 libutil.so.1 libresolv.so.2 libudev.so.1 \
        libssl.so.3 libcrypto.so.3 \
        libxcb.so.1 libX11.so.6 libXext.so.6 libXi.so.6 libXcursor.so.1 \
        libXrandr.so.2 libXfixes.so.3 libXinerama.so.1 libXrender.so.1 \
        libXcomposite.so.1 libXdamage.so.1 libXtst.so.6 libxkbcommon.so.0 \
        libICE.so.6 libSM.so.6 libGL.so.1 libgbm.so.1 libdrm.so.2 \
        libdbus-1.so.3 libglib-2.0.so.0 libgobject-2.0.so.0 libgio-2.0.so.0 \
        libnss3.so libnssutil3.so libsmime3.so libnspr4.so \
        libatk-1.0.so.0 libatk-bridge-2.0.so.0 libatspi.so.0 libcups.so.2 \
        libpango-1.0.so.0 libcairo.so.2 libasound.so.2 libexpat.so.1 \
        libfontconfig.so.1 libfreetype.so.6; do
        echo "Requires: ${soname}()(64bit)"
    done
    cat <<EOF
Requires: libicu
Requires: fontconfig
Requires: /usr/bin/secret-tool
Recommends: libfido2.so.1()(64bit)
Recommends: libgssapi_krb5.so.2()(64bit)

%description
${description}

This package installs the self-contained build under ${install_root} and
puts the asura command on the PATH. It has no updater: install a newer
release the same way. Third-party notices are in
${install_root}/THIRD-PARTY-NOTICES.md.

%install
mkdir -p %{buildroot}
cp -a "${stage}/." %{buildroot}/

%files
%defattr(-,root,root,-)
${install_root}
/usr/bin/asura
/usr/share/applications/${app_id}.desktop
/usr/share/icons/hicolor/*/apps/${app_id}.png
/usr/share/metainfo/${app_id}.metainfo.xml
%doc /usr/share/doc/asura/copyright
EOF
} > "${rpm_top}/SPECS/asura.spec"
rpmbuild -bb --quiet \
    --define "_topdir ${rpm_top}" \
    --define "_binary_payload w6.xzdio" \
    --target "${rpm_arch}" \
    "${rpm_top}/SPECS/asura.spec" >/dev/null
built_rpm="$(find "${rpm_top}/RPMS" -name "asura-${version}-1.${rpm_arch}.rpm" -print -quit)"
if [[ -z "${built_rpm}" ]]; then
    echo "rpmbuild produced no asura-${version}-1.${rpm_arch}.rpm." >&2
    exit 1
fi
rpm="${output_dir}/Asura-${rid}.rpm"
cp "${built_rpm}" "${rpm}"
checksum "${rpm}"

# The AppImage is the same staged application directory with a distribution
# manifest that tells the app it updates through Velopack from the release's
# channel feed. Velopack lays the directory out as usr/bin inside the AppDir
# and adds its UpdateNix helper and package metadata beside the app, so the
# manifest sits beside the executable, where the app reads it. The packages
# above carry no manifest: they have no update channel.
channel="${rid}-stable"
package_id="sh.asura"
appimage_pack="${work_dir}/appimage/pack"
appimage_release="${work_dir}/appimage/release"
mkdir -p "${appimage_pack}" "${appimage_release}"
cp -a "${stage}${install_root}/." "${appimage_pack}/"
# A mounted AppImage is nosuid, so a setuid bit on the helper would only
# make the app and Chromium trust a helper that cannot gain root; the
# AppImage's browser runs in the namespace sandbox or not at all.
chmod 0755 "${appimage_pack}/chrome-sandbox"
cat > "${appimage_pack}/distribution.json" <<EOF
{
  "schemaVersion": 1,
  "source": "github-release",
  "updateStrategy": "velopack",
  "packageId": "${package_id}",
  "channel": "${channel}",
  "runtimeIdentifier": "${rid}"
}
EOF
chmod 0644 "${appimage_pack}/distribution.json"
(
    cd "${repository_dir}"
    "${dotnet}" tool restore >/dev/null
    "${dotnet}" tool run vpk -- pack \
        --outputDir "${appimage_release}" \
        --channel "${channel}" \
        --runtime "${rid}" \
        --packId "${package_id}" \
        --packVersion "${version}" \
        --packDir "${appimage_pack}" \
        --packAuthors "Asura contributors" \
        --packTitle Asura \
        --mainExe Asura \
        --icon "${assets_dir}/icons/hicolor/512x512/apps/${app_id}.png" \
        --categories "Development;TerminalEmulator" \
        --delta None
)
# The AppImage name carries no version either; the full package and feed
# keep Velopack's names, which the updater in a running copy asks for.
appimage="${output_dir}/Asura-${rid}.AppImage"
full_package="${package_id}-${version}-${channel}-full.nupkg"
feed="releases.${channel}.json"
for expected in "${package_id}-${channel}.AppImage" "${full_package}" "${feed}"; do
    if [[ ! -f "${appimage_release}/${expected}" || -L "${appimage_release}/${expected}" ]]; then
        echo "Velopack did not produce ${expected}." >&2
        exit 1
    fi
done
mv "${appimage_release}/${package_id}-${channel}.AppImage" "${appimage}"
chmod 0755 "${appimage}"
mv "${appimage_release}/${full_package}" "${appimage_release}/${feed}" "${output_dir}/"
checksum "${appimage}"
checksum "${output_dir}/${full_package}"
checksum "${output_dir}/${feed}"

for package in "${archive}" "${deb}" "${rpm}" "${appimage}" "${output_dir}/${full_package}" "${output_dir}/${feed}"; do
    printf '%s\t%s\n' "$(du -h "${package}" | cut -f1)" "$(basename "${package}")"
done
