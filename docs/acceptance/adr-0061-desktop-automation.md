# ADR 0061 acceptance evidence: desktop automation

This page tracks the evidence that
[ADR 0061](../adr/0061-governed-desktop-automation-through-agent-desktop-ffi.md)
lists under "Evidence required before acceptance". For each item it gives the
status, the command that produces the evidence, and what is still needed.

Most of the evidence comes from `tests/Asura.DesktopAutomationAcceptance.Tests`,
run by
[`collect-evidence.sh`](../../scripts/acceptance/desktop-automation/collect-evidence.sh).
The script writes a machine-readable receipt. The fixture application the live
tests drive is described in
[`scripts/acceptance/desktop-automation/README.md`](../../scripts/acceptance/desktop-automation/README.md).

## Status

Last collected on 2026-11-11 on macOS 27.2.1, Apple Silicon, live, under the
evidence runner's Accessibility grant and through the evidence host. Every
item the collector covers passed. The only skips are the two cases that can
be observed only while the grant is absent; the 2026-11-10 run, without the
grant, collected those.

| # | ADR evidence | Receipt items | Status | What is outstanding |
|---|---|---|---|---|
| 1 | Fixture driven end to end with Off, Ask, Auto and confirmed YOLO, including every closed failure code | `A1-policy-matrix`, `A4-stub-flavor`, `B6-fixture-end-to-end` | Collected live (A1 27 passed, A4 2, B6 17 of 17). | Nothing. |
| 2 | `desktop.list_apps` omits Asura; a hand-built proposal naming Asura's PID, a CEF helper PID and a process under the bundle path fails before an approval card | `A2-self-operation`, `B6-list-apps-omits-asura` | Collected live (18 and 1 passed). | A real CEF helper of a running, signed Asura bundle has not been refused live; a compiled stand-in was. |
| 3 | Stale-ref run | `B6-stale-ref` | Collected live (passed). | Nothing. |
| 4 | Outcome-unknown run | `B6-outcome-unknown` | Collected live (passed). | Nothing. |
| 5 | Panic-injection build cancels the run without taking down the process | `A3-panic-boundary` | Collected (passed). | Nothing. |
| 6 | Signed and notarized bundle whose Accessibility grant survives an in-app update | `C-signed-update-survival` (`manual`) | Needs the owner's signing identity and two releases. | Follow the [manual procedure](#manual-procedure-the-grant-survives-an-in-app-update). |
| 7 | `./scripts/check.sh --full` green with the new architecture tests and security campaign cases | `C-repository-gate` (`not-run`) | Not run by the collector. Passed on 2026-11-11 in a manual run: all 30 test projects green, no failures. | Run `./scripts/check.sh --full` again after any change. |
| — | Shipped library identity: the receipted release build, loaded from the application directory (decision 6) | `A0-library-identity` | Collected (passed). | Nothing. |
| — | Audit details hold no tree content, element names, window titles, typed text or application names | `A5-audit-secrecy` | Collected (passed). | Nothing. |

`licenses/native-desktop-automation-components.json` is still marked BLOCKED,
as a record pending review, the same way
`licenses/native-terminal-components.json` is. Nothing enforces it. Neither
the release workflow nor the rehearsal runs
`scripts/build-agent-desktop.sh --verify-release-clearance`, and release
publication does not wait on it. It is a record, not a release gate.

## Running the collector

```bash
./scripts/acceptance/desktop-automation/collect-evidence.sh
```

The script does the following, in order:

1. Checks the staged release build with
   `scripts/build-agent-desktop.sh --verify-staged --rid osx-arm64`. The
   receipts staged now record the 25-entry export manifest that preceded
   `ad_last_error_code`; `native/agent-desktop/required-exports.txt` has 26
   entries, so this check fails until the release, stub and panic flavors
   are staged again with `./scripts/build-agent-desktop.sh`.
2. Builds the fixture with `build.sh`. This needs `swiftc` from the Xcode
   command line tools.
3. Runs the test project with `ASURA_DESKTOP_EVIDENCE=1`. Only with this
   variable set may tests launch the fixture and put its windows on screen.
4. Writes `receipt.json`, `observations.jsonl`, the TRX results and a log to
   `.test-results/desktop-automation-evidence/<UTC time>/`. That directory is
   gitignored.
5. Checks the receipt with `validate_evidence_receipt.py` and prints a summary.

Use `--output <dir>` to write somewhere else. `--artifacts-path <dir>` is
passed on to `dotnet test`.

Exit status:

