# ADR 0061: Governed desktop automation through the agent-desktop library

## Status

Proposed — 2026-11-09. Amended during implementation on 2026-11-10; the
amendments are listed under "Amendments during implementation" and the body
below already reads as amended. The family is implemented in the working tree
and composed into the application, and it has not been exercised against a
real applications on 2026-11-11 under the Accessibility grant of a dedicated
evidence runner. The live runs changed two decisions (amendments 27 and 28),
and the last full live run passed every item that needs the grant; only the
signed-update evidence remains outstanding.
Acceptance
requires the threat-model amendments in "Consequences" and the macOS evidence
listed under "Evidence required before acceptance".

- Extends: [ADR 0019](0019-one-action-agent-capability-broker.md) and
  [ADR 0017](0017-native-dotnet-agent-runtime.md)
- Reuses the stale-reference discipline of
  [ADR 0023](0023-governed-native-document-snapshots.md) and
  [ADR 0024](0024-governed-browser-element-click.md)
- Native-dependency pattern:
  [ADR 0040](0040-cross-platform-libghostty-vt-terminal.md) and
  [ADR 0042](0042-cef-off-screen-browser-runtime.md)
- Narrows the "desktop-wide mouse and keyboard tools" rejection in
  [`docs/agent-panel-tool-design.md`](../agent-panel-tool-design.md). That
  rejection concerned coordinate input that can escape the target. This record
  admits semantic, application-scoped element actions only; coordinate input
  stays rejected.

## Context

Asura's agent can operate what Asura hosts: terminals, the embedded browser,
files, databases, Docker, Kubernetes and Git. It cannot see or operate another
application on the same desktop. Users who run Xcode, a password-free native
tool, a vendor GUI, or a dialog that only exists outside Asura have to finish
those steps by hand.

