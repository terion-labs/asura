#!/usr/bin/env bash
set -euo pipefail

# Builds agent-desktop's C library and its platform helper from the pinned
# upstream commit and stages them for Asura.DesktopAutomation (ADR 0061).
#
# The release flavor is what Asura ships. The two evidence flavors exist only
# for tests: "stub" compiles the adapter that answers every operation with
# not_supported, and "panic" adds the export that panics inside the library so
# the fault boundary can be exercised. Neither is ever packaged.

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repository_dir="$(cd -- "${script_dir}/.." && pwd -P)"
dependencies_dir="${repository_dir}/.deps"
source_dir="${dependencies_dir}/agent-desktop"
artifact_parent_dir="${repository_dir}/native/artifacts"
component_catalog="${repository_dir}/licenses/native-desktop-automation-components.json"
required_exports_manifest="${repository_dir}/native/agent-desktop/required-exports.txt"
dotnet="${ASURA_DOTNET:-${repository_dir}/.dotnet/dotnet}"
dotnet_artifacts_arguments=()
if [[ -n "${ASURA_BUILD_ARTIFACTS_ROOT:-}" ]]; then
    dotnet_artifacts_arguments=(--artifacts-path "${ASURA_BUILD_ARTIFACTS_ROOT}")
fi

# Every pin below is repeated in the component catalog, which is the reviewed
# record; the build refuses to run when the two disagree.
agent_desktop_repository="https://github.com/vercel-labs/agent-desktop.git"
agent_desktop_commit="2b0a7334fc111730454b726e6800aa41cdafc093"
agent_desktop_version="1.2.0"
rust_version="1.89.0"
# The commit rustc 1.89.0 reports for itself on every host. rustup verifies what
# it downloads against the release manifest; this binds the compiler that then
# runs to the reviewed release rather than to whatever answers to that version.
rust_commit="29483883eed69d5fb4db01964cdf2af4d86e9cb2"
# Only a machine without rustup uses this: the installer is then fetched at
# this version, checked against its published SHA-256 and kept inside .deps.
rustup_init_version="1.29.3"
cargo_profile="release-ffi"
# Asura.DesktopAutomation refuses any other major (AgentDesktopLibrary).
abi_major="7"
# The deployment target upstream itself compiles at (crates/macos/build.rs
# default). Its Objective-C bridges are built with -Werror and call AppKit APIs
# deprecated in macOS 11, so a newer target fails the build. A lower minimum is
# compatible with Asura's own macOS 13 floor; rustc raises it to 11.0 on arm64.
upstream_macos_deployment_target="10.15"
license_file="AGENT-DESKTOP-LICENSE"
notices_file="AGENT-DESKTOP-THIRD-PARTY-NOTICES.md"
standard_library_notice_file="RUST-STANDARD-LIBRARY-COPYRIGHT.html"
manifest_file="MANIFEST.sha256"
receipt_file="agent-desktop-build-receipt.json"
target_rid=""
flavor="release"
verify_release_clearance=false
verify_staged=false

usage() {
    cat >&2 <<'EOF'
Usage: ./scripts/build-agent-desktop.sh [--rid <runtime-identifier>] [--flavor release|stub|panic]
       ./scripts/build-agent-desktop.sh --verify-staged [--rid <runtime-identifier>]
       ./scripts/build-agent-desktop.sh --verify-release-clearance

Builds the pinned agent-desktop C library and publishes it under
native/artifacts/<rid>/agent-desktop. Supported targets are osx-arm64, osx-x64,
linux-arm64 and linux-x64; the target must share the host's operating system.
The default target is the current host RID.

The stub and panic flavors are test fixtures. They are published under
native/artifacts/<rid>/agent-desktop-evidence/<flavor> and are never packaged.

--verify-staged builds nothing. It succeeds only when the staged release build
is complete, is unaltered since its receipt was written, was built from the
pins in this script, and is described by the catalog as it stands now.
Packaging runs it before it ships the library and the helper.

--verify-release-clearance builds nothing. It succeeds only when
licenses/native-desktop-automation-components.json records that the component
may be published. Nothing runs it automatically: the catalog state is a
record, and this check is there for whoever wants to confirm it by hand.

Prerequisites: git, python3 and curl. The pinned Rust toolchain is installed
through rustup on first use: the rustup already on this machine when there is
one, otherwise a pinned, checksum-verified rustup kept under .deps/toolchains.
macOS also needs the Xcode command line tools (clang and the macOS SDK build
the Objective-C bridges) and the repository .NET SDK (./.dotnet/dotnet, or
ASURA_DOTNET), which computes the signature-removed digests the receipt
records for the post-signing provenance check; ASURA_BUILD_ARTIFACTS_ROOT
moves that tool's build output. Linux needs a C linker and binutils (cc and readelf,
both from build-essential) and no development packages: the AT-SPI, D-Bus and
X11 clients are Rust crates. Set ASURA_CARGO to use a specific cargo
executable; its rustc must sit beside it and already be the pinned release.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            target_rid="$2"
            shift 2
            ;;
        --flavor)
            [[ $# -ge 2 ]] || { usage; exit 64; }
            flavor="$2"
            shift 2
            ;;
        --verify-release-clearance)
            verify_release_clearance=true
            shift
            ;;
        --verify-staged)
            verify_staged=true
            shift
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

if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is required to read the desktop automation component catalog." >&2
    exit 1
fi
if [[ ! -f "${component_catalog}" || -L "${component_catalog}" ]]; then
    echo "The desktop automation component catalog is missing or linked." >&2
    exit 1
fi

