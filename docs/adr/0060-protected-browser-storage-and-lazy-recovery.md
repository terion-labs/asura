# ADR 0060: Protected browser storage and lazy recovery

**Status:** Accepted
**Date:** 2026-10-25

## Context

Production startup and shutdown treated Chromium's regenerable caches as
application state. The investigation found roughly 35,000 HTTP/code-cache files.
Archiving, copying and extracting those files dominated the cost of the durable
profile lifecycle. Native AOT does not remove that filesystem work.

This supersedes ADR 0051's complete-tree archival and eager-recovery decisions.
Cookies, local storage, IndexedDB, service workers, CacheStorage and other site
state still need encrypted persistence. HTTP cache and compiled-code cache do not.

## Storage and migration

Signed macOS production builds resolve `UYKDC3566W.sh.asura` through
`NSFileManager.containerURLForSecurityApplicationGroupIdentifier`. The main app,
CEF helper bundles and workspace runtime declare membership. Missing entitlement
or an unavailable container prevents production storage initialization. Development
and explicit throwaway profiles retain their separate namespaces.

The container holds `data`, `cache`, `logs`, browser working trees, recovery
workspaces and `tmp`. Both libc and the managed environment receive `TMPDIR`
before browser initialization. Application keys stay in Keychain. User-selected
projects, downloads and exports stay where the user put them.

Migration holds the legacy single-instance lock and atomically renames owned
entries. It validates ownership and rejects links before tightening old 0755/0644
permissions. It neither overwrites conflicts nor marks incomplete migration as
complete. Retry resumes the remaining renames. Browser recovery variants and
interrupted `.restore`/`.shutdown-snapshot` siblings migrate with their workspace.
The legacy per-user temporary root is obtained with `confstr`, independently of
redirected or inherited `TMPDIR`.

Two non-secret coordination files remain at the legacy location. Keeping the
lock/activation endpoint there prevents an older installed release from opening
the same workspace concurrently. Recovery workspaces migrate when opened under
their own lock; migration never takes ownership of another running workspace.

On macOS 15+, App Group container protection also applies to nonsandboxed apps.
It restricts access by applications outside the group, subject to user-granted
access and system privileges. This is not process-exclusive storage or encryption
of every active database. Earlier supported macOS releases do not provide that
additional protection. See [Apple's container guidance](https://developer.apple.com/documentation/xcode/accessing-app-group-containers).

## Native HTTP cache

The exact pinned CEF 150.0.9 / Chromium 150.0.7871.46 build receives
`EnableCacheEncryption,EnableCacheEncryptionForTesting`. The second feature
bypasses enterprise enrollment in this build; it uses the normal cache-encryption
provider, normal OS-protected keys and encrypted disk backend, not mock keys.
Command-line arguments cannot override the host's switches. Version validation
rejects an unexpected native runtime.

The native probe exercises the shipped framework with a disposable profile,
loopback HTTP fixture and unique Keychain service. It requires encrypted cache
file headers, absence of the response sentinel in plaintext, and a cache hit
from a second process. A third process with a different signing identity must
fetch over HTTP while leaving the cache unchanged after key access is denied.
This test runs in the native-runtime build gate.

Legacy plaintext HTTP cache directories are deleted once before first browser
recovery. The completion marker lives beside the encrypted profile store directory.
Old archive entries for disposable caches are skipped on restoration. There is
no plaintext disk-cache fallback when Chromium cannot obtain its encryption key.

## Durable state and cache retention

Snapshot writers, the macOS stopped-engine copy and archive readers all exclude
known Chromium HTTP, code, GPU and shader cache directories. The rule applies
only at known engine/context cache locations. `Service Worker/CacheStorage` and
site-owned paths named `Cache` remain durable.

Durable state is restored into a runtime profile only when a browser consumer
needs it. Stable hashed profile/partition/route identities retain caches across
runs. Concurrent live routes with the same durable identity use separate slots.
An explicit whole-profile reset also removes retained caches of unopened slots.

Engine restoration remains staged. Only a complete decrypted snapshot becomes
the engine root. Retained cache directories are moved by rename; interruption can
lose disposable caches but cannot replace the last complete encrypted snapshot
with a partial restore. Context preparing/active manifests preserve the same
crash-recovery distinction. Successful sealing deletes durable working files
while leaving regenerable caches in place.

## UI and shutdown

The shell starts without browser recovery. Browser tabs, HTML previews, agent web
tools and explicit browser-data management share an asynchronous initialization
gate. Recovery and profile extraction run on workers. CEF initialization and view
creation still occur on their required UI thread. Plain HTTP agent fetches do not
start CEF. Closing an existing tab cannot block on another profile's extraction.

After quit confirmation, the existing shutdown coordinator keeps Avalonia pumping
while sessions close and durable state is encrypted. Repeated native quit
requests cannot bypass the pending save. Shutdown first prevents new acquisitions,
waits for existing extraction, closes browsers, shuts down CEF and seals context
and engine state. The macOS stopped-engine copy remains a separate process to
avoid disturbing Chromium's retained POSIX database locks. Cache trees are neither
copied nor traversed by that process. CEF's final native shutdown call still runs
on its owning thread; it is not safe to move that call to a worker.

## Validation limits

The native cache/encryption behavior is exercised locally with disposable data.
Managed regression tests cover cache omission, old archives, retention, migration,
interrupted restoration, route isolation and disposal during a blocked restore.
A disposable Native AOT probe verifies the Foundation/Security interop, native
and managed temp redirection, stable legacy-temp resolution, and missing-entitlement
failure. The final Developer ID signed container access check must run in the release
signing environment: this workstation has no valid signing identity. Unsigned
local runs cannot establish macOS's protection guarantees for the production team.
