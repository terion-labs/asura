# macOS release rehearsal

This guide covers running the local macOS control build before pushing a release tag.

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
```