# Validates the catalog's shape and release state. Any further arguments are
# name=value pins the catalog must repeat exactly; "clearance" instead requires
# the state that permits publication.
check_catalog() {
    python3 - "${component_catalog}" "$@" <<'PY'
import json
import sys

path, expectations = sys.argv[1], sys.argv[2:]


def fail(message):
    sys.exit(f"{path}: {message}")


with open(path, encoding="utf-8") as handle:
    catalog = json.load(handle)
if (catalog.get("schemaVersion") != 1
        or catalog.get("format") != "asura-native-desktop-automation-component-catalog-v1"):
    fail("not a desktop automation component catalog")

readiness = catalog.get("releaseReadiness")
clearance = catalog.get("legalClearance")
blockers = catalog.get("releaseBlockers")
if (not isinstance(clearance, bool) or not isinstance(blockers, list)
        or any(not isinstance(blocker, str) or not blocker.strip() for blocker in blockers)):
    fail("legalClearance must be a boolean and releaseBlockers a list of reasons")
cleared = readiness == "READY" and clearance and not blockers
blocked = readiness == "BLOCKED" and not clearance and bool(blockers)
if not cleared and not blocked:
    fail("the release state must be READY with clearance and no blockers, "
         "or BLOCKED without clearance and with its blockers named")

allowed = catalog.get("dependencies", {}).get("licensesAllowed")
if (not isinstance(allowed, list) or not allowed
        or any(not isinstance(license, str) or not license.strip() for license in allowed)):
    fail("dependencies.licensesAllowed must list the accepted license identifiers")

for expectation in expectations:
    if expectation == "clearance":
        if not cleared:
            fail("publication of the desktop automation component is blocked:\n  - "
                 + "\n  - ".join(blockers))
        continue
    name, _, expected = expectation.partition("=")
    value = catalog
    for key in name.split("."):
        value = value.get(key) if isinstance(value, dict) else None
    if isinstance(value, bool) or not isinstance(value, (str, int)) or str(value) != expected:
        fail(f"{name} is {value!r}; scripts/build-agent-desktop.sh pins {expected!r}")
PY
}

if [[ "${verify_release_clearance}" == true ]]; then
    check_catalog clearance
    echo "The desktop automation component catalog permits publication."
    exit 0
fi

case "$(uname -s):$(uname -m)" in
    Darwin:arm64) host_rid="osx-arm64" ;;
    Darwin:x86_64) host_rid="osx-x64" ;;
    Linux:x86_64) host_rid="linux-x64" ;;
    Linux:aarch64|Linux:arm64) host_rid="linux-arm64" ;;
    *)
        echo "Unsupported agent-desktop build host: $(uname -s) $(uname -m)" >&2
        exit 1
        ;;
esac
target_rid="${target_rid:-${host_rid}}"

case "${target_rid}" in
    osx-arm64) cargo_target="aarch64-apple-darwin" ;;
    osx-x64) cargo_target="x86_64-apple-darwin" ;;
    linux-arm64) cargo_target="aarch64-unknown-linux-gnu" ;;
    linux-x64) cargo_target="x86_64-unknown-linux-gnu" ;;
    *)
        echo "Unsupported agent-desktop target RID: ${target_rid}" >&2
        exit 64
        ;;
esac
if [[ "${target_rid%%-*}" != "${host_rid%%-*}" ]]; then
    echo "agent-desktop links the target's system frameworks; build ${target_rid} on its own operating system." >&2
    exit 1
fi
if [[ "${target_rid}" == linux-* && "${target_rid}" != "${host_rid}" ]]; then
    echo "The Linux agent-desktop build links with the host C toolchain; build ${target_rid} on a ${target_rid} host." >&2
    exit 1
fi

case "${target_rid}" in
    osx-*)
        library="libagent_desktop_ffi.dylib"
        helper="agent-desktop-macos-helper"
        helper_package="agent-desktop-macos"
        helper_catalog_key="helper.macosExecutable"
        ;;
    linux-*)
        library="libagent_desktop_ffi.so"
        helper="agent-desktop-linux-clipboard-owner"
        helper_package="agent-desktop-linux"
        helper_catalog_key="helper.linuxExecutable"
        ;;
esac

feature_arguments=()
case "${flavor}" in
    release) artifact_dir="${artifact_parent_dir}/${target_rid}/agent-desktop" ;;
    stub)
        artifact_dir="${artifact_parent_dir}/${target_rid}/agent-desktop-evidence/stub"
        feature_arguments=(--features stub-adapter)
        ;;
    panic)
        artifact_dir="${artifact_parent_dir}/${target_rid}/agent-desktop-evidence/panic"
        feature_arguments=(--features panic-injection)
        ;;
    *)
        echo "Unknown agent-desktop flavor: ${flavor}" >&2
        usage
        exit 64
        ;;
esac

if [[ ! -f "${required_exports_manifest}" || -L "${required_exports_manifest}" ]]; then
    echo "The reviewed agent-desktop export manifest is missing or linked." >&2
    exit 1
fi
if ! LC_ALL=C sort -c -u "${required_exports_manifest}" 2>/dev/null; then
    echo "The reviewed agent-desktop export manifest must be sorted and unique." >&2
    exit 1
fi
if grep -Evq '^ad_[a-z0-9_]+$' "${required_exports_manifest}"; then
    echo "The reviewed agent-desktop export manifest contains an invalid symbol." >&2
    exit 1
fi
# The panic export is never a required one; it is admitted only in the panic
# flavor, by name, below.
if grep -Fxq ad_test_panic_boundary "${required_exports_manifest}"; then
    echo "The reviewed agent-desktop export manifest must not require the panic-injection export." >&2
    exit 1
fi
if [[ -L "${repository_dir}/native" || -L "${artifact_parent_dir}" ]]; then
    echo "The native artifact directory must not be a symbolic link." >&2
    exit 1
fi

hash_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    else
        sha256sum "$1" | awk '{ print $1 }'
    fi
}

file_size() {
    wc -c < "$1" | tr -d '[:space:]'
}

# Release signing rewrites both Mach-O files, so the post-signing provenance
# check (tools/Asura.Packaging) compares them by this digest: Apple's canonical
# signature removal keeps the program content and drops the signature, making
# the identity independent of the later Developer ID signature.
signature_removed_hash() {
    local digest
    digest="$("${dotnet}" run \
        --project "${repository_dir}/tools/Asura.Packaging/Asura.Packaging.csproj" \
        --configuration Release --verbosity quiet \
        ${dotnet_artifacts_arguments[@]+"${dotnet_artifacts_arguments[@]}"} \
        -- native-signature-removed-sha256 "$1")"
    if [[ ! "${digest}" =~ ^[a-f0-9]{64}$ ]]; then
        echo "The signature-removed digest of $(basename -- "$1") was not produced cleanly." >&2
        exit 1
    fi
    printf '%s\n' "${digest}"
}

# The pins the catalog must repeat whether or not the source is checked out.
catalog_pins=(
    "component.identity=agent-desktop-ffi/${agent_desktop_version}"
    "component.sourceRepository=${agent_desktop_repository}"
    "component.sourceCommit=${agent_desktop_commit}"
    "component.sourceTag=v${agent_desktop_version}"
    "component.licenseEvidenceFile=${license_file}"
    "component.abiMajor=${abi_major}"
    "${helper_catalog_key}=${helper}"
    "toolchain.rustVersion=${rust_version}"
    "toolchain.rustcCommit=${rust_commit}"
    "toolchain.cargoProfile=${cargo_profile}"
    "toolchain.licenseEvidenceFile=${standard_library_notice_file}"
    "dependencies.licenseEvidenceFile=${notices_file}"
)

