# Signed production AOT lifecycle acceptance — 2026-10-26

## Scope and environment

This is a complete desktop application test, using the packaged arm64 Native AOT
executable, Avalonia window, CEF helpers, normal UI navigation, and normal Cmd-Q.
It supplements the component benchmarks in
[the investigation](../startup-shutdown-investigation-2026-10-25.md).

Host: Mac16,5, 128 GiB RAM, macOS 27.0 (26A428). The bundle identifies as
`sh.asura`, version 0.1.92/build 739, signed with Developer ID team UYKDC3566W and
hardened runtime. Packaging used the pinned repository SDK, production build
settings, full Xcode SDK 26.5, and the configured LLD linker. This local acceptance
did not notarize or publish a release. The configured certificate was imported
into an ephemeral keychain, then the keychain was removed and the previous search
list restored. The earlier claim that no signing identity was available was
incorrect: none was installed, but a valid certificate was configured.

The installed `/Applications/Asura.app` and the user's live profile were left
running and untouched. Tests used numbered recovery workspaces `261026001`,
`261026002`, and `261026003`; these are isolated production profiles, not the
non-production throwaway-profile option. The signed app and its CEF helpers used
`~/Library/Group Containers/UYKDC3566W.sh.asura/` successfully.

## Method

A Python launcher recorded process start, exit, and exit code. Native UI automation
opened the app by its exact test bundle path, navigated browser panels, opened
DevTools, and issued Cmd-Q. Quit timing runs from immediately before the UI quit
keypress to process exit. The local fixture server recorded the browser's report
only after checking its storage. Browser-ready timing therefore includes launch,
shell/workspace setup, CEF initialization, profile restoration, and page checks.
It is not a first-paint measurement. Accessibility observation timestamps include
automation delay and are only upper bounds on when the UI became available.

The loopback-only page seeded 32 MiB of cryptographically random IndexedDB data,
a local-storage string, a persistent HttpOnly cookie, a service-worker registration,
and 1 MiB of offline CacheStorage. It also fetched 1,200 cacheable 256 KiB responses
(about 302 MiB on disk). Restarts used the verification URL without the seed
parameter; checks did not recreate missing durable state. SHA-256 checked the
whole IndexedDB value, and server request logs distinguished cache hits from
network fetches. Filesystem inspection was limited to these synthetic profiles.

Evidence is retained locally in `/private/tmp/asura-full-app-20261026/`: package
logs, per-run process records and output, UI timestamps, fixture source, HTTP
request/result events, cache hashes, and `measurements.json`. Early manually
populated runs include time spent investigating the fixture and must not be used
as startup measurements.

## Failures found

1. The workspace-runtime packaging verifier still required only the virtualization
   entitlement, rejecting the newly added App Group entitlement. It now compares
   the actual entitlement dictionary with the complete checked-in entitlement
   plist. Extra or missing entitlements still fail verification. Rebuild, Swift
   tests, and strict artifact verification passed.
2. Opening DevTools makes the request context Chrome's last-used profile. On
   restart, Chromium opened that profile before Asura's lazy restore, creating
   Preferences/Cookies and violating the cache-only restore precondition. Two
   full-app restarts reproduced the failure; a filesystem watcher observed the
   premature writes. Retry recovered the original durable value, but failed
   restoration had discarded the HTTP cache. CEF now explicitly receives
   `profile-directory=Default` before initialization. A native regression probe
   passes with the setting and fails without it. Passing the switch only as an
   application launch argument did not prevent the macOS failure. Tracked as
   `asura-2ty4`.
3. HTTP POST through the workspace forward proxy delivers Content-Length and
   headers but omits the body, so the origin and browser wait indefinitely. A
   direct server self-test returned immediately; the browser request remained
   pending. The HttpForward branch writes headers and only relays the response.
   This independent defect remains open as `asura-h6g5`. The lifecycle fixture
   reports results with GET so this failure does not mask storage validation.