[vercel-labs/agent-desktop](https://github.com/vercel-labs/agent-desktop)
(Apache-2.0, 1.2.0 released 2026-11-05) reads another application's
accessibility tree through the macOS AX API, Windows UI Automation or Linux
AT-SPI, returns it as JSON with snapshot-qualified element refs such as
`@s8f3k2p9:e5`, and acts on a ref. It contains no model and no approval
workflow. Its properties match the boundary Asura already enforces for the
browser:

- refs are deterministic within one snapshot, re-identified strictly before
  every action, and rejected as `STALE_REF` or `AMBIGUOUS_TARGET` instead of
  guessed;
- semantic actions are headless by default and fail closed when no
  accessibility path exists; physical focus or cursor delivery is a separate
  explicit policy;
- every action result carries delivery semantics saying whether input reached
  the target and whether a retry is safe;
- secure fields withhold their values; protected system processes cannot be
  closed; desktop mutations are serialized by a cross-process interaction
  lease.

It ships as a CLI, an MCP stdio server, and a C shared library
(`libagent_desktop_ffi`, ABI major 7, about 4 MB on macOS as Asura builds it,
unstripped) with a colocated clipboard helper. The MCP route needs no code but
maps every call to the single `mcp.call` Mutation descriptor, so a snapshot
read needs the same human click as a button press and nothing binds a click to
its snapshot. A child-process route isolates faults but adds a process
boundary to every observation and reuses no typed result. The library route
gives Asura the richest typed surface and the lowest latency at the cost of
running Rust inside the desktop process.

Same-account processes are not trusted in Asura, and a tool that can click in
any window can click in Asura's own approval card. The design therefore has to
add an authority boundary that no other family needs: the agent must never be
able to observe or operate Asura itself.

## Decision

### One governed family, loaded in-process

Asura adds a `desktop.*` tool family executed through `libagent_desktop_ffi`
loaded into the desktop process. A new project, `Asura.DesktopAutomation`,
owns the interop. It references `Asura.Application` and `Asura.Core` only,
enables `AllowUnsafeBlocks`, and is the only project that names an `ad_*`
symbol. `Asura.Core`, `Asura.Protocol`, `Asura.Application` and `Asura.Agent`
keep their banned-interop rules. `Asura.Desktop` composes the engine and
registers it with SessionHost exactly as it does for the browser, and resolves
it immediately after the service provider is built, before Avalonia or CEF
start their threads.

The library is loaded through `NativeLibrary` from exactly one place: the
application base directory, where the helper executable must sit beside it.
No environment variable, `runtimes/<rid>/native` folder or other candidate is
probed, because loading a file runs its initializers inside the process that
holds the broker and the Accessibility grant, before any acceptance check. A
candidate is accepted only when every required `ad_*` export resolves, the
library's size getters for `AdAction`, `AdExactWindowInfo` and `AdWaitArgs`
equal Asura's mirrors, `ad_abi_version()` reports the compiled major, and
`ad_init(7)` succeeds. A library that fails any check is unloaded and the
family is not advertised; no fallback to a CLI or MCP child exists. An
accepted library is never unloaded.

Before the library is loaded, the engine sets `AGENT_DESKTOP_HOME` through
libc `setenv`, not `Environment.SetEnvironmentVariable`, because the .NET
runtime does not write managed environment changes back to the native
environment on Unix and the Rust library reads the native one. It also
removes `AGENT_DESKTOP_MACOS_HELPER_PATH` and
`AGENT_DESKTOP_INTERACTION_LEASE_FD` with `unsetenv`: the pinned library
would otherwise launch a helper from a path the launch environment names, or
adopt an inherited file descriptor as its interaction lease. A failure of
either step leaves the family unavailable. The state root is
`<active profile data directory>/agent-desktop/`, refused when it or its
`sessions` directory is a symbolic link, and set to mode `0700` and read back.
Refmaps and traces never go to `~/.agent-desktop`, so a CLI installed by the
user does not by default consume refs minted for an Asura run and Asura does
not act on refs minted elsewhere.

One adapter is created per governed run with
`ad_adapter_create_with_session(<name>)`, where the name is a fresh random
`asura-<32 hex>` per adapter, never the run identifier, so a released and
reused run never shares refs with its predecessor. The adapter is destroyed
on Clear, Stop, disposal, run replacement, or an internal fault, and its
session directory is deleted with it. All `ad_*` calls for a run execute on
one dedicated worker thread with a single-consumer queue, so one native call
runs at a time per run; the permission probe has its own adapter. Native
element handles (`AdNativeHandle`, `ad_find_exact`,
`ad_resolve_element_exact`) are not used in the first slice because they are
thread-affine and the JSON entrypoints already cover every advertised tool.

The library is built from a pinned upstream commit with
`--profile release-ffi`, which keeps `panic = "unwind"` so the Rust side traps
panics at the boundary. `AD_RESULT_ERR_INTERNAL` from any call poisons that
run's adapter: the adapter is destroyed, the current action fails with
`desktop_adapter_faulted`, SessionHost ends the run's desktop session, and the
runtime cancels the run. Other runs are unaffected and keep using the same
loaded image. A fault on the permission probe's own adapter withholds the
family until restart.

### Capabilities and risk

Three append-only values join `AgentCapability`, with protocol tokens
`desktop_data`, `desktop_interaction` and `desktop_control`:

| Capability | Risk of its tools | Default |
|---|---|---|
| `DesktopData` | Observation; `desktop.wait` is Routine | Off |
| `DesktopInteraction` | Mutation | Off |
| `DesktopControl` | Destructive | Off |

All three default Off in `InitialPermissions`, in durable workspace and screen
definitions, in recovery and in checkpoint history through a
`WithMissingDesktopPermissionsOff` upgrade, so an existing policy gains no
authority. `Auto` authorizes `DesktopData` only. The broker escalates every
`DesktopInteraction` and `DesktopControl` action to exact `HumanApproval`, and
the SessionHost desktop gate independently accepts only `HumanApproval` or an
explicitly confirmed run-local `YoloPolicy` for them. `agent.request_capability`
may offer `DesktopData` and `DesktopInteraction` under its ordinary Off-to-Ask
rule; it never offers `DesktopControl`, which the user must enable in policy.

### Closed tool set

The first slice exposes these tools and nothing else:

- `DesktopData`: `desktop.list_apps`, `desktop.list_windows`,
  `desktop.snapshot`, `desktop.wait`.
- `DesktopInteraction`, all requested headless: `desktop.click` (click,
  double, right), `desktop.set_value`, `desktop.type_text`, `desktop.clear`,
  `desktop.select`, `desktop.toggle` (toggle, check, uncheck),
  `desktop.expand` (expand, collapse), `desktop.scroll` (scroll, scroll-to).
- `DesktopControl`: `desktop.press_key`, `desktop.focus_window`,
  `desktop.launch_app`, `desktop.close_app`.

Every interaction and `desktop.press_key` names one snapshot-qualified ref.
`desktop.press_key` accepts one key from a closed list plus at most one
modifier; held key edges (`KEY_DOWN`, `KEY_UP`) are not exposed. Asura
requests the headless policy for it like every element action, and the pinned
library raises a key press to its focus-fallback minimum, which may bring the
target window forward but never moves the cursor. Snapshots accept only the
application-owned surfaces `Window`, `Focused`, `Menu`, `Menubar`, `Sheet`,
`Popover` and `Alert` of the named application. The system surfaces
(`Desktop`, `Dock`, `Spotlight`, `Menu bar extras`, `Taskbar`, `System tray`,
`Start menu`, `Action center`, `Quick settings`, `Notification center`) are
not representable in the request type.

Deferred, each requiring an amendment with its own evidence: headed and
focus-fallback delivery for anything but the library's own `press_key`
minimum, coordinate mouse events and wheel, drag, hover, clipboard read or
write, notification listing or dismissal, screenshots, window move, resize,
minimize, maximize and restore, trace export, and native element handles.

### Target binding and the self-operation guard

The family is advertised for the Workspace target only, like workspace layout
mutations. The run does not pin an external application. Instead every
element and focus proposal binds the exact external target inside its
proposal digest: the application's display text and identity (the bundle
identifier the library lists, else the resolved executable path), the process
ID with agent-desktop's process-generation token, the window identity and
title returned by the snapshot envelope or listing, the snapshot ID and ref,
the element role, name and secure flag, the action kind, and the canonical
argument material. The observation time is not bound. The approval card shows
the trusted action title, the application name, `bundle_id` or `executable`,
the process ID, the window title, the element role and name from the
snapshot, and `snapshot_age` or `listing_age`, read as "<n> s when proposed".
Window titles, element names and application names are untrusted provider
content: strict Unicode, 128 UTF-8 bytes, secret-shaped text redacted.
Only refs and windows that reached the model in a result can be acted on;
entries trimmed from the JSON it was shown are never actionable.

`desktop.close_app` binds the running application the run observed, not a
name. Its `app` argument is only a lookup key: the runtime's observation
ledger (`DesktopAgentObservationLedger.ResolveClose`) builds an
`AgentDesktopApplicationTarget` from a `desktop.list_apps` or
`desktop.list_windows` result or a snapshot the run received, with the
process ID, the application identity, the generation token when a window of
that process carried one, and the observation time. A name the run has not
observed, or one that names two observed processes, is refused before
authority as `desktop_stale_ref` or `desktop_ambiguous_target`. The digest
binds the application text, identity, process ID and generation token; the
card shows the process ID, `bundle_id` or `executable`, and
`observation_age`. Immediately before the native call the engine
(`AgentDesktopRunSession.ResolveObservedApplication`) requires that the live
process with that ID still has the observed identity, that its listed display
name selects exactly that process, and that its first live window still
carries the bound token; otherwise the close is `desktop_stale_ref` with
nothing delivered. ABI 7 still closes by name, so the library looks the name
up again inside the call, and a close after which another process answers to
that name is reported as an unknown outcome.