verify_staged_payload() {
    local exported staged_code library_signature_removed="" helper_signature_removed=""
    if [[ "${flavor}" != "release" ]]; then
        echo "Only the release flavor is ever packaged, so only it can be verified." >&2
        exit 64
    fi
    if [[ ! -d "${artifact_dir}" || -L "${artifact_dir}" ]]; then
        echo "No agent-desktop release build is staged for ${target_rid}; run ./scripts/build-agent-desktop.sh --rid ${target_rid}." >&2
        exit 1
    fi
    if [[ "${target_rid}" == osx-* ]]; then
        for staged_code in "${library}" "${helper}"; do
            if [[ ! -f "${artifact_dir}/${staged_code}" || -L "${artifact_dir}/${staged_code}" ]]; then
                echo "The staged ${staged_code} is missing or linked." >&2
                exit 1
            fi
        done
        library_signature_removed="$(signature_removed_hash "${artifact_dir}/${library}")"
        helper_signature_removed="$(signature_removed_hash "${artifact_dir}/${helper}")"
    fi
    python3 - \
        "${artifact_dir}" "${component_catalog}" "${required_exports_manifest}" \
        "${target_rid}" "${agent_desktop_repository}" "${agent_desktop_commit}" \
        "${agent_desktop_version}" "${rust_version}" "${rust_commit}" \
        "${cargo_profile}" "${abi_major}" "${receipt_file}" "${manifest_file}" \
        "${library}" "${helper}" "${license_file}" "${notices_file}" \
        "${standard_library_notice_file}" \
        "${library_signature_removed}" "${helper_signature_removed}" <<'PY'
import hashlib
import json
import os
import re
import stat
import sys

(directory, catalog_path, exports_path, target_rid, repository, commit, version,
 rust_version, rust_commit, profile, abi_major, receipt_name, manifest_name,
 library, helper, license_name, notices_name, standard_library_name,
 library_signature_removed, helper_signature_removed) = sys.argv[1:21]


def fail(message):
    sys.exit(f"{directory}: {message}")


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def staged(name):
    return os.path.join(directory, name)


payload = [library, helper, license_name, notices_name, standard_library_name]
expected_names = sorted(payload + [manifest_name, receipt_name])
if sorted(os.listdir(directory)) != expected_names:
    fail("the staged files are not exactly " + ", ".join(expected_names))
modes = {}
for name in expected_names:
    status = os.lstat(staged(name))
    if not stat.S_ISREG(status.st_mode) or status.st_nlink != 1:
        fail(f"{name} is not a singly linked regular file")
    modes[name] = stat.S_IMODE(status.st_mode)

with open(staged(manifest_name), encoding="utf-8") as handle:
    manifest = handle.read()
if manifest != "".join(f"{sha256(staged(name))}  {name}\n" for name in payload):
    fail(f"{manifest_name} does not describe the staged files")

with open(staged(receipt_name), encoding="utf-8") as handle:
    receipt = json.load(handle)
with open(catalog_path, encoding="utf-8") as handle:
    catalog = json.load(handle)


def recorded(*keys):
    value = receipt
    for key in keys:
        value = value.get(key) if isinstance(value, dict) else None
    return value


expectations = {
    ("schemaVersion",): 1,
    ("format",): "asura-agent-desktop-build-receipt-v1",
    ("flavor",): "release",
    ("targetRid",): target_rid,
    ("catalogSha256",): sha256(catalog_path),
    ("source", "repository"): repository,
    ("source", "commit"): commit,
    ("source", "version"): version,
    ("source", "cargoLockSha256"): catalog.get("dependencies", {}).get("cargoLockSha256"),
    ("toolchain", "rust"): rust_version,
    ("toolchain", "rustcCommit"): rust_commit,
    ("toolchain", "profile"): profile,
    ("toolchain", "panicStrategy"): "unwind",
    ("abiMajor",): int(abi_major),
    ("abi", "requiredExportsSha256"): sha256(exports_path),
    ("abi", "panicInjectionExport"): False,
}
for keys, expected in expectations.items():
    actual = recorded(*keys)
    # bool is an int in Python; a receipt saying 0 must not pass for false.
    if type(actual) is not type(expected) or actual != expected:
        fail(f"the receipt records {'.'.join(keys)} as {json.dumps(actual)}, "
             f"not {json.dumps(expected)}")

# Only a Mach-O build is signed later, so only it records the digest the
# post-signing provenance check compares; the shell recomputed it just now.
signature_removed = {}
if target_rid.startswith("osx-"):
    signature_removed = {"library": library_signature_removed, "helper": helper_signature_removed}
    for key, digest in signature_removed.items():
        if not re.fullmatch(r"[a-f0-9]{64}", digest):
            fail(f"the signature-removed digest of the staged {key} could not be computed")

entries = {
    "library": (library, 0o755),
    "helper": (helper, 0o755),
    "license": (license_name, 0o644),
    "thirdPartyNotices": (notices_name, 0o644),
    "standardLibraryNotices": (standard_library_name, 0o644),
    "manifest": (manifest_name, 0o644),
}
for key, (name, mode) in entries.items():
    described = {
        "file": name,
        "bytes": os.path.getsize(staged(name)),
        "sha256": sha256(staged(name)),
        "mode": f"{mode:04o}",
    }
    if key in signature_removed:
        described["signatureRemovedSha256"] = signature_removed[key]
    if recorded(key) != described:
        fail(f"the receipt does not describe the staged {name}")
    if modes[name] != mode:
        fail(f"{name} has mode {modes[name]:04o}, not {mode:04o}")
PY
    check_catalog "${catalog_pins[@]}" \
        "component.licenseEvidenceSha256=$(hash_file "${artifact_dir}/${license_file}")"
    # The receipt already says so; this reads it from the library itself,
    # because a shipped library that can be told to panic is a
    # denial-of-service switch.
    if ! exported="$(nm -g -P "${artifact_dir}/${library}" 2>/dev/null)"; then
        echo "The staged agent-desktop exports could not be inspected." >&2
        exit 1
    fi
    if awk '{ print $1 }' <<< "${exported}" | grep -Eq '^_?ad_test_panic_boundary$'; then
        echo "The staged agent-desktop release library carries the panic-injection export." >&2
        exit 1
    fi
}