- `0`: no evidence test failed. Skipped tests are allowed.
- `1`: a test failed, or the receipt is invalid.
- `2`: this host cannot collect the evidence. It is not macOS on Apple
  Silicon, or the SDK is missing.

The validator rejects a receipt that claims more than it holds:

- An item counts as passed only if at least one of its tests ran and none was
  skipped or failed.
- A skipped item must carry the reason xunit reported.
- The item counts must add up to the test totals.
- The three library receipts must name the right flavor, and only the panic
  flavor may carry the panic-injection export.

The receipt binds the evidence to its inputs:

- the commit, with SHA-256 digests of the uncommitted diff and of the
  untracked files;
- every staged agent-desktop flavor's receipt hash, library hash and helper
  hash;
- the fixture binary's hash;
- the host's grant state.

`observations.jsonl` records facts that are worth keeping but not asserting:

- the stable code each policy cell produced;
- the code each README recipe produced, including those the README marks
  "expected";
- what a snapshot of the two-window fixture returned.

Without `ASURA_DESKTOP_EVIDENCE=1`, for example under `./scripts/check.sh
--full`, the same project runs everything that needs neither the fixture nor
the grant. A plain run gives 51 passed and 11 skipped.

## What "the real stack" means here

`src/Asura.Desktop/DesktopComposition.cs` also starts Avalonia and CEF, so it
cannot be built headlessly in a test. `GovernedDesktopStack` composes the same
objects by hand:

- `GovernedAgentRuntime`, with every host role filled by the real
  `InMemorySessionHostClient`;
- `AgentCapabilityBroker` over a `SqliteAuditStore` in a database file of its
  own;
- `AgentDesktopActionComposer`;
- `AgentDesktopAutomationEngine`. The release engine is created through the
  public `Create(stateRoot)`, so it loads `libagent_desktop_ffi.dylib` from the
  application directory, exactly as Asura does. `A0` proves this is the
  receipted build.

The scripted model is a provider that plays fixed rounds. In live runs it
reads the latest snapshot result it was given to choose its refs, the way a
model would.

Three things are not the shipped arrangement. Each is stated here so the
evidence is not overstated:

- **`RecordingDesktopEngine`** is a pass-through around the real engine. It
  forwards every call and every answer unchanged and only counts them. This
  is how the tests show "exactly one dispatch attempt" and "no native action
  entrypoint".
- **`GrantReportingDesktopHost`**. On a host without the grant the runtime
  refuses every desktop call before authority. To show the runtime, broker,
  host and library path anyway, part A wraps the real host so that it reports
  "granted" to the runtime. Every other member is the real host. The library's
  own permission check then refuses the dispatch with
  `desktop_permission_denied`. On a host with the grant, and in every part B
  test, the wrapper is not used.
  `A_grant_missing_at_call_time_refuses_the_call_before_authority` shows the
  real "not granted" answer at call time. `Without_the_grant_the_runtime_offers_no_desktop_tool`
  shows the unwrapped behaviour: no desktop tool is offered, and a call to one
  ends the turn as `invalid_provider_stream`.