`desktop.launch_app` binds the application identifier the model supplied and
its timeout. An application that is not running has no observed process or
identity to bind.

The `Asura.DesktopAutomation` engine refuses, before any approval card and
without an override, any observation or action whose target process is Asura:
the desktop process itself, every CEF helper, the workspace runtime, the
sql-language worker, the connection backend, any process whose executable
lives inside Asura's bundle or install root, any process with Asura's bundle
identifier, and on macOS any direct child of the desktop process.
`desktop.list_apps` and `desktop.list_windows` omit them. A proposal that
resolves to one of them fails with `desktop_self_target`, is audited `denied`,
and reaches no approval card. The runtime runs this pre-authority check only
when the run's policy leaves the tool's capability on: under `Off` the broker
denies the proposal by policy and no native listing runs. The check is
repeated after permit consumption, immediately before dispatch; a self-target
found there is audited `failed` with `desktop_self_target`, because the audit
chain has no transition from `started` to `denied`. A launch is classified
after the fact, because nothing exists to check before it: the engine takes a
fresh application listing and judges the launched process ID together with the
bundle identifier that listing reports for it. A launch that started Asura
fails with `desktop_self_target_launched`, is audited `failed`, is never
retried, and reconciles like an unknown outcome; a launched process that cannot be
inspected, or a listing that cannot be read, is an unknown outcome. The engine
dispatches an element action only for a ref whose snapshot this run's adapter
took, of the same process, generation and window the proposal names, while a
live window of that process still carries the generation token.

`desktop.close_app` additionally honours agent-desktop's protected-process
refusal, and the runtime rejects, with `desktop_unsaved_changes`, a process
that any snapshot the run still holds showed with a sheet.

### Authorization and dispatch

The ordinary one-action path applies without variation. A trusted composer in
`Asura.Application` turns the closed typed request into an
`AgentDesktopAction`, which pairs the request with its `AgentActionProposal`
and has no public constructor. SessionHost exposes the family through
`IAgentDesktopSessionHost` with four members: the access state, the
pre-authority target check, the governed run of one prepared action, and the
release of a run's desktop session. It re-resolves the run, consumes one
permit, re-runs the self-target guard, invokes exactly one engine call and
records exactly one outcome. Element actions use the fixed 5,000 ms auto-wait
budget; a request can neither widen nor narrow it. `desktop.wait` (at most 30
seconds) and `desktop.launch_app` (at most 15 seconds) carry their own
timeouts. The host never retries an `ad_*` call.

Outcome mapping follows the delivery semantics the library reports:

- `not_delivered`: a failed tool result with the closed stable code. It is
  retryable for `desktop_stale_ref` (which also covers an element or snapshot
  the library no longer knows), `desktop_ambiguous_target`,
  `desktop_timeout_before_delivery`, `desktop_policy_denied`,
  `desktop_permission_denied`, `desktop_app_not_found`,
  `desktop_window_not_found` and `desktop_not_supported`, and not retryable
  otherwise. `desktop_policy_denied` means the library's own refusal of a
  request it was given; a rejected, replayed or foreign authorization is the
  host's non-retryable `invalid_request`, and an expired one carries
  `authorization_expired` or `approval_expired`, from which the runtime
  renews the approval as it does for every other family.
- An element action whose input was delivered: `succeeded`. With a verified
  effect it is audited `desktop_completed`; delivered without verification,
  which is what the library reports for every plain button press, it is
  audited `desktop_delivered_unverified` and the model sees
  `verified: false` with the instruction to snapshot rather than repeat. In
  both cases the window's refs expire. An element action the library answers as successful
  with `not_delivered`, because the element was already in the requested
  state (a checked box to check, an expanded node to expand), also
  succeeds: nothing reached the application, the audit code is
  `desktop_already_satisfied`, the model sees `delivery: "not_delivered"`,
  `verified: false` and `already_in_state: true` under the stable code
  `desktop_action_completed`, and the window's refs do not expire.
  `desktop.focus_window` and `desktop.close_app` succeed with "requested"
  semantics, because neither entrypoint can verify its effect, and are
  audited with `desktop_requested`. `desktop.launch_app` succeeds with what
  the engine observed of the launched process.
- An element action with `uncertain` or `unknown` delivery, a
  timeout after a step may have changed the element, an invalid or oversized
  envelope, caller cancellation after dispatch began, or any exception that
  escapes the engine after a mutation was handed to it: non-retryable
  `desktop_action_outcome_unknown`, audited `failed`, never redispatched. An
  escaping exception also ends the run's desktop session.
- `AD_RESULT_ERR_INTERNAL`: non-retryable `desktop_adapter_faulted`, audited
  `failed`, never redispatched; the run is cancelled as described above.

After either of the last two outcomes the runtime commits the result, skips
the remainder of the stale proposal batch as
`tool_batch_reconciliation_required`, stops an `agent.run_sequence` at that
step, and requires a fresh `desktop.snapshot` before any further desktop
mutation except `desktop.launch_app`. A launch acts on nothing the run
observed, may be what makes a snapshot possible again, and stays an exactly
approved `DesktopControl` action; it does not itself lift the requirement.
The requirement survives Stop and Clear, because ending a run says nothing
about what an uncertain action did.

The same fresh-snapshot requirement follows any other failure of a mutation
that the host received, unless its code proves that nothing was delivered:
the closed not-delivered codes, `desktop_self_target` for anything but a
launch, and the authorization refusals `target_changed`,
`deadline_exceeded`, `approval_expired` and `authorization_expired`
(`GovernedAgentRuntime.ProvesNothingWasDelivered`). Such a failure is not
retryable. A completion the audit trail could not record
(`completion_audit_unavailable`) is one of them: it still goes through the
runtime's general completion-audit quarantine, and the runtime also discards
every observation. Neither is a reconciliation code, so the rest of the batch
is not skipped as `tool_batch_reconciliation_required`. A plain
`desktop_self_target` is always a refusal before any input and proves nothing
was delivered; a launch that reached Asura has its own reconciliation code,
`desktop_self_target_launched`.