if [[ "${verify_staged}" == true ]]; then
    verify_staged_payload
    echo "The staged agent-desktop release build for ${target_rid} matches its receipt, its manifest and the catalog."
    exit 0
fi

if [[ "${target_rid}" == linux-* ]]; then
    for linux_tool in cc readelf; do
        if ! command -v "${linux_tool}" >/dev/null 2>&1; then
            echo "The Linux agent-desktop build needs ${linux_tool}; install build-essential." >&2
            exit 1
        fi
    done
fi

# A machine without rustup gets one of its own under .deps, the way the Zig
# compiler is kept there for libghostty-vt. Nothing outside the repository is
# written: the toolchains and the crate cache live beside it.
local_rustup_home="${dependencies_dir}/toolchains/rustup"
local_cargo_home="${dependencies_dir}/toolchains/cargo"
install_pinned_rustup() {
    local installer_triple installer_sha installer_dir installer
    case "${host_rid}" in
        osx-arm64)
            installer_triple="aarch64-apple-darwin"
            installer_sha="63752a1919a3a4793173ec70f8f212621a7ce38ba6a246095984482f4de20f9f"
            ;;
        osx-x64)
            installer_triple="x86_64-apple-darwin"
            installer_sha="bc107f1fb0228de7fa32413c89c1b305cea9cd256e94b01150cd054128145429"
            ;;
        linux-arm64)
            installer_triple="aarch64-unknown-linux-gnu"
            installer_sha="9f608c142e0e38a78957d0661ba1dfaf0b6fb37184cfaa4b9a0aa57af6184282"
            ;;
        linux-x64)
            installer_triple="x86_64-unknown-linux-gnu"
            installer_sha="b1c8cb14e30889e7e2999e6b69e62d9e9b9ffc0f4df8180564d306d9680e6c55"
            ;;
    esac
    if ! command -v curl >/dev/null 2>&1; then
        echo "curl is required to install the pinned rustup ${rustup_init_version}." >&2
        exit 1
    fi
    mkdir -p -- "${dependencies_dir}/toolchains"
    installer_dir="$(mktemp -d "${dependencies_dir}/toolchains/.rustup-init.XXXXXX")"
    installer="${installer_dir}/rustup-init"
    if ! curl -fL --proto '=https' --tlsv1.2 --retry 5 --retry-delay 15 --retry-all-errors \
            "https://static.rust-lang.org/rustup/archive/${rustup_init_version}/${installer_triple}/rustup-init" \
            -o "${installer}"; then
        rm -rf -- "${installer_dir}"
        echo "rustup ${rustup_init_version} could not be downloaded." >&2
        exit 1
    fi
    if [[ "$(hash_file "${installer}")" != "${installer_sha}" ]]; then
        rm -rf -- "${installer_dir}"
        echo "The downloaded rustup installer failed its pinned SHA-256 check." >&2
        exit 1
    fi
    chmod 700 "${installer}"
    # The installer otherwise refuses to run beside another Rust on PATH, and
    # would add itself to the caller's shell profile.
    if ! RUSTUP_HOME="${local_rustup_home}" CARGO_HOME="${local_cargo_home}" \
            RUSTUP_INIT_SKIP_PATH_CHECK=yes \
            "${installer}" -y --no-modify-path --default-toolchain none --profile minimal; then
        rm -rf -- "${installer_dir}"
        echo "The pinned rustup ${rustup_init_version} did not install." >&2
        exit 1
    fi
    rm -rf -- "${installer_dir}"
}

cargo="${ASURA_CARGO:-}"
if [[ -z "${cargo}" ]]; then
    if command -v cargo >/dev/null 2>&1; then
        cargo="$(command -v cargo)"
    elif [[ -x /opt/homebrew/opt/rustup/bin/cargo ]]; then
        # Homebrew installs rustup keg-only, so its proxies are not on PATH.
        cargo="/opt/homebrew/opt/rustup/bin/cargo"
    else
        if [[ ! -x "${local_cargo_home}/bin/cargo" ]]; then
            install_pinned_rustup
        fi
        export RUSTUP_HOME="${local_rustup_home}" CARGO_HOME="${local_cargo_home}"
        cargo="${local_cargo_home}/bin/cargo"
    fi
fi
if [[ ! -x "${cargo}" ]]; then
    echo "The cargo executable ${cargo} is unavailable." >&2
    exit 1
fi
toolchain_bin_dir="$(cd -- "$(dirname -- "${cargo}")" && pwd -P)"
cargo="${toolchain_bin_dir}/$(basename -- "${cargo}")"
rustc="${toolchain_bin_dir}/rustc"
if [[ ! -x "${rustc}" ]]; then
    echo "No rustc sits beside ${cargo}; the build cannot identify its compiler." >&2
    exit 1
fi
# cargo runs rustc and the build scripts' tools from PATH.
export PATH="${toolchain_bin_dir}:${PATH}"

# The toolchain is selected by name rather than by the checkout's
# rust-toolchain.toml, so rustup installs only what the build needs and not the
# lint components upstream lists for its own development. The file is still
# checked against the pin below.
export RUSTUP_TOOLCHAIN="${rust_version}"
rustup=""
if [[ -x "${toolchain_bin_dir}/rustup" ]]; then
    rustup="${toolchain_bin_dir}/rustup"
elif command -v rustup >/dev/null 2>&1; then
    rustup="$(command -v rustup)"
fi
if [[ -n "${rustup}" ]]; then
    installed_toolchains="$("${rustup}" toolchain list)"
    if ! grep -q "^${rust_version}-" <<< "${installed_toolchains}"; then
        "${rustup}" toolchain install "${rust_version}" --profile minimal --no-self-update
    fi
fi
cargo_version="$("${cargo}" --version | awk '{ print $2 }')"
rustc_identity="$("${rustc}" --version --verbose)"
rustc_version="$(sed -n 's/^release: //p' <<< "${rustc_identity}")"
rustc_commit="$(sed -n 's/^commit-hash: //p' <<< "${rustc_identity}")"
rustc_host="$(sed -n 's/^host: //p' <<< "${rustc_identity}")"
if [[ "${cargo_version}" != "${rust_version}" || "${rustc_version}" != "${rust_version}" ]]; then
    echo "agent-desktop must be built with Rust ${rust_version}; ${cargo} reports cargo ${cargo_version} and rustc ${rustc_version}." >&2
    exit 1
fi
if [[ "${rustc_commit}" != "${rust_commit}" ]]; then
    echo "rustc ${rust_version} at ${rustc} reports commit ${rustc_commit:-<none>}, not the reviewed ${rust_commit}." >&2
    exit 1