- **The stub and panic flavors** cannot sit in the application directory
  beside the release library. The tests load them by path through the
  engine's internal factory, `AgentDesktopAutomationEngine.Create(stateRoot,
  libraryPath)`, and call the internal panic hook `InjectPanicForTestAsync`
  directly. `src/Asura.DesktopAutomation/Asura.DesktopAutomation.csproj`
  grants its internals to `Asura.DesktopAutomation.Tests` and to this
  project, `Asura.DesktopAutomationAcceptance.Tests`, and to nothing else.
  If a member is renamed, the evidence project stops compiling; it does not
  skip.
  The panic itself is real. The library's `ad_test_panic_boundary` export
  panics behind its `catch_unwind` boundary, on the worker of the run the
  model is in. The fault then reaches the model through an ordinary
  `desktop.list_apps` call, unaltered.

For the self-operation evidence, the test host stands in for Asura. Its
application directory is the install root the guard protects. Three
processes stand in for Asura's helpers:

- a small helper compiled into that directory with `xcrun clang`, started as
  a direct child (the CEF helper's stand-in);
- the same helper started so that launchd, not the test host, is its parent
  (a process under the bundle path that is not a child);
- the fixture bundle copied into the directory and opened through
  LaunchServices. It is a real GUI application, which the native application
  listing reports without the grant.

A copied Apple binary would not work for the first two: macOS launch
constraints kill a copy of a system executable. The same helper outside the
install root is the control case, and it is not refused.

## Collecting the live evidence

Part B (items 1 to 4 above) needs the Accessibility grant, and it needs a
process whose main thread services AppKit's run loop, as Asura's does. Two
pieces provide both:

- **The evidence runner** (`EvidenceRunner.swift`, built by
  `build-runner.sh`) is a one-purpose app. Launched through LaunchServices it
  is its own responsible application, so the grant names this runner and the
  processes it starts, never a terminal or an IDE. Its identifier,
  `dev.asura-fixture.evidence-runner`, is outside `sh.asura` on purpose.
- **The evidence host** (`EvidenceHost.cs`) is the evidence assembly's own
  entry point. It runs the tests in-process on worker threads while its main
  thread services the run loop. agent-desktop learns which applications run
  from `NSWorkspace.runningApplications`, which AppKit refreshes only there;
  under `dotnet test` every application launched after the library's first
  inventory read is invisible, and its windows are reported missing.
  `collect-evidence.sh` runs the evidence through this host.

To collect:

1. Quit other agent-desktop clients (the `agent-desktop` CLI or MCP server).
   They take the same interaction lease.
2. Run `./scripts/acceptance/desktop-automation/collect-evidence-granted.sh`.
   It builds the runner once and runs the collector inside it. The first time,
   macOS asks whether "Asura Desktop Evidence Runner" may control the
   computer; allow it in System Settings › Privacy & Security ›
   Accessibility, which macOS 27 calls "Device Control and Data Access", and
   run the script again. A rebuilt runner has a new ad-hoc signature and needs
   the grant again, so `build-runner.sh` keeps an existing one unless given
   `--force`.
3. Keep the screen unlocked and do not type or click while it runs. A locked
   session exposes no application window through accessibility, so the live
   tests skip with that reason. The live tests act only on the fixture's
   window, but a headless action lands even if you are typing in the same
   application (ADR 0061, Consequences).
4. In the printed summary, `A0` must report `Accessibility granted`, and
   `B6-list-apps-omits-asura`, `B6-fixture-end-to-end`, `B6-stale-ref` and
   `B6-outcome-unknown` must read `passed`.
5. Read `observations.jsonl` for the code each recipe really produced.
6. Copy the summary into this page under "Run record".
7. Remove the runner from the Accessibility list if it should not keep the
   grant.

With the grant, `A1` no longer ends at the library's permission check. It
asserts the authorization and dispatch counts, and records the library's real
answer as an observation, and its not-granted cases skip.

## Manual procedure: the grant survives an in-app update

macOS binds an Accessibility grant to the bundle's designated requirement.
This check shows that an update installed by Asura's own updater keeps that
requirement, so the grant survives. It needs the owner's Developer ID
Application identity and notarization credentials (see
[macOS release rehearsal](../macos-release-rehearsal.md), "Prerequisites").
It also needs two consecutive releases that both contain the desktop family:
`src/Asura.Updates` reads the GitHub releases of the configured repository
(non-prerelease, channel `osx-arm64-stable`), so the newer release must really
be published.

1. **Build and sign release N.** Run `./scripts/rehearse-macos-release.sh
   --tag vN` from a clean tagged tree. Alternatively, run
   `./scripts/package-macos-github-release.sh --version N --build-version <b>
   --output-dir <new dir> --sign-identity "<Developer ID Application: …>"
   --notary-profile <profile>`, which signs through
   `scripts/sign-notarize-macos.sh`. Then check the nested code:

   ```bash
   app=<output>/Asura.app
   for nested in libagent_desktop_ffi.dylib agent-desktop-macos-helper; do
     codesign --verify --strict --verbose=2 "$app/Contents/MacOS/$nested"
   done
   codesign -d -r- "$app" 2>&1 | tee dr-before.txt
   spctl -a -vv "$app"
   ```

2. **Install N.** Extract the portable ZIP of N into a folder you can write
   to, such as `~/Applications`, and start it.
3. **Grant access.** Open Settings › AI. The desktop automation row shows
   "Access not granted". Click "Open System Settings…", turn on Asura in the
   list the row names ("Accessibility" through macOS 26, "Device Control and
   Data Access" from macOS 27), and come back. The row shows "Ready".
4. **Use the grant.** In a workspace chat, set "Desktop app data" to Ask. Ask
   the agent to list the running applications and approve the card. The
   result lists applications and does not include Asura.
5. **Publish N+1.** Follow the release rehearsal and publication steps for
   version N+1, with the same signing identity.
6. **Update in place.** In the running N, open About, check for updates,
   download N+1 and apply it. Asura restarts as N+1.
7. **Check the identity.** Confirm the About page shows N+1, then:

   ```bash
   codesign -d -r- ~/Applications/Asura.app 2>&1 | tee dr-after.txt
   diff dr-before.txt dr-after.txt   # must print nothing
   spctl -a -vv ~/Applications/Asura.app
   ```

8. **Check the grant.** Without touching System Settings, open Settings › AI.
   The row must show "Ready". Repeat step 4; the listing must succeed.
9. **Record the result.** Add to this page: both versions, both designated
   requirements, the row state before and after the update, and the result
   code of each listing.

The check fails if the row reads "Access not granted" after the
update, or if the listing returns `desktop_permission_denied`. In that case,
record how the two designated requirements differ. One question this
procedure does not settle is whether `agent-desktop-macos-helper`, which
Asura's library launches, ever needs a grant of its own.

## Run record

### 2026-11-11 12:24 UTC, live, complete

macOS 27.2.1 arm64, screen unlocked, under the evidence runner's grant,
through the evidence host. Uncommitted tree on top of `7a7b455f`.

```text
tests: 72 total, 70 passed, 0 failed, 2 skipped
  passed   A0-library-identity          pass/fail/skip 1/0/0
  skipped  A1-policy-matrix             pass/fail/skip 27/0/2   (the 2 are the not-granted cases)
  passed   A2-self-operation            pass/fail/skip 18/0/0
  passed   A3-panic-boundary            pass/fail/skip 1/0/0
  passed   A4-stub-flavor               pass/fail/skip 2/0/0
  passed   A5-audit-secrecy             pass/fail/skip 1/0/0
  passed   B6-list-apps-omits-asura     pass/fail/skip 1/0/0
  passed   B6-fixture-end-to-end        pass/fail/skip 17/0/0
  passed   B6-stale-ref                 pass/fail/skip 1/0/0
  passed   B6-outcome-unknown           pass/fail/skip 1/0/0
  manual   C-signed-update-survival
  not-run  C-repository-gate