Snapshot refs expire two minutes after the snapshot, on run Clear or Stop,
when the same window is snapshotted again, and when a mutation to the same
window succeeds by delivering input; an element action answered as already
satisfied leaves them valid. Refs for other windows survive. Observations are
bounded to 512 nodes and depth 32 by `max_depth` plus host trimming, measured
as the escaped JSON envelope and reduced to at most 64 KiB, with
`interactive_only` and `compact` selectable by the model. `desktop.type_text`
and `desktop.set_value` accept at most 2,048 UTF-8 bytes of strict Unicode and
reject likely literal secrets before approval. `desktop.set_value`,
`desktop.type_text`, `desktop.clear` and `desktop.press_key` are refused for
an element the snapshot marks secure; the user types credentials themselves.
`desktop.click`, `desktop.select`, `desktop.toggle`, `desktop.expand` and
`desktop.scroll` still accept one. Audit details contain the run, capability,
risk, decision, target-identity digest, action kind, stable result code and
durations. They never contain tree content, element names, window titles,
typed text, or application names.

### Platform scope

The family is advertised only where the library loads and
`ad_check_permissions` reports the platform grant; the runtime asks again
before every desktop call. macOS on Apple Silicon is the release target.
Settings › AI shows the Accessibility grant state and, on macOS, opens System
Settings to the list where it is given, which the row names "Accessibility"
through macOS 26 and "Device Control and Data Access" from macOS 27; Asura
never requests the grant on the agent's behalf. The policy editors do not
offer `Auto` for `DesktopInteraction` or `DesktopControl`, where it would
authorize nothing beyond `Ask`, and open a stored `Auto` for either as
`Ask`. No Screen Recording grant is requested because screenshots
are deferred. Linux on X11 or Wayland through AT-SPI is a development target
behind the same probe; it needs the session D-Bus and `libatspi2.0` that the
Linux packages already depend on. Windows advertises nothing until Asura
ships there; it is `notApplicable`, not passed.

The library learns which applications run from AppKit's
`NSWorkspace.runningApplications`, which AppKit refreshes only on the main
run loop, and Asura calls it from worker threads. The application's main
thread runs AppKit, so it sees every launch. A process whose main thread does
not service that run loop, such as a `dotnet test` host, never sees an
application launched after the library's first inventory read: its windows
are reported missing. Live evidence therefore runs in the evidence
assembly's own host, whose main thread services the run loop while the tests
run on worker threads (`EvidenceHost`).

### Packaging and provenance

`scripts/build-agent-desktop.sh` builds `agent-desktop-ffi` and the platform
helper from the pinned upstream commit with the upstream
`rust-toolchain.toml` (Rust 1.89) and `cargo build --locked --profile
release-ffi`, then stages the library, the helper, the upstream `LICENSE`, a
generated third-party notice from the dependency graph, the Rust standard
library notice, and a receipt binding commit, toolchain, profile, ABI, export
manifest, and every staged file's SHA-256, size and mode into
`native/artifacts/<rid>/agent-desktop/`. For a Mach-O build the receipt also
records each binary's `signatureRemovedSha256`, which the post-signing
provenance check compares. `src/Asura.Desktop/Asura.Desktop.csproj` copies
the library and helper from the directory of its effective runtime
identifier (`AsuraEffectiveRuntimeIdentifier`), and only when both are
staged there; the interop project copies nothing, because the SDK builds it
without the application's runtime identifier. Two evidence flavors, `stub` and
`panic`, are staged under `agent-desktop-evidence/` and are never packaged.
`licenses/native-desktop-automation-components.json` records identity, source
and license; it is `BLOCKED` pending review as a record, like
`licenses/native-terminal-components.json`, and nothing enforces that state.
Release binaries from GitHub are not consumed; they are ad-hoc signed and
carry no reviewed provenance.

On macOS the dylib and `agent-desktop-macos-helper` are placed in
`Contents/MacOS` beside `libghostty-vt.dylib`, added to the required nested
code list in `scripts/sign-notarize-macos.sh`, and signed with the team
identity, hardened runtime and timestamp; the signing script checks after
signing that both carry the team, the hardened runtime and no entitlements.
No new entitlement is required: Accessibility is a TCC grant to the
application bundle. On Linux both files sit beside the `Asura` executable in
the tarball, `.deb`, `.rpm` and AppImage.

## Consequences

- Asura reverses a documented rejection for the semantic, application-scoped
  subset only. Coordinate input remains rejected; headed delivery remains
  deferred.
- The agent-to-tool threat model gains at least three threats before this
  record can be accepted: the agent observes or operates Asura itself or a
  process that holds Asura's authority; a committed desktop action has an
  unknown outcome and the model repeats an irreversible effect in another
  application; refmap state on disk is shared with, or forged by, another
  agent-desktop consumer. Section 2 must state that external applications are
  untrusted content sources, that an external application cannot be bound as a
  run target, and that the desktop family provides no human-override lease:
  the user's own keystrokes do not revoke an in-flight headless action the way
  they revoke terminal input. The amendment is T28 to T39 and findings D1 to
  D16 in [`docs/security/agent-tool-threat-model.md`](../security/agent-tool-threat-model.md).
