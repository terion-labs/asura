# macOS release rehearsal

This guide covers the local macOS control build and GitHub release dispatch.

## GitHub release authorization

The repository release workflow is manually dispatched from `main`. Supply an
existing `vMAJOR.MINOR.PATCH` tag that points to the exact selected main commit;
other branches and tags pointing elsewhere cannot enter its build pipeline.
The workflow rechecks that identity before sealing release sources and fetches
the remote tag again immediately before publication. Pushing a tag alone no
longer publishes a release.

Before dispatching, configure the `release` GitHub environment with a deployment
branch policy allowing only `main` (no tags) and required release reviewers.
Store these six values **only as environment secrets**: `RELEASE_APPLE_CERTIFICATE_P12_BASE64`,
`RELEASE_APPLE_CERTIFICATE_PASSWORD`, `RELEASE_APPLE_DEVELOPER_ID_APPLICATION`,
`RELEASE_APPLE_NOTARY_ISSUER_ID`, `RELEASE_APPLE_NOTARY_KEY_ID`, and
`RELEASE_APPLE_NOTARY_PRIVATE_KEY_BASE64`. After securely migrating them, remove
the old repository-level `APPLE_*` signing secrets and restrict changes to
`main`/release workflows through repository rules. The workflow uses the new
names so an unmigrated installation fails before signing; adding an environment
name alone does not establish a secret boundary for other workflows.

Configure a tag ruleset for `v*` that restricts creation to release owners and
prevents updates and deletion, including through ordinary writer bypasses.
The final remote check detects a tag moved during the build, but it cannot make
the check and publication atomic or prevent a later tag replacement. Keep this
protection in force after publication so release source archives retain the
verified commit. `gh release create --verify-tag` only checks that a tag exists.

Release packaging builds the Avalonia native bridge in a fresh private tree.
The shared developer cache is not an authority for release binaries. The Native
AOT linker and dependency bottle metadata come from the pinned Homebrew/core
commit recorded in `linker-source.txt`; updating this pin requires reviewing the
formula and dependency changes.

## Prerequisites

The rehearsal requires:
- Apple silicon Mac
- Full Xcode 26 or newer
- GraalVM Native Image 25.0.4
- LLVM `ld64.lld` 22.x
- Apple Developer ID Application certificate and App Store Connect API key

Store local credentials in the ignored `.apple/` directory and configure their shell assignments in the ignored `.env` file at the repository root.

```bash
APPLE_CERTIFICATE_P12_BASE64="$(/usr/bin/base64 -i .apple/developer-id-application.p12 | tr -d '\n')"
APPLE_CERTIFICATE_PASSWORD='replace-with-the-p12-export-password'
APPLE_DEVELOPER_ID_APPLICATION='Developer ID Application: Example Name (TEAMID)'
APPLE_NOTARY_ISSUER_ID='replace-with-app-store-connect-issuer-id'
APPLE_NOTARY_KEY_ID='replace-with-app-store-connect-key-id'
APPLE_NOTARY_PRIVATE_KEY_BASE64="$(/usr/bin/base64 -i .apple/AuthKey_KEYID.p8 | tr -d '\n')"

GRAALVM_HOME='/absolute/path/to/graalvm-25.0.4'
ASURA_XCODE_APP='/Applications/Xcode.app'
ASURA_NATIVE_AOT_LINKER='/opt/homebrew/opt/lld@22/bin/ld64.lld'
```

`APPLE_CERTIFICATE_P12_BASE64` must decode to a password-protected PKCS#12 file containing the Developer ID Application certificate and its matching private key. `APPLE_NOTARY_PRIVATE_KEY_BASE64` must decode to the App Store Connect API `.p8` key used by `notarytool`.

## Rehearsal workflow

Commit the release source, create an annotated tag at `HEAD`, and run the rehearsal script:

```bash
release_tag='v<major>.<minor>.<patch>'

git status --short # Must print nothing
git tag -a "$release_tag" -m "Asura ${release_tag#v}"

./scripts/rehearse-macos-release.sh --tag "$release_tag"
```

The script runs `./scripts/check.sh --full`, builds all native payloads from the tagged source, creates the signed Velopack release, notarizes and staples it, checks it with Gatekeeper, and validates the release evidence.

A passing run writes a receipt under `.git/asura-release-rehearsals/` bound to the tag, commit, and tree. After the rehearsal passes, push the commit and tag:

```bash
git push --atomic origin main "$release_tag"
gh workflow run repository-gate.yml --ref main -f "tag=$release_tag"
```
