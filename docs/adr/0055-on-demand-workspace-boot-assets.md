# ADR 0055: On-demand workspace boot assets

The app bundles the signed host runtime, its required Swift libraries, and a
`boot-assets.json` descriptor. It does not bundle the Linux kernel, guest boot
filesystem, or source-distribution archives.

Release packaging produces `Asura-workspace-boot-arm64.zip` alongside the
app. The signed descriptor pins the archive and each image by SHA-256 and exact
size. On first isolated-workspace preparation, the host downloads that asset
from the matching Asura version's GitHub release. This bootstrap download
precedes the guest and does not carry workspace traffic.

Images are cached in the app data directory at `sdk-workspaces/boot/<archive hash>`.
All workspaces share the cache. A cross-process lock serializes provisioning,
downloads and extraction are bounded, and each file is verified before atomic
publication. Only creation of a new environment, including after an explicit
recreation command, may select the current app's descriptor and download images.
Interrupted creation can retry that download. Cache hits work offline.
Cancellation and failures leave no usable partial image. Progress appears in
the existing workspace-preparation view.

Each environment persists its selected boot directory and per-file SHA-256/size
in `boot-images.json`, alongside its writable `rootfs.ext4`. Ordinary startup
verifies those saved files without consulting the new app's descriptor or
downloading replacements. Missing or changed boot files fail with a recovery
message; they never trigger a download or reset. The `disk.ready` receipt prevents
a missing previously completed root disk from being mistaken for first creation.
The selected default OCI image is retained too, even if a later app changes its
default. Installed packages, tooling and guest-only files remain on that disk
across stops, restarts and app updates.

For pre-existing environments without `boot-images.json`, migration adopts only
the boot paths from their saved `runtime.json`. Those files were checked during
the original provisioning, but older versions did not retain their individual
hashes. Migration records a local baseline once, without claiming a new signed
verification or fetching a replacement. Missing paths or files fail closed and
leave the writable disk untouched.

Explicit recreation retires the entire environment directory, including its
disk and boot selection, as a recovery copy. The next creation can use the current
images. Shared cached boot files outlive app replacement and recreation; selecting
images for one workspace does not change any other workspace's selection.

Development uses the same verification path with a locally built sidecar,
selected by the development launcher's `ASURA_WORKSPACE_BOOT_ARCHIVE`.
Changing that path cannot bypass the descriptor's hashes. No development
sidecar is copied into the app.

`Asura-networking-sources.zip` is a separate release asset with the kernel
source, configuration, Kata patches/build material, and OpenConnect source and
relinking instructions. License notices and source-location instructions remain
in the app. Packaging rejects any boot image or source tarball inside the app
and verifies that the boot sidecar matches its signed descriptor before emitting
release assets. Both sidecars and their checksum files are uploaded in the same
release creation operation as the app and updater artifacts.