- Rust enters the native build. The toolchain is pinned by the upstream
  manifest, and the build runs through the same receipt and catalog records as
  libghostty-vt and CEF. `scripts/build-agent-desktop.sh` refuses a linked
  crate whose license expression offers none of the identifiers that
  `licenses/native-desktop-automation-components.json` allows in
  `dependencies.licensesAllowed`: `Apache-2.0`,
  `Apache-2.0 WITH LLVM-exception`, `MIT`, `MPL-2.0`, `Unicode-3.0` and
  `Unlicense`. That list is what the build accepts, not what is linked. The
  generated `AGENT-DESKTOP-THIRD-PARTY-NOTICES.md` of each runtime
  identifier is the authority for the crates actually linked and their
  licenses; for `osx-arm64` it lists 34 crates: eight under MIT, 25 under MIT
  or Apache-2.0, and `memchr` under Unlicense or MIT. None is under MPL-2.0
  or Unicode-3.0.
- A Rust panic becomes an in-process fault that ends the run's desktop
  session and cancels the run; other runs continue on the same image. A hard
  abort inside the library would take the desktop process with it; the
  pinned profile is the only defence, and the upstream default `release`
  profile must never be used. The pinned library also reports every
  non-permission I/O or JSON error as `INTERNAL`, so a full disk or a corrupt
  file under the state root faults a run the same way.
- `AgentDesktopExecutionBoundaryTests` mirrors the browser boundary: only the
  interop project names an `ad_*` entrypoint; only SessionHost calls the
  engine; the agent runtime reaches the desktop only through
  `IAgentDesktopSessionHost`, whose four members accept no permit, proposal,
  string, object, JSON element or delegate; `AgentDesktopAction` has no public
  constructor and only the composer creates one; requests carry no approval
  or authorization identity; the host consumes the permit before its single
  engine call, repeats the guard, and contains no loop or retry; and no
  production code reads the environment to locate the library.
  `ProjectDependencyTests` admits `Asura.DesktopAutomation` as a domain
  project that references only `Asura.Application` and `Asura.Core`, declares
  interop, and is referenced only by `Asura.Desktop`.
- Every non-Observation tool, `desktop.wait` included, has an
  `authority.<tool>` case in
  `scripts/acceptance/security-campaign/cases.v1.json`, plus one case proving
  `desktop_self_target` for Asura's own window and one for a CEF helper.
- Application targeting at snapshot time is by name, because ABI 7 exposes no
  snapshot by process or window ID. Two running applications that answer to
  one name are refused as ambiguous; the walked window is checked against the
  resolved process after the walk; and a ref minted for one process cannot be
  dispatched under another. `desktop.close_app` is bound to an observed
  process, but ABI 7 also closes by name only, so the library resolves the
  name once more inside the call.
- The interaction lease that agent-desktop takes serializes agent-desktop
  consumers, not humans. A headless action lands even if the user is typing
  in the same application at that moment. This is residual risk recorded in
  the threat model, mitigated only by the one-action approval and the
  five-second auto-wait ceiling.

## Amendments during implementation

Each entry records a decision taken, or a deviation found, while the family
was built, with its reason. The body above already reads as amended.

1. **Fault scope.** `AD_RESULT_ERR_INTERNAL`, a trapped panic included,
   poisons the run's adapter and cancels that run; other runs are unaffected.
   The original text disabled the family until restart. A per-run latch keeps
   one faulted application or one corrupt reference map from withdrawing the
   family from every other run, and the library image cannot be unloaded
   safely in either design. Only a fault on the permission probe's own
   adapter still withholds the family until restart.
2. **Host interface.** `IAgentDesktopSessionHost` has four members (access
   state, pre-authority target check, governed run, release), and the
   prepared type is `AgentDesktopAction`, not `PreparedDesktopAction`. The
   original text named one method taking `(AgentAuthorizationId,
   PreparedDesktopAction, CancellationToken)`. The pre-authority check, the
   access read and the release each need the host, and the boundary tests
   pin all four members to accept no permit, proposal or free-form value.
3. **Self-target after permit consumption.** A self-target found by the
   repeated guard is audited `failed` with `desktop_self_target`. Before
   authority it is audited `denied`. The audit state machine has no
   transition from `started` to `denied`.
4. **Toolbar removed.** `Toolbar` is no longer an accepted snapshot surface:
   the pinned library does not offer it.
5. **Success semantics.** Element actions succeed on delivered input,
   verified or not (amendment 27), or, since amendment 21, when the library
   reports the element already in the requested state with nothing
   delivered; only `uncertain` or `unknown` delivery is
   `desktop_action_outcome_unknown`.
   `desktop.focus_window` and `desktop.close_app` succeed with "requested"
   semantics, because their entrypoints answer with a status alone and could
   otherwise never succeed.
6. **Loader candidates.** The library is loaded from the application
   directory only, with the helper beside it. The `ASURA_AGENT_DESKTOP_PATH`
   override and the `runtimes/<rid>/native` candidate are removed: a path
   taken from the environment is code injection for anything that can set a
   variable before launch, and the helper must sit beside the library.
   Asura also removes the library's own `AGENT_DESKTOP_MACOS_HELPER_PATH` and
   `AGENT_DESKTOP_INTERACTION_LEASE_FD` from the native environment.
7. **`desktop.wait` is Routine.** It stays `DesktopData` with Routine risk,
   not Observation: it holds the run's worker for up to 30 seconds. `Auto`
   authorizes both risks, so the policy effect is the same; the difference is
   that it carries an `authority.desktop.wait` campaign case.
8. **Secure refusal extended.** An element the snapshot marks secure refuses
   `desktop.clear` and `desktop.press_key` as well as `desktop.set_value` and
   `desktop.type_text`, so a secure field cannot be emptied or filled one
   approved key at a time. `desktop.select` is deliberately not refused: it
   enters no text into the field.
9. **"Secure or protected" reads "secure".** The pinned library (commit
   `2b0a7334`) has no element-level "protected" notion; its element state
   vocabulary has `secure` only, and "protected" exists there only as a
   process notion used by `close_app`. Asura models `secure` alone.
