#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_dir="$(cd -- "${script_dir}/.." && pwd -P)"
icon_document="${repository_dir}/assets/macos/Asura.icon"
output_icon="${repository_dir}/assets/macos/Asura.icns"
icon_composer_tool="${ICON_COMPOSER_TOOL:-/Applications/Icon Composer.app/Contents/Executables/ictool}"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "The macOS icon build requires macOS." >&2
    exit 1
fi
if [[ ! -x "${icon_composer_tool}" ]]; then
    echo "Install Icon Composer or set ICON_COMPOSER_TOOL to ictool." >&2
    exit 1
fi
if [[ ! -d "${icon_document}" || -L "${icon_document}" ]]; then
    echo "The layered Icon Composer document is missing or linked." >&2
    exit 1
fi
if [[ ! -x /usr/bin/iconutil || ! -x /usr/bin/sips || ! -x /usr/bin/xcrun ]]; then
    echo "The macOS icon utilities are unavailable." >&2
    exit 1
fi

working_directory="$(mktemp -d "${TMPDIR:-/tmp}/asura-macos-icon.XXXXXX")"
cleanup() {
    rm -rf -- "${working_directory}"
}
trap cleanup EXIT

iconset="${working_directory}/Asura.iconset"
mkdir -- "${iconset}"

# The .icns is the fallback for macOS 13 through 15 (and for development
# bundles, which carry no Assets.car). Those releases draw icons on the
# pre-26 grid: an 824-point body centred on a 1024-point canvas, with a soft
# shadow. Icon Composer only exports the macOS 26 and 27 designs, which fill
# the canvas, so one full-size rendition is placed on the older grid and every
# smaller size is resampled from it.
full_bleed="${working_directory}/full-bleed-1024.png"
legacy_grid="${working_directory}/legacy-grid-1024.png"
"${icon_composer_tool}" \
    "${icon_document}" \
    --export-image \
    --output-file "${full_bleed}" \
    --platform macOS \
    --rendition Default \
    --width 512 \
    --height 512 \
    --scale 2 \
    --design-generation 26

pixel_width() {
    /usr/bin/sips -g pixelWidth "$1" | /usr/bin/awk '/pixelWidth:/ {print $2}'
}
pixel_height() {
    /usr/bin/sips -g pixelHeight "$1" | /usr/bin/awk '/pixelHeight:/ {print $2}'
}

if [[ "$(pixel_width "${full_bleed}")" != "1024" || "$(pixel_height "${full_bleed}")" != "1024" ]]; then
    echo "Icon Composer produced an unexpected full-size rendition." >&2
    exit 1
fi

/usr/bin/xcrun swift "${script_dir}/macos-icon-legacy-grid.swift" "${full_bleed}" "${legacy_grid}"

render() {
    local points="$1"
    local scale="$2"
    local suffix=""
    local pixels=$((points * scale))
    if [[ "${scale}" == "2" ]]; then
        suffix="@2x"
    fi

    local output="${iconset}/icon_${points}x${points}${suffix}.png"
    /usr/bin/sips -z "${pixels}" "${pixels}" "${legacy_grid}" --out "${output}" >/dev/null
    if [[ "$(pixel_width "${output}")" != "${pixels}" || "$(pixel_height "${output}")" != "${pixels}" ]]; then
        echo "sips produced an unexpected ${points}pt@${scale}x rendition." >&2
        exit 1
    fi
}

for points in 16 32 128 256 512; do
    render "${points}" 1
    render "${points}" 2
done

candidate="${working_directory}/Asura.icns"
/usr/bin/iconutil --convert icns --output "${candidate}" "${iconset}"
if [[ "$(LC_ALL=C /usr/bin/head -c 4 "${candidate}")" != "icns" ]]; then
    echo "iconutil did not produce an ICNS container." >&2
    exit 1
fi

mkdir -p -- "$(dirname -- "${output_icon}")"
mv -- "${candidate}" "${output_icon}"
echo "Created ${output_icon} from ${icon_document}."