fi
if [[ -z "${rustc_host}" ]]; then
    echo "rustc did not report its host." >&2
    exit 1
fi
if [[ ! -d "$("${rustc}" --print target-libdir --target "${cargo_target}")" ]]; then
    if [[ -z "${rustup}" ]]; then
        echo "The Rust ${rust_version} standard library for ${cargo_target} is not installed." >&2
        exit 1
    fi
    "${rustup}" target add --toolchain "${rust_version}" "${cargo_target}"
fi
standard_library_notice="$("${rustc}" --print sysroot)/share/doc/rust/COPYRIGHT-library.html"
if [[ ! -f "${standard_library_notice}" || -L "${standard_library_notice}" ]]; then
    echo "The Rust toolchain does not carry its standard library notices (share/doc/rust/COPYRIGHT-library.html)." >&2
    exit 1
fi

mkdir -p -- "${dependencies_dir}"
if [[ ! -d "${source_dir}/.git" ]]; then
    rm -rf -- "${source_dir}"
    git init --quiet "${source_dir}"
    git -C "${source_dir}" remote add origin "${agent_desktop_repository}"
    git -C "${source_dir}" fetch --quiet --depth 1 origin "${agent_desktop_commit}"
    git -C "${source_dir}" checkout --quiet --detach FETCH_HEAD
fi
if [[ "$(git -C "${source_dir}" rev-parse HEAD)" != "${agent_desktop_commit}" ]]; then
    echo "The cached agent-desktop checkout is not at the pinned commit ${agent_desktop_commit}." >&2
    exit 1
fi
# Ignored files count as changes: cargo would read a .cargo/config.toml left
# in the checkout whether or not upstream's .gitignore hides it.
if [[ -n "$(git -C "${source_dir}" status --porcelain --untracked-files=all --ignored)" ]]; then
    echo "The cached agent-desktop checkout has local changes." >&2
    exit 1
fi
if ! grep -Eq "^channel[[:space:]]*=[[:space:]]*\"${rust_version//./\\.}\"" "${source_dir}/rust-toolchain.toml"; then
    echo "The pinned agent-desktop checkout no longer selects Rust ${rust_version}; review the toolchain pin." >&2
    exit 1