10. **Release publication and the BLOCKED catalog.** Release publication is
    not blocked on `licenses/native-desktop-automation-components.json`. The
    catalog stays `BLOCKED` pending review as a record, exactly like
    `licenses/native-terminal-components.json`, and nothing enforces it: the
    release workflow and the rehearsal do not run
    `scripts/build-agent-desktop.sh --verify-release-clearance`.
11. **Retryable code set.** `AgentDesktopFailure.Retryable` is true only for
    `not_delivered` failures coded `StaleRef`, `ElementNotFound`,
    `SnapshotNotFound`, `AmbiguousTarget`, `Timeout`, `AppNotFound`,
    `WindowNotFound`, `PermissionDenied`, `PolicyDenied` and
    `ActionNotSupported`. `AgentDesktopStableCodes.For` folds the element and
    snapshot cases into `desktop_stale_ref`, whose remedy is the same fresh
    snapshot, so the stable retryable set is exactly the eight codes listed in
    "Authorization and dispatch". `desktop_secure_field`,
    `desktop_invalid_arguments`, `desktop_action_failed`,
    `desktop_self_target`, `desktop_unavailable`, `desktop_cancelled`,
    `desktop_unsaved_changes` and the two reconciliation codes are not
    retryable.
12. **Fixed action budget.** Element actions always use 5,000 ms. The
    original text allowed a smaller request `timeout_ms`; no tool offers one,
    and a shorter budget would only turn more actions into a timeout of
    uncertain delivery.
13. **Struct-size handshake.** The load-time handshake covers the three
    structures Asura passes by pointer (`AdAction`, `AdExactWindowInfo`,
    `AdWaitArgs`). `AdAppInfo` and `AdDeliverySemantics`, which the library
    returns, rely on the ABI major; their sizes are pinned against the
    header by `AgentDesktopLibraryNativeTests.The_struct_mirrors_have_the_sizes_the_header_declares`.
14. **`press_key` focus fallback.** The pinned library's minimum policy for a
    key press is focus-fallback (`Action::base_interaction_policy`), so
    `desktop.press_key` may bring the target window forward even though Asura
    requests headless delivery. The original text implied every element
    action was strictly headless.
15. **Self-target cases under `lifecycle`.** The two self-target campaign
    cases are registered as `lifecycle.desktop-self-target-own-window` and
    `lifecycle.desktop-self-target-cef-helper`, not under `authority`, because
    the campaign validator requires the `authority` cases to be exactly one
    per non-Observation catalog tool. Both run the real broker and host with a
    recording engine; the CEF-helper case marks a stand-in process ID as
    Asura's rather than starting a real helper.
16. **Digest and card contents.** The digest binds the application display
    text, window title, role, element name and secure flag besides the
    identities the original text listed, so an approval cannot be carried to
    a proposal that shows another application or window for the same ref. The
    observation time is not bound; the card shows it as an age.
17. **Session names.** Adapters are created with a random session name, not
    the run identifier. A released and reused run would otherwise share the
    on-disk refs that its release was meant to expire, and the old worker's
    cleanup would delete the new adapter's refs.
18. **`launch_app` and the fresh-snapshot rule.** After an unknown outcome or
    an adapter fault, `desktop.launch_app` is exempt from the fresh-snapshot
    requirement, which otherwise survives Stop and Clear. The original text
    required a snapshot before any further desktop action.
19. **Self-target scope.** The guard also treats a process with Asura's
    bundle identifier and, on macOS only, any direct child of the desktop
    process as Asura. On Linux the parent rule cannot hold, because the
    library launches applications there as children of the host process.
20. **Unsaved-changes refusal.** It is decided by the runtime before
    authority from the run's snapshots, with the dedicated code
    `desktop_unsaved_changes`. Since amendment 22 an application name the run
    has not observed is refused as stale whether or not a sheet is showing.
21. **Already-satisfied element actions.** agent-desktop answers a
    successful `not_delivered` when the element is already in the requested
    state (`OperationReport::satisfied_without_delivery`, read from source).
    SessionHost accepts that answer as a success
    (`InMemorySessionHostClient.DesktopEngineSuccess`) and audits it
    `desktop_already_satisfied`; the runtime reports it as
    `desktop_action_completed` with `already_in_state: true` and
    `verified: false`, and keeps the window's refs. The earlier text made it
    `desktop_action_outcome_unknown`, which forced a fresh snapshot and
    skipped the batch after an action that sent nothing. Tested:
    `AgentDesktopSessionHostTests.ElementActionTheLibraryFoundAlreadySatisfiedSucceedsWithoutDeliveredInput`,
    `DesktopAgentToolResultJsonTests.AnElementActionTheLibraryFoundAlreadySatisfiedSucceedsWithoutAskingForASnapshot`,
    `DesktopAgentObservationLedgerTests.AnElementActionThatDeliveredNothingLeavesItsWindowObserved`
    and
    `GovernedAgentRuntimeDesktopTests.AnElementActionThatWasAlreadySatisfiedNeitherSkipsTheBatchNorExpiresItsWindow`.
22. **`desktop.close_app` binds an observed process.** This decides the
    earlier open question 1. The request carries an
    `AgentDesktopApplicationTarget` built from the run's observations; its
    digest binds the application text, identity, process ID and generation
    token, and its card shows `pid`, `bundle_id` or `executable`, and
    `observation_age`. An unobserved name is `desktop_stale_ref` before
    authority, and the engine refuses at dispatch when the process ID,
    identity or token no longer match. Tested:
    `AgentDesktopActionComposerTests.AClosedApplicationIsBoundAndShownByItsObservedProcessAndIdentity`,
    `DesktopAgentObservationLedgerTests.AnApplicationTheRunNeverObservedCannotBeClosedEvenWithoutASheet`,
    `AnApplicationListingIsAnObservationACloseBindsWithoutAGeneration` and
    `ACloseBindsTheGenerationAWindowOfTheSameProcessCarried`,
    `GovernedAgentRuntimeDesktopTests.ClosingBindsTheListedProcessAndShowsItOnTheCard`
    and `ATargetTheRunNeverObservedIsRefusedBeforeAuthority`,
    `AgentDesktopApplicationResolverTests.An_observed_process_resolves_while_its_live_entry_keeps_the_observed_identity`,
    and, against the release library without the grant,
    `AgentDesktopNativeCallTests.The_refusals_come_before_the_library_is_asked_to_act`.
    A launch still binds only the name it was given.
