# Startup and shutdown investigation — 2026-10-25

The strongest measured cause is whole-tree browser-state persistence. Native AOT does not remove this work: a Native AOT probe using the production encrypted store took **23.272 seconds to save an unchanged synthetic browser tree and 13.297 seconds to restore it**. Excluding disposable cache files from the fixture reduced those operations to **1.212 seconds and 0.300 seconds**.

This investigation changes no application behavior. The user's running production workspace was not restarted or closed.

## Scope and evidence

- Installed application: macOS arm64, Asura 0.1.91, build 164. The browser persistence and desktop lifecycle source files examined are unchanged between tag `v0.1.91` and the merged security-fix branch.
- Live browser runtime metadata: approximately 35,840 files, 750 MiB. HTTP cache accounts for 13,937 files / 325.69 MiB; code cache accounts for 21,418 files / 289.41 MiB. Together these disposable caches represent about 98.6% of files and 82% of bytes.
- Encrypted browser container: 2,630,107,136 bytes. This is the allocated container's file length, **not evidence that every byte is read at startup**. Free-page retention or historical archive replacement may contribute; this investigation did not decrypt the production container.
- Five-second sampling of the running production instance did not find a persistent idle deadlock. This does not rule out a shutdown-only deadlock.
- Two launches of the installed production executable used a new numbered recovery workspace. The normal AppKit event loop was observed in samples beginning approximately 6.2 seconds and 5.7 seconds after process launch. Sample processing returned around 10 seconds after launch. These are coarse observations, not exact first-paint measurements. The second launch recovered the empty interrupted test profile. No user connections or credentials were copied into it. Test processes were terminated after sampling; these runs do **not** measure graceful quit.
- A separately identified, ad-hoc-signed copy failed profile preparation and was discarded as an invalid end-to-end test setup. No latency conclusion is drawn from that copy.

## Reproduced persistence cost

An isolated console probe used the repository's compiled `EncryptedBrowserProfileStateStore`, its LiteDB dependency, and a synthetic encryption password. All file contents were generated; production file contents and encryption keys were not read. The probe used the pinned .NET SDK and was then published for `osx-arm64` with Native AOT. The native compilation reported the existing LiteDB trim/AOT warnings; all measured operations completed.

The large fixture combines the aggregate tree into one archive: 485 synthetic state files totaling 135 MiB, plus the observed HTTP/code-cache counts and approximate byte totals. It is a scale reproduction, not a replica of the user's individual browser partitions. The reduced fixture retains those same 485 state files. Each case starts with a fresh container and measures initial save, unchanged replacement save, and restore. Fixture generation and cleanup are outside the timers.

| Fixture | Native AOT initial save | Native AOT unchanged save | Native AOT restore |
| --- | ---: | ---: | ---: |
| Empty | 0.048 s | 0.019 s | 0.011 s |
| Synthetic state, caches omitted | 0.617 s | 1.212 s | 0.300 s |
| Same state plus disposable caches | 18.370 s | 23.272 s | 13.297 s |

The corresponding managed Release run measured 11.400 / 27.060 / 14.083 seconds for the large fixture. These single runs demonstrate substantial work in both modes; machine load and filesystem state prevent using them as an AOT-versus-JIT performance comparison. Neither is a total application launch/quit time.

## Why this becomes visible latency or a freeze

1. `EncryptedBrowserProfileStateStore.WriteArchive` recursively includes virtually every Chromium file, uses `CompressionLevel.NoCompression`, and synchronously copies the complete tree. It excludes process-lock markers, but not HTTP/code/GPU caches. Every save writes a replacement archive even if unchanged. Atomic replacement is valuable and must be retained.
2. `Program.Main` waits for `PrepareAsync` before opening the shell. `InitializeProfileCoreAsync` synchronously recovers browser state. After an interrupted exit, `CefBrowserProfileStore.RecoverOrphanedRuntimeState` can save leftover contexts, delete their runtime files, then restore engine state before the window opens.
3. First browser-context acquisition calls `_stateStore.Restore` synchronously inside `CefBrowserProfileStore.Acquire`. `DesktopBrowserRendererViewFactory.CreateRoutedAsync` invokes acquisition before its first await for local connections. An async method name does not move that work to a background thread.
4. After the desktop event loop exits, `Program` synchronously finalizes services and calls browser shutdown/sealing. `SealRuntimeStateCoreAsync` saves durable contexts synchronously before its first await. The main thread therefore cannot process normal application events during this portion of quit.
5. `BrowserEngineRuntime.Shutdown` closes browsers and manually pumps CEF with sleeps for up to ten seconds. `Cef.Shutdown()` follows outside that timeout. This is not a ten-second bound on total shutdown. The macOS engine snapshot also walks/copies the tree before archiving it; its child-copy timeout is two minutes.
6. Session-host close and disposal iterate engines sequentially. `GhosttyVtTerminalSession.StopAsync` allows five seconds for a PTY exit, then awaits reader/writer completion. Workspace isolation release is also sequential. Several slow sessions can accumulate delays. This risk is established by code inspection, not reproduced with the user's sessions.
7. The normal finalization path is not covered by the later 15-second fallback quiescence deadline. Merely timing out a wait would not safely cancel the underlying work or preserve ownership.