fi
# Only an unwinding profile lets the library trap a panic and answer
# AD_RESULT_ERR_INTERNAL; with panic = "abort" a Rust panic would take the
# desktop process down with it.
if ! awk -v section="[profile.${cargo_profile}]" '
        $0 == section { inside = 1; next }
        /^\[/ { inside = 0 }
        inside && /^panic[[:space:]]*=[[:space:]]*"unwind"/ { found = 1 }
        END { exit found ? 0 : 1 }
    ' "${source_dir}/Cargo.toml"; then
    echo "The pinned agent-desktop ${cargo_profile} profile does not unwind on panic." >&2
    exit 1
fi
# Apache-2.0 obliges a redistributor to pass on an upstream NOTICE file. The
# pinned commit ships none; a pin that adds one must stage it.
upstream_notice="$(find "${source_dir}" -maxdepth 1 -iname 'NOTICE*' -print -quit)"
if [[ -n "${upstream_notice}" ]]; then
    echo "The pinned agent-desktop checkout now ships $(basename -- "${upstream_notice}"); stage it and record it in the catalog." >&2
    exit 1
fi
cargo_lock_sha="$(hash_file "${source_dir}/Cargo.lock")"
check_catalog "${catalog_pins[@]}" \
    "component.licenseEvidenceSha256=$(hash_file "${source_dir}/LICENSE")" \
    "dependencies.cargoLockSha256=${cargo_lock_sha}"

# Each flavor keeps its own cargo output so a feature change cannot leave a
# stale library behind. The directory is a cache; the checkout stays clean.
build_dir="${dependencies_dir}/agent-desktop-build/${target_rid}-${flavor}"
profile_dir="${build_dir}/${cargo_target}/${cargo_profile}"
cargo_home="${CARGO_HOME:-${HOME}/.cargo}"
# rustc records source paths in panic messages. Mapping the three roots that
# vary between machines keeps the build host's user name and directory layout
# out of a shipped library, and makes two builds of the pin comparable.
remap_separator=$'\x1f'
encoded_rustflags="--remap-path-prefix=${source_dir}=/agent-desktop"
encoded_rustflags+="${remap_separator}--remap-path-prefix=${cargo_home}=/cargo"
encoded_rustflags+="${remap_separator}--remap-path-prefix=${build_dir}=/agent-desktop-build"
if [[ "${target_rid}" == osx-* ]]; then
    # The Apple linker otherwise writes a debug map naming every standard
    # library object under the build host's toolchain directory. Leaving it out
    # at link time keeps the symbol table, which a later strip would damage.
    encoded_rustflags+="${remap_separator}-Clink-arg=-Wl,-S"
else
    # The Rust standard library is shipped with debug information, which the
    # linker would copy into both binaries. The Linux packages carry none;
    # the symbol table stays, as it does on macOS.
    encoded_rustflags+="${remap_separator}-Clink-arg=-Wl,--strip-debug"
fi
(
    cd -- "${source_dir}"
    export CARGO_TARGET_DIR="${build_dir}"
    # Takes precedence over RUSTFLAGS and any rustflags in the user's cargo
    # configuration, so ambient compiler flags cannot reach this build.
    export CARGO_ENCODED_RUSTFLAGS="${encoded_rustflags}"
    # Upstream strips the release-ffi profile. The stripped Mach-O fails to
    # load on current macOS ("mis-aligned LINKEDIT string pool"), and symbols
    # are what makes a field crash readable, so the library ships unstripped.
    export CARGO_PROFILE_RELEASE_FFI_STRIP=false
    # cargo would otherwise honour a compiler, a compiler wrapper or a panic
    # strategy named by the caller's environment or cargo configuration, and
    # the build would no longer be the one verified above.
    export RUSTC="${rustc}"
    export RUSTC_WRAPPER="" RUSTC_WORKSPACE_WRAPPER=""
    export CARGO_PROFILE_RELEASE_FFI_PANIC=unwind
    if [[ "${target_rid}" == osx-* ]]; then
        export MACOSX_DEPLOYMENT_TARGET="${upstream_macos_deployment_target}"
    fi
    "${cargo}" build --locked --profile "${cargo_profile}" --target "${cargo_target}" \
        -p agent-desktop-ffi ${feature_arguments[@]+"${feature_arguments[@]}"}
    "${cargo}" build --locked --profile "${cargo_profile}" --target "${cargo_target}" \
        -p "${helper_package}" --bin "${helper}"
)
if [[ -n "$(git -C "${source_dir}" status --porcelain --untracked-files=all --ignored)" ]]; then
    echo "The agent-desktop build modified its pinned checkout." >&2
    exit 1
fi
for built in "${library}" "${helper}"; do
    if [[ ! -f "${profile_dir}/${built}" ]]; then
        echo "The agent-desktop build did not produce ${built}." >&2
        exit 1
    fi
done

mkdir -p -- "$(dirname -- "${artifact_dir}")"
staging_dir="$(mktemp -d "$(dirname -- "${artifact_dir}")/.asura-agent-desktop.XXXXXX")"
work_dir="$(mktemp -d "${TMPDIR:-/tmp}/asura-agent-desktop.XXXXXX")"
cleanup() {
    rm -rf -- "${staging_dir}" "${work_dir}"
}
trap cleanup EXIT

cp "${profile_dir}/${library}" "${staging_dir}/${library}"
cp "${profile_dir}/${helper}" "${staging_dir}/${helper}"
cp "${source_dir}/LICENSE" "${staging_dir}/${license_file}"
cp "${standard_library_notice}" "${staging_dir}/${standard_library_notice_file}"
chmod 755 "${staging_dir}/${library}" "${staging_dir}/${helper}"
chmod 644 \
    "${staging_dir}/${license_file}" \
    "${staging_dir}/${standard_library_notice_file}"

exported_symbols="${work_dir}/exported-symbols.txt"
if ! nm -g -P "${staging_dir}/${library}" 2>/dev/null \
        | awk '{ print $1 }' \
        | sed -E 's/^_(ad_)/\1/' \
        | LC_ALL=C sort -u > "${exported_symbols}"; then
    echo "The staged agent-desktop exports could not be inspected." >&2
    exit 1
fi
while IFS= read -r required_export; do
    if ! grep -Fxq "${required_export}" "${exported_symbols}"; then
        echo "The staged agent-desktop library is missing required export ${required_export}." >&2
        exit 1
    fi
done < "${required_exports_manifest}"
# The panic export must exist in the panic flavor and nowhere else: a shipped
# library that can be told to panic is a denial-of-service switch.
if grep -Fxq ad_test_panic_boundary "${exported_symbols}"; then
    has_panic_export=true
else
    has_panic_export=false
fi
if [[ "${flavor}" == "panic" && "${has_panic_export}" != true ]] \
        || [[ "${flavor}" != "panic" && "${has_panic_export}" == true ]]; then
    echo "The ${flavor} agent-desktop library has the wrong panic-injection export state." >&2
    exit 1
fi

if [[ "${target_rid}" == osx-* ]]; then
    macho_architecture="${cargo_target%%-*}"
    macho_architecture="${macho_architecture/aarch64/arm64}"
    if ! file "${staging_dir}/${library}" \
            | grep -Fq "Mach-O 64-bit dynamically linked shared library ${macho_architecture}"; then
        echo "The staged agent-desktop library is not a ${macho_architecture} Mach-O dynamic library." >&2
        exit 1
    fi
    if ! file "${staging_dir}/${helper}" \
            | grep -Fq "Mach-O 64-bit executable ${macho_architecture}"; then
        echo "The staged agent-desktop helper is not a ${macho_architecture} Mach-O executable." >&2
        exit 1
    fi
    if [[ "$(otool -D "${staging_dir}/${library}" | tail -n 1)" != "@rpath/${library}" ]]; then
        echo "The staged agent-desktop library has an unexpected install name." >&2
        exit 1
    fi
    # Both binaries may link Apple's own libraries and nothing else, so no
    # library from the build host (Homebrew, a developer SDK) can ride along.
    for linked in "${library}" "${helper}"; do
        unexpected_dependencies="$(
            otool -L "${staging_dir}/${linked}" \
                | tail -n +2 \
                | awk '{ print $1 }' \
                | grep -Fvx "@rpath/${library}" \
                | grep -Ev '^(/System/Library/Frameworks/|/usr/lib/)' \
                || true
        )"
        if [[ -n "${unexpected_dependencies}" ]]; then
            echo "The staged ${linked} links outside the macOS system libraries: ${unexpected_dependencies}" >&2
            exit 1
        fi
    done
    unexpected_exports="$(
        nm -gU "${staging_dir}/${library}" \
            | awk '{ print $NF }' \
            | grep -Ev '^_ad_[a-z0-9_]+$' \
            || true
    )"
    if [[ -n "${unexpected_exports}" ]]; then
        echo "The staged agent-desktop library exports symbols outside the ad_ C ABI." >&2
        exit 1
    fi
else
    elf_machine="x86-64"
    if [[ "${cargo_target}" == aarch64-* ]]; then
        elf_machine="ARM aarch64"
    fi
    if ! file "${staging_dir}/${library}" \
            | grep -Fq "ELF 64-bit LSB shared object, ${elf_machine}"; then
        echo "The staged agent-desktop library is not a ${elf_machine} ELF shared object." >&2
        exit 1
    fi
    if ! file "${staging_dir}/${helper}" \
            | grep -Eq "ELF 64-bit LSB (pie executable|shared object|executable), ${elf_machine}"; then
        echo "The staged agent-desktop helper is not a ${elf_machine} ELF executable." >&2
        exit 1
    fi
    # Both binaries may need the C library's own objects and nothing else, so
    # the packages gain no dependency: AT-SPI, D-Bus and X11 are spoken by the
    # Rust crates linked in, not by a system library.
    for linked in "${library}" "${helper}"; do
        unexpected_dependencies="$(
            readelf -d "${staging_dir}/${linked}" \
                | sed -n 's/.*(NEEDED).*\[\(.*\)\]$/\1/p' \
                | grep -Ev '^(libc\.so\.6|libm\.so\.6|libgcc_s\.so\.1|libpthread\.so\.0|libdl\.so\.2|librt\.so\.1|libutil\.so\.1|ld-linux-(aarch64\.so\.1|x86-64\.so\.2))$' \
                || true
        )"
        if [[ -n "${unexpected_dependencies}" ]]; then
            echo "The staged ${linked} needs libraries outside the C library: ${unexpected_dependencies}" >&2
            exit 1
        fi
    done
    unexpected_exports="$(
        nm -D --defined-only "${staging_dir}/${library}" \
            | awk '{ print $NF }' \
            | grep -Ev '^ad_[a-z0-9_]+$' \
            || true
    )"
    if [[ -n "${unexpected_exports}" ]]; then
        echo "The staged agent-desktop library exports symbols outside the ad_ C ABI." >&2
        exit 1
    fi