23. **No pre-authority guard under `Off`.** This decides the earlier open
    question 2 (threat model D13). The runtime calls
    `CheckAgentDesktopTargetAsync` only when the run's effective policy leaves
    the tool's capability on, so a model cannot drive the library's
    application listing through a capability that is `Off`; the broker denies
    the proposal by policy. The host repeats the guard after any permit
    consumption. Tested:
    `GovernedAgentRuntimeDesktopTests.ACapabilityThePolicyTurnsOffNeverRunsTheTargetCheckAndIsDeniedByPolicy`.
24. **A launch that started Asura.** A `SelfTarget` failure with a delivery
    other than `not_delivered` maps to its own stable code,
    `desktop_self_target_launched`, not to `desktop_action_outcome_unknown`,
    so the audit trail names it as a self-target. The engine classifies the
    launched process with the bundle identifier a fresh listing reports for
    it, which recognizes an Asura copy outside the install root
    (`AgentDesktopRunSession.RefuseLaunchedProcess`). The runtime receives
    only the stable code, so the launch case needed a code distinct from the
    host guard's refusal: `desktop_self_target_launched` is a reconciliation
    code, which skips the batch remainder and requires a fresh snapshot,
    while a plain `desktop_self_target` proves nothing was delivered. Tested:
    `AgentDesktopLaunchClassificationTests`,
    `AgentDesktopResultTests.ALaunchThatStartedAsuraIsReportedAsALaunchedSelfTargetAndReconciles`,
    `AgentDesktopSessionHostTests.ALaunchThatStartedAsuraIsAFailedSelfTargetThatIsNeverRetried`,
    `GovernedAgentRuntimeDesktopTests.ALaunchThatReachedAsuraSkipsTheBatchRemainderAndRequiresAFreshSnapshot`
    and
    `GovernedAgentRuntimeDesktopTests.AHostSelfTargetRefusalOfALaunchProvesNothingWasDelivered`.
25. **Fresh snapshot after an unproven mutation failure.** Any failure of a
    mutation the host received requires a fresh snapshot unless its code
    proves nothing was delivered, as described under "Authorization and
    dispatch". The earlier text tied the requirement to the two
    reconciliation codes alone, so a click whose completion could not be
    audited left every ref of its window usable. Tested:
    `GovernedAgentRuntimeDesktopTests.AMutationWhoseCompletionAuditFailedStillRequiresAFreshSnapshot`.
26. **A fault while creating an adapter.** When
    `ad_adapter_create_with_session` returns no adapter, the engine reads
    `ad_last_error_code` on the same worker thread. `AD_RESULT_ERR_INTERNAL`
    faults the run as any other internal fault does, and on the probe's own
    adapter latches the family as unavailable; any other creation failure
    leaves the run unavailable for that request only. The required export
    manifest gained `ad_last_error_code` and has 26 entries. Tested:
    `AgentDesktopAdapterCreationNativeTests`.
27. **Delivered without verification is a success.** Amendment 5 first
    treated delivered-but-unverified input as an unknown outcome. The live
    run on 2026-11-11 showed the library reports every plain button press
    that way, because nothing about a button shows that it was pressed, so
    `desktop.click` could never succeed and every click forced a fresh
    snapshot. Such an action now succeeds, audited
    `desktop_delivered_unverified`, with `verified: false` for the model, and
    expires its window's refs as any delivered input does. Only `uncertain`
    or `unknown` delivery remains an unknown outcome. Tested:
    `AgentDesktopSessionHostTests.ElementActionDeliveredWithoutAnObservedEffectSucceedsAsUnverified`,
    `DesktopAgentToolResultJsonTests.AnElementActionDeliveredWithoutAnObservedEffectSucceedsUnverified`
    and
    `GovernedAgentRuntimeDesktopTests.AnElementActionDeliveredWithoutVerificationSucceedsAndExpiresItsWindow`.
28. **A window accessibility does not expose yet.** For a window the window
    server lists before accessibility exposes it, as a just-launched
    application's window is for a moment, the library answers
    `ACTION_NOT_SUPPORTED` with the detail kind
    `window_without_accessibility_element`. Asura reads that as
    `desktop_window_not_found`, which may be retried, rather than as
    `desktop_not_supported`, which tells the model to give up. Tested:
    `AgentDesktopEnvelopeTests.A_window_accessibility_does_not_expose_yet_is_a_window_not_found`.

No open question remains for the record's author.

## Evidence required before acceptance

The evidence is collected by
`scripts/acceptance/desktop-automation/collect-evidence.sh`, which writes a
machine-readable receipt whose items are named below, and is tracked in
[`docs/acceptance/adr-0061-desktop-automation.md`](../acceptance/adr-0061-desktop-automation.md),
which holds the run records. The last run, on 2026-11-11 at 12:24 UTC, ran
live on macOS 27.2.1 arm64 under the Accessibility grant of the dedicated
evidence runner, through the evidence host: 72 tests, 70 passed, 0 failed,
and 2 skipped, the not-granted cases that cannot be observed while the grant
is held.