## Recommended order

1. **Exclude known regenerable Chromium caches from encrypted snapshots.** Preserve cookies, local storage, IndexedDB, service-worker/offline application data, preferences, and other durable state. Apply a conservative path policy and migration tests; do not wipe the existing store or disable encryption. The synthetic native test demonstrates the potential scale of this change: roughly 19x less unchanged-save time and 44x less restore time.
2. **Keep non-CEF persistence off the main thread and preserve a responsive closing state.** Await shutdown while the UI loop is still available; keep CEF operations on the owning thread and retain the existing process/lock ownership boundaries. Define stage-specific cancellation/escalation behavior, and close independent sessions with bounded concurrency where graph ordering permits. Do not force exit before durable writes finish.
3. **Move browser recovery/restoration out of the shell's critical path.** Show the shell promptly, restore visible browser contexts asynchronously, and evaluate deferring CEF startup until the first browser panel. Preserve startup-protection semantics and avoid exposing stale plaintext state. Deferring CEF is a separate optimization from removing cache I/O.
4. **Add phase timings and production performance acceptance.** Measure process start to first responsive shell, visible-tab readiness, close request to window dismissal, and close request to process exit. Cover empty, many restored tabs, large caches, interrupted previous exit, slow PTY shutdown, and isolated workspaces. Record file/byte counts without URLs or secrets. This is necessary to attribute the remaining empty-profile startup time and quantify the full quit path.

## Relevant code

Tracked follow-ups: `asura-5hti` (cache persistence), `asura-f99e` (responsive bounded quit), and `asura-1u0l` (startup attribution and critical-path work).

- `src/Asura.Desktop/Program.cs`: preparation before lifetime start; finalization after lifetime exit.
- `src/Asura.Desktop/DesktopBrowserRendererViewFactory.cs`: synchronous context acquisition before the first local-route await.
- `src/Asura.Browser/CefBrowserProfileStore.cs`: recovery, context restore, and shutdown sealing.
- `src/Asura.Files/EncryptedBrowserProfileStateStore.cs`: complete uncompressed archive writes and synchronous extraction.
- `src/Asura.Browser/BrowserEngineRuntime.cs`: CEF close loop and synchronous shutdown.
- `src/Asura.Desktop/BrowserEngineSnapshotCopy.cs`: macOS child-process snapshot copy.
- `src/Asura.SessionHost/InMemorySessionHostClient.cs`: sequential session close/disposal.
- `src/Asura.Terminal/GhosttyVtTerminalSession.cs`: PTY exit wait and reader/writer drain.
- `src/Asura.App/WorkspaceRuntimeLeaseCoordinator.cs`: sequential workspace release.


## Implementation verification

The follow-up implementation is described in [ADR 0060](adr/0060-protected-browser-storage-and-lazy-recovery.md).
The same synthetic Native AOT fixture now archives only durable state even when
all 35,355 disposable HTTP/code-cache files are present. Under concurrent build
and test load, unchanged save took 4.406 seconds and restoration 1.142 seconds,
versus the earlier 23.272 / 13.297 seconds. The durable-only and cache-heavy
fixtures both produced a 286,605,312-byte container; cached payloads no longer
increase its size. These are storage-operation measurements, not whole-app
launch or quit timings, and concurrent load limits direct timing comparisons.

The checked-in Native AOT storage acceptance passed both ordinary restart and
repair of damaged LiteDB file metadata, preserving cookies and Service Worker
CacheStorage while omitting HTTP/code caches. The native CEF cache probe passed
cross-process cache reuse and denied-Keychain access without a plaintext fallback.
A separate Native AOT probe verified native/managed TMPDIR agreement, independent
legacy temp resolution, and missing-entitlement failure. Developer ID signed
container authorization remains release-environment acceptance, tracked by
`asura-cpw3`; this workstation has no valid signing identity.