fi
for staged_binary in "${library}" "${helper}"; do
    for host_path in "${repository_dir}" "${cargo_home}" "${HOME}"; do
        if [[ "${#host_path}" -gt 1 ]] \
                && LC_ALL=C grep -aFq -- "${host_path}/" "${staging_dir}/${staged_binary}"; then
            echo "The staged ${staged_binary} embeds the build host path ${host_path}." >&2
            exit 1
        fi
    done
done

# The notice is generated from the locked graph of exactly the crates linked
# into the two binaries: normal dependencies for this target, without the
# procedural macros and build scripts that only run at compile time. The two
# evidence features add no dependency, so one graph describes every flavor.
dependency_graph="${work_dir}/dependency-graph.tsv"
(
    cd -- "${source_dir}"
    "${cargo}" tree --locked --offline \
        -p agent-desktop-ffi -p "${helper_package}" \
        --target "${cargo_target}" \
        -e normal,no-proc-macro \
        --prefix none \
        -f $'{p}\t{l}\t{r}'
) > "${dependency_graph}"
notice_summary="$(python3 - \
    "${component_catalog}" \
    "${dependency_graph}" \
    "${cargo_home}/registry/src" \
    "${source_dir}" \
    "${staging_dir}/${notices_file}" \
    "${target_rid}" \
    "${agent_desktop_commit}" \
    "${rust_version}" <<'PY'
import glob
import hashlib
import json
import os
import re
import sys

(catalog_path, graph_path, registry_root, workspace_root, output_path,
 target_rid, source_commit, rust_version) = sys.argv[1:9]


def fail(message):
    sys.exit(f"agent-desktop third-party notices: {message}")


with open(catalog_path, encoding="utf-8") as handle:
    catalog = json.load(handle)
allowed = set(catalog["dependencies"]["licensesAllowed"])
upstream_version = catalog["component"]["identity"].split("/", 1)[1]
license_names = re.compile(
    r"^(LICEN[CS]E|COPYING|COPYRIGHT|NOTICE|UNLICENSE)([-_.].*)?$", re.IGNORECASE)


def permitted(expression):
    # Conservative reading of an SPDX expression: every AND operand must offer
    # an accepted alternative. "MIT/Apache-2.0" is the legacy spelling of OR.
    flattened = expression.replace("(", " ").replace(")", " ")
    for operand in re.split(r"\s+AND\s+", flattened):
        alternatives = [item.strip() for item in re.split(r"\s+OR\s+|/", operand)]
        if not any(alternative in allowed for alternative in alternatives):
            return False
    return True


crates = {}
with open(graph_path, encoding="utf-8") as handle:
    for line in handle:
        line = line.rstrip("\n")
        if line.endswith(" (*)"):
            line = line[:-4]
        if not line.strip():
            continue
        fields = line.split("\t")
        if len(fields) != 3:
            fail(f"unreadable dependency line: {line!r}")
        package, expression, repository = fields
        match = re.fullmatch(r"(\S+) v(\S+)((?: \([^)]*\))*)", package)
        if not match:
            fail(f"unreadable package: {package!r}")
        name, version, qualifiers = match.groups()
        origins = re.findall(r"\(([^)]*)\)", qualifiers)
        if any(os.path.isabs(origin) for origin in origins):
            # A workspace member of agent-desktop itself, covered by its license.
            if not all(os.path.isabs(origin) and
                       os.path.commonpath([origin, workspace_root]) == workspace_root
                       for origin in origins):
                fail(f"{name} {version} is built from outside the pinned checkout")
            continue
        if origins:
            fail(f"{name} {version} does not come from crates.io: {qualifiers.strip()}")
        if not expression.strip():
            fail(f"{name} {version} declares no license")
        if not permitted(expression):
            fail(f"{name} {version} is licensed {expression!r}, which the catalog does not accept")
        crates[(name, version)] = (expression.strip(), repository.strip())
if not crates:
    fail("the dependency graph is empty")

texts = {}
unlicensed = []
for (name, version) in sorted(crates):
    sources = glob.glob(os.path.join(glob.escape(registry_root), "*", f"{name}-{version}"))
    if len(sources) != 1 or not os.path.isdir(sources[0]):
        fail(f"expected exactly one registry source for {name} {version}; found {len(sources)}")
    carried = sorted(
        entry for entry in os.listdir(sources[0])
        if license_names.match(entry)
        and os.path.isfile(os.path.join(sources[0], entry))
        and not os.path.islink(os.path.join(sources[0], entry)))
    if not carried:
        unlicensed.append((name, version))
    for entry in carried:
        with open(os.path.join(sources[0], entry), "rb") as handle:
            text = handle.read().decode("utf-8", errors="replace")
        text = "\n".join(line.rstrip() for line in text.replace("\r\n", "\n").split("\n")).strip("\n")
        digest = hashlib.sha256(text.encode("utf-8")).hexdigest()
        texts.setdefault(digest, (text, []))[1].append(f"`{name}` {version} (`{entry}`)")

lines = [
    "# agent-desktop third-party notices",
    "",
    f"Generated by `scripts/build-agent-desktop.sh` for `{target_rid}` from the locked",
    f"dependency graph of agent-desktop {upstream_version} (commit `{source_commit}`).",
    "Do not edit this file; rebuild the component instead.",
    "",
    "`libagent_desktop_ffi` and the helper beside it are built from",
    "[agent-desktop](https://github.com/vercel-labs/agent-desktop), which is licensed",
    "under Apache-2.0. Its license is shipped as `AGENT-DESKTOP-LICENSE`; upstream",
    "ships no NOTICE file. Both binaries statically link the crates listed below and",
    f"the Rust {rust_version} standard library (MIT OR Apache-2.0), whose own notices are",
    "shipped as `RUST-STANDARD-LIBRARY-COPYRIGHT.html`. Asura builds every one of",
    "them from unmodified source.",
    "",
    "## Linked crates",
    "",
    "| Crate | Version | License | Source |",
    "|---|---|---|---|",
]
for (name, version), (expression, repository) in sorted(crates.items()):
    lines.append(
        f"| `{name}` | `{version}` | {expression} "
        f"| <https://crates.io/crates/{name}/{version}> |")
copyleft = sorted(key for key, (expression, _) in crates.items() if "MPL-2.0" in expression)
if copyleft:
    lines += [
        "",
        "## MPL-2.0 components",
        "",
        "These crates are distributed under the Mozilla Public License 2.0. Their",
        "complete, unmodified source is available from the Source link of each row",
        "above:",
        "",
    ]
    lines += [f"- `{name}` {version}" for name, version in copyleft]