- A macOS fixture application with text fields, a secure field, a checkbox, a
  menu, a sheet and a modal alert, driven end to end through the governed
  path with policy Off, Ask, Auto and confirmed YOLO, including every closed
  failure code.
  Status: collected live. Receipt items `A1-policy-matrix`, `A4-stub-flavor`
  and `B6-fixture-end-to-end` (17 of 17) passed in the 2026-11-11 run. The
  fixture's own event log shows each of the nine interaction tools delivered
  exactly once; text entry on the secure field refused before any card;
  `desktop.close_app` refused with `desktop_unsaved_changes` while the sheet
  is up; the live policy matrix for Off, Ask (approved and declined), Auto and
  confirmed YOLO; and a recipe for every closed failure code that delivered
  nothing, `desktop_ambiguous_target` among them through two running
  instances of the fixture. The fixture exists
  (`scripts/acceptance/desktop-automation/`, with its control inventory in
  `README.md` and a self-test in `test.sh`). Unit coverage of the same rules
  with a recording engine: `AgentDesktopPolicyGateTests` and
  `AgentDesktopSessionHostTests.ClosedCodeTableNamesEveryEngineFailure`.
- Proof that `desktop.list_apps` omits Asura and that a hand-built proposal
  naming Asura's PID, a CEF helper PID, and a process under the bundle path
  fails before an approval card appears.
  Status: collected, live listing included.
  Receipt item `A2-self-operation` passed in the recorded run: Asura's PID, a
  compiled helper started as a direct child (the CEF helper's stand-in), the
  same helper started outside the parent relation from inside the install
  root, and a fixture copy opened from the install root are refused and
  audited `denied`. Unit coverage:
  `AgentDesktopEngineNativeTests.A_target_that_is_this_process_is_refused_before_any_approval`
  (real engine and guard); `AgentDesktopSelfTargetGuardTests` with a fake
  inspector on the shipped layouts, including
  `In_the_shipped_bundle_a_CEF_helper_outside_the_application_directory_is_Asura`,
  `A_helper_Asura_starts_from_outside_its_bundle_is_Asura_on_macOS_only` and
  `In_the_Linux_layout_the_install_directory_is_Asura_and_launched_children_are_not`;
  `AgentDesktopProcessInspectorTests`; and the `lifecycle.desktop-self-target-*`
  cases in `SecurityCampaignDesktopAuthorityTests` (recording engine). The
  list filter is shown on hand-written listings by
  `AgentDesktopListProjectionTests`: Asura's PID, a CEF helper in the
  bundle, an Asura copy known only by its bundle identifier, an uninspectable
  process and a direct child of Asura are dropped from both listings.
  `B6-list-apps-omits-asura`, the live listing with an Asura process running
  beside the test host, passed in the 2026-11-11 run.
  `AgentDesktopObservationNativeTests.The_application_list_is_bounded_and_omits_this_process`
  also needs the grant and proves little about the omission: the library
  never lists the test host. A real CEF helper of a running Asura bundle has
  not been refused live.
- A stale-ref run: snapshot, mutate the fixture out of band, act, observe
  `desktop_stale_ref` with no delivered input.
  Status: collected live (receipt item `B6-stale-ref`, 2026-11-11). The refusals Asura makes itself
  before the library is asked are collected in
  `AgentDesktopReferenceBindingNativeTests` and
  `GovernedAgentRuntimeDesktopTests.ARefOutsideItsSnapshotAndARefSpentByAMutationAreRefusedBeforeAuthority`.
- An outcome-unknown run: a cancelled or timed-out action after dispatch
  produces `desktop_action_outcome_unknown`, the batch remainder is skipped,
  and no second dispatch occurs.
  Status: collected live, with a real action against the fixture's slow
  control (receipt item `B6-outcome-unknown`, 2026-11-11). Also collected
  with a recording engine and a fake host:
  `AgentDesktopSessionHostTests.CancellationAfterDispatchWithoutProofIsOutcomeUnknown`,
  `AgentDesktopRequestContainmentNativeTests.An_exception_after_the_mutating_call_began_is_an_unknown_outcome`,
  and `GovernedAgentRuntimeDesktopTests.AnUnknownOutcomeSkipsTheBatchRemainderAndRequiresAFreshSnapshot`.
- A panic-injection build (`--features panic-injection`) showing
  `AD_RESULT_ERR_INTERNAL` cancels the run without taking down the process.
  Status: collected. Receipt item `A3-panic-boundary` passed in the recorded
  run. `AgentDesktopPanicIsolationEvidenceTests` loads the
  staged `panic` flavor and shows the panic trapped, the run refused
  afterwards, other runs and the shipped image unaffected, and the test
  process alive; the run cancellation that follows is
  `GovernedAgentRuntimeDesktopTests.AnAdapterFaultCommitsTheResultSkipsTheBatchAndCancelsTheRun`
  and `AgentDesktopSessionHostTests.AdapterFaultReleasesTheRunsDesktopSession`.
- A signed and notarized bundle whose Accessibility grant survives an in-app
  update, since TCC binds the grant to the bundle's designated requirement.
  Status: outstanding because it needs the owner's signing identity
  (receipt item `C-signed-update-survival`, manual; the procedure is on the
  acceptance page). No signed or notarized bundle containing the library has
  been produced. The build receipt records the `signatureRemovedSha256` of
  the library and the helper that `AgentDesktopPackageProvenance` compares
  after signing.
- `./scripts/check.sh --full` green with the new architecture tests and
  security campaign cases.
  Status: collected on 2026-11-11 by a manual run on macOS arm64 (receipt
  item `C-repository-gate`, which the collector does not run). The gate
  exited 0: the build, formatting, dependency audit and UI copy checks
  passed, and all 30 test projects passed with no failures. The desktop
  projects skip only what needs the Accessibility grant or the fixture
  (7 of 448 in `Asura.DesktopAutomation.Tests`, 11 of 62 in
  `Asura.DesktopAutomationAcceptance.Tests`). The run used the release and
  evidence flavors staged with the 26-entry export manifest, which
  `scripts/build-agent-desktop.sh --verify-staged` accepts.