```

All 24 fixture event logs were kept. Together they hold exactly the input the
tests sent: four `Record Action` presses (one for each authorized policy case
and one from the delivery test), one each for the checkbox, disclosure,
pop-up and slow button, and one Return key in the second text field. No alert
button was pressed in any of them, so the stray OK press seen once in the
earlier run did not recur.

Two more things changed before this run:

- The fixture logs the state of its alert just before AppKit shows the alert
  window, so a test that waited for that line could snapshot while only one
  window existed. The fixture now also logs `alert-on-screen` from inside the
  modal session, and the tests wait for that.
- Once the alert is the focused window, the library snapshots it instead of
  answering `AMBIGUOUS_TARGET`; that answer exists only for the instant
  before. The ambiguous-target recipe now uses two running instances of the
  fixture, which Asura refuses by name before the library is asked anything.

### 2026-11-11, live, under the evidence runner's grant, before the fixes

macOS 27.2.1 arm64, uncommitted tree on top of `7a7b455f`. This was the first
run with a real Accessibility grant. It is a record of what the live runs
found, not of a passing collection: the screen locked part way through. The
complete run above repeated it with the screen unlocked.

- Run through `dotnet test`, 16 of the 17 fixture tests failed. The cause was
  the test host, not the product: without a serviced main run loop the
  library never saw a fixture launched after its first inventory read. The
  evidence host fixed that, and a full run through it then passed 66 of 72
  tests, the 2 skips being the not-granted cases.
- That run found four defects, each fixed since:
  - Every plain button press came back `delivered_unverified`, which Asura
    then treated as `desktop_action_outcome_unknown`, so a click could never
    succeed (ADR 0061 amendment 27).
  - A just-launched window not yet exposed through accessibility came back
    as `desktop_not_supported`, which tells the model to give up (amendment
    28).
  - The ambiguous-target recipe was wrong: a twin's ref is unambiguous, and
    the click reached exactly that twin. The recipe now snapshots the window
    while the fixture's modal alert is up, which the library answers with
    `AMBIGUOUS_TARGET`.
  - The secure-field test matched the field's label as well as the field.
- One observation is not explained yet. In the full run, the two-window test
  saw the alert's OK button pressed about four seconds after the alert
  appeared. Run alone, twice, and taken one surface at a time against the
  library directly, nothing pressed it. Its fixture event logs are kept when
  `ASURA_DESKTOP_EVIDENCE_KEEP_FIXTURE_LOGS` names a directory, so the next
  full live run can show which test sent that input.
- With the screen locked, the same collection reports 49 passed, 0 failed and
  13 skipped: every fixture test skips with the lock as its reason.

### 2026-11-10, no grant

2026-11-10, macOS 27.2.1 arm64, no Accessibility grant, uncommitted tree on
top of `7a7b455f`.

```text
ADR 0061 desktop automation evidence, 2026-11-10T13:38:55Z
source 7a7b455f1861 (dirty, 118 paths), host macOS 27.2.1 arm64, Accessibility not_granted
agent-desktop 2b0a7334fc11 ABI 7, staged release verified: True, fixture built: True
tests: 62 total, 52 passed, 0 failed, 10 skipped
  passed   A0-library-identity          pass/fail/skip 1/0/0     Packaging and provenance; decision 6
  passed   A1-policy-matrix             pass/fail/skip 29/0/0    Evidence 1: policy Off, Ask, Auto and confirmed YOLO
  passed   A2-self-operation            pass/fail/skip 18/0/0    Evidence 2: proposals naming Asura fail before an approval card
  passed   A3-panic-boundary            pass/fail/skip 1/0/0     Evidence 5: panic-injection build
  passed   A4-stub-flavor               pass/fail/skip 2/0/0     Closed failure codes: desktop_unavailable
  passed   A5-audit-secrecy             pass/fail/skip 1/0/0     Authorization and dispatch: audit details
  skipped  B6-list-apps-omits-asura     pass/fail/skip 0/0/1     Evidence 2: desktop.list_apps omits Asura
  skipped  B6-fixture-end-to-end        pass/fail/skip 0/0/7     Evidence 1: the fixture driven end to end, every closed failure code
  skipped  B6-stale-ref                 pass/fail/skip 0/0/1     Evidence 3: stale-ref run
  skipped  B6-outcome-unknown           pass/fail/skip 0/0/1     Evidence 4: outcome-unknown run
  manual   C-signed-update-survival     pass/fail/skip 0/0/0     Evidence 6: Accessibility grant survives an in-app update
  not-run  C-repository-gate            pass/fail/skip 0/0/0     Evidence 7: ./scripts/check.sh --full green