lines += ["", "## License texts", ""]
for digest in sorted(texts, key=lambda key: texts[key][1][0]):
    text, carriers = texts[digest]
    lines += ["Carried by " + ", ".join(carriers) + ":", "", "~~~~text", text, "~~~~", ""]
if unlicensed:
    lines += [
        "## Crates without a license file",
        "",
        "The published source of these crates carries no license file. The license",
        "they declare in the table above applies; its text is available from",
        "<https://spdx.org/licenses/>:",
        "",
    ]
    lines += [f"- `{name}` {version}" for name, version in unlicensed]
    lines.append("")
with open(output_path, "w", encoding="utf-8", newline="\n") as handle:
    handle.write("\n".join(lines))
print(len(crates), len(texts), len(unlicensed))
PY
)"
read -r notice_crate_count notice_text_count notice_unlicensed_count <<< "${notice_summary}"
if [[ ! "${notice_crate_count}" =~ ^[1-9][0-9]*$ \
        || ! "${notice_text_count}" =~ ^[0-9]+$ \
        || ! "${notice_unlicensed_count}" =~ ^[0-9]+$ ]]; then
    echo "The agent-desktop third-party notice was not generated cleanly." >&2
    exit 1
fi
chmod 644 "${staging_dir}/${notices_file}"

# Packaging verifies the staged payload against this manifest, then compares
# what it ships byte for byte with the staged files.
staged_files=(
    "${library}"
    "${helper}"
    "${license_file}"
    "${notices_file}"
    "${standard_library_notice_file}"
)
: > "${staging_dir}/${manifest_file}"
for staged_file in "${staged_files[@]}"; do
    printf '%s  %s\n' \
        "$(hash_file "${staging_dir}/${staged_file}")" \
        "${staged_file}" >> "${staging_dir}/${manifest_file}"
done
chmod 644 "${staging_dir}/${manifest_file}"

receipt_entry() {
    local name="$1" mode="$2" signature_removed="${3:-}"
    printf '{ "file": "%s", "bytes": %s, "sha256": "%s", "mode": "%s"' \
        "${name}" \
        "$(file_size "${staging_dir}/${name}")" \
        "$(hash_file "${staging_dir}/${name}")" \
        "${mode}"
    if [[ -n "${signature_removed}" ]]; then
        printf ', "signatureRemovedSha256": "%s"' "${signature_removed}"
    fi
    printf ' }'
}
macos_deployment_target="null"
library_signature_removed=""
helper_signature_removed=""
if [[ "${target_rid}" == osx-* ]]; then
    macos_deployment_target="\"${upstream_macos_deployment_target}\""
    library_signature_removed="$(signature_removed_hash "${staging_dir}/${library}")"
    helper_signature_removed="$(signature_removed_hash "${staging_dir}/${helper}")"
fi
printf '%s\n' \
    '{' \
    '  "schemaVersion": 1,' \
    '  "format": "asura-agent-desktop-build-receipt-v1",' \
    '  "generator": "scripts/build-agent-desktop.sh",' \
    "  \"catalogSha256\": \"$(hash_file "${component_catalog}")\"," \
    "  \"targetRid\": \"${target_rid}\"," \
    "  \"flavor\": \"${flavor}\"," \
    '  "source": {' \
    "    \"repository\": \"${agent_desktop_repository}\"," \
    "    \"commit\": \"${agent_desktop_commit}\"," \
    "    \"version\": \"${agent_desktop_version}\"," \
    "    \"cargoLockSha256\": \"${cargo_lock_sha}\"" \
    '  },' \
    '  "toolchain": {' \
    "    \"rust\": \"${rust_version}\"," \
    "    \"rustcCommit\": \"${rustc_commit}\"," \
    "    \"host\": \"${rustc_host}\"," \
    "    \"cargoTarget\": \"${cargo_target}\"," \
    "    \"profile\": \"${cargo_profile}\"," \
    '    "panicStrategy": "unwind",' \
    '    "stripped": false,' \
    '    "sourcePathsRemapped": true,' \
    "    \"macosDeploymentTarget\": ${macos_deployment_target}" \
    '  },' \
    "  \"abiMajor\": ${abi_major}," \
    '  "abi": {' \
    "    \"requiredExportsSha256\": \"$(hash_file "${required_exports_manifest}")\"," \
    "    \"requiredExportsCount\": $(wc -l < "${required_exports_manifest}" | tr -d '[:space:]')," \
    "    \"panicInjectionExport\": ${has_panic_export}" \
    '  },' \
    "  \"library\": $(receipt_entry "${library}" 0755 "${library_signature_removed}")," \
    "  \"helper\": $(receipt_entry "${helper}" 0755 "${helper_signature_removed}")," \
    "  \"license\": $(receipt_entry "${license_file}" 0644)," \
    "  \"thirdPartyNotices\": $(receipt_entry "${notices_file}" 0644)," \
    "  \"standardLibraryNotices\": $(receipt_entry "${standard_library_notice_file}" 0644)," \
    "  \"manifest\": $(receipt_entry "${manifest_file}" 0644)," \
    '  "dependencies": {' \
    "    \"linkedCrateCount\": ${notice_crate_count}," \
    "    \"licenseTextCount\": ${notice_text_count}," \
    "    \"cratesWithoutLicenseFile\": ${notice_unlicensed_count}" \
    '  }' \
    '}' > "${staging_dir}/${receipt_file}"
chmod 644 "${staging_dir}/${receipt_file}"
python3 -c 'import json, sys; json.load(open(sys.argv[1], encoding="utf-8"))' \
    "${staging_dir}/${receipt_file}"

# Replace the published directory in one rename so a reader never sees a
# library from one build beside a helper from another.
chmod 755 "${staging_dir}"
retired_dir=""
if [[ -e "${artifact_dir}" ]]; then
    retired_dir="$(mktemp -d "$(dirname -- "${artifact_dir}")/.asura-agent-desktop-retired.XXXXXX")"
    mv -- "${artifact_dir}" "${retired_dir}/previous"
fi
mv -- "${staging_dir}" "${artifact_dir}"
if [[ -n "${retired_dir}" ]]; then
    rm -rf -- "${retired_dir}"
fi

echo "Staged agent-desktop ${agent_desktop_version} (${flavor}) for ${target_rid} at ${artifact_dir#"${repository_dir}/"}."