4. Native UI automation inserted extra `A` characters while typing/pasting in a
   terminal. The shell printed the expected lowercase marker and stayed live,
   but exact synthetic input did not pass. This is not yet established as a
   human-input defect; `asura-xsa4` tracks separating automation behavior from
   terminal/IME behavior.

## Initial full-app measurements

| Scenario | Normal quit to process exit | Result |
| --- | ---: | --- |
| Fresh starter workspace | 1.152 s | Exit 0 |
| Reopened starter workspace | 1.001 s | Exit 0 |
| Populated browser, 32 MiB random durable value and ~302 MiB HTTP cache | 2.604 s | Exit 0; durable working files removed; cache retained |
| Browser after manual recovery retry | 1.598 s | Exit 0; all durable checks passed |
| Clean profile restart before DevTools changed last-used metadata | 1.279 s | Exit 0; browser fixture ready 4.441 s after launch |

The first cold UI attachment timed out; it provides no reliable cold first-paint
measurement. A clean empty UI was observed by 6.066 s, including automation delay.
The successful 4.441 s browser-ready run does not validate the DevTools restart
path: that path failed until the CEF startup setting was enforced.

## Rebuilt application results

The fixed bundle is `/private/tmp/asura-full-app-20261026/fixed/Asura.app`.
Its signed Native AOT executable SHA-256 is
`89dd2d0bd51c036014507926fa41619d35a983d245910deb1de8136bcc473da8`.
Strict deep signature verification passed. No startup-profile override was passed
on the command line: the production CEF configuration supplied the fix.

| Scenario | Browser fixture ready after launch | Normal quit to exit | Result |
| --- | ---: | ---: | --- |
| Seeded profile, DevTools opened/closed, local terminal exercised and left live | Not a startup measurement | 1.117 s | Exit 0 |
| First automatic restoration after DevTools | 4.465 s | 1.342 s | Exit 0; no retry |
| Second automatic restoration | 5.338 s | 0.996 s | Exit 0; tabs closed before quit |
| Empty workspace with saved browser data still present | CEF not started | 0.280 s | Exit 0 |

Both automatic restorations reported all checks successful without reseeding:
32 MiB IndexedDB SHA-256
`72ae7ab2784799823e7c39402375ce6f4d68e75872685fb180faff7722fc013a`, the
local-storage string, persistent cookie, service worker, and 1,048,592-byte offline
CacheStorage response. The HTTP fixture and its external script were read without
any corresponding origin request on either restart.

Before quit, the profile had 1,206 HTTP cache files totaling 316,495,546 bytes,
five code-cache files totaling 6,796 bytes, and 33,557,402 bytes of IndexedDB files.
All 1,203 inspected HTTP cache entries had the native encrypted header, and none
contained the fixture's plaintext response marker. All 1,203 entry hashes were
unchanged immediately after the first quit; 1,202 remained unchanged after the
two subsequent launches, with Chromium updating one entry. The same profile slot
was reused. After successful quits, the working profile contained only HTTP,
code, GPU and shader caches; durable files had been removed after sealing.

For the empty launch, the encrypted 78,430,208-byte profile container retained its
exact SHA-256 through launch and quit. There were no child processes or loaded
CEF framework mappings, and durable working files were not extracted. This tests
the lazy boundary with existing saved browser data, rather than only a new empty
profile. UI observation occurred 8.256 s after launch, including automation delay;
it is an upper bound, not an empty-launch benchmark.

The native cache regression also passed trusted cross-process cache reuse and
denied-Keychain behavior. Removing the startup-profile setting made that probe
fail, establishing a negative control. Production packaging reran Native AOT
storage restart/repair acceptance. The repository pre-commit and pre-push hooks
require the quick and full quality gates respectively before these changes land.

## Remaining scope

These tests do not establish unrelated-application denial, signed legacy-profile
migration, virtualization-helper container access, update identity continuity,
or performance of real remote SSH/Kubernetes/VPN sessions. Container acceptance
remains tracked by `asura-cpw3`; exact first-paint instrumentation and broader
terminal/isolation teardown remain `asura-1u0l` and `asura-f99e`.