```

Every skip carries the reason "Requires the macOS Accessibility grant for the
test host … agent-desktop reported NotGranted." A skipped xunit theory counts
as one test, so the seven skipped B6 tests cover more cases than seven.

Observed on this run, without the grant:

- Without the wrapper, the runtime offers no desktop tool. With it:
  - Off answers `policy_denied`.
  - A declined card answers `approval_denied`, including the card that Auto
    raises for DesktopControl.
  - Every authorized dispatch ends in `desktop_permission_denied` from the
    library, recorded as `failed` in the audit store.
- Each `Off` cell recorded one guard call, which reads the native
  application listing: the target guard then ran before the broker refused
  an `Off` capability (threat-model finding D13). That has been fixed since
  this run. The runtime now skips the guard when the run's policy turns the
  tool's capability off
  (`GovernedAgentRuntimeDesktopTests.ACapabilityThePolicyTurnsOffNeverRunsTheTargetCheckAndIsDeniedByPolicy`),
  and `A1` now asserts zero guard calls in every `Off` cell; it passed in the
  2026-11-11 live run.
- The pre-approval guard missed an Asura process in one order of events,
  reproduced in two runs. A copy of the fixture was launched from inside the
  install root right after the original fixture had quit. A request naming
  the copy's display name was refused as `desktop_self_target`, but
  `desktop.close_app` naming its bundle identifier was let through to an
  approval card. When the copy ran alone, both were refused. The cause is now
  documented in `AgentDesktopApplicationResolver`: agent-desktop merges
  AppKit's running applications with `ps`, and listed the copy only as `ps`
  reported it, under its bundle directory's name with no bundle identifier,
  so the bundle identifier matched nothing. When the resolver now decides
  whether an identifier could reach Asura or an uninspectable process, an
  entry without a listed bundle identifier answers to any identifier shaped
  like one, and any entry answers to its executable's file name where the
  library's alias rule allows (`AgentDesktopApplicationResolver.CouldAnswer`); an entry that answers and
  is Asura, or cannot be inspected, refuses the identifier. `AgentDesktopApplicationResolverTests`
  reproduces the recorded listings
  (`A_copy_in_the_install_root_listed_without_its_bundle_identifier_is_refused_under_every_name`,
  `A_stale_entry_of_the_quit_original_beside_the_copy_is_still_refused_as_Asura`,
  `A_copy_listed_twice_once_with_and_once_without_its_bundle_identifier_is_refused`
  and `The_original_still_running_beside_the_copy_does_not_make_its_bundle_identifier_safe`).
  The fix is proven against those recorded listings, not live. The live
  evidence test
  (`SelfOperationEvidenceTests.A_real_application_listed_from_inside_the_install_root_is_Asura`)
  runs the copy first and alone, so its result does not depend on the order
  that exposed the gap.
