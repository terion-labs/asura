# Desktop automation fixture

A small AppKit application that Asura's governed `desktop.*` tools observe and operate during the evidence runs [ADR 0061](../../../docs/adr/0061-governed-desktop-automation-through-agent-desktop-ffi.md) requires. The fixture keeps its own record of every input that reached it, so a run can prove which control received an action and how many times. Exactly-once delivery is the property under test.

The fixture is not Asura and cannot be mistaken for it. Its bundle identifier is `dev.asura-fixture.desktop-automation`, its display name is `Desktop Automation Fixture`, and it is built under `native/artifacts/`, outside any Asura bundle or install root, so the self-target guard does not hide it. It is never packaged and carries only an ad-hoc signature.

The fixture needs no Accessibility grant. The grant under test belongs to Asura.

## Build, run, stop

```bash
./scripts/acceptance/desktop-automation/build.sh
./scripts/acceptance/desktop-automation/run.sh --quit-after 600
./scripts/acceptance/desktop-automation/stop.sh
```

`build.sh` compiles `DesktopAutomationFixture.swift` with `swiftc` (warnings are errors) and writes `native/artifacts/<rid>/agent-desktop-evidence/fixture/Desktop Automation Fixture.app` with an `Info.plist` and an ad-hoc code signature. The directory is gitignored.

`run.sh` launches the bundle through LaunchServices, waits for the pid file, and prints four `key=value` lines: `pid`, `event_log`, `pid_file` and `control_file`. It refuses to start a second fixture on the same pid file. `stop.sh` sends SIGTERM, which the fixture records before exiting, and falls back to SIGKILL after five seconds. Both accept `--pid-file`.

| Argument | Default from `run.sh` | Meaning |
|---|---|---|
| `--event-log <path>` | `<fixture dir>/run/events.jsonl` | Append-only event log. Earlier runs' lines stay; tell runs apart by `pid`. |
| `--pid-file <path>` | `<fixture dir>/run/fixture.pid` | Written once the window is up, removed on exit. |
| `--control-file <path>` | `<fixture dir>/run/control` | Out-of-band commands, one per line. |
| `--quit-after <seconds>` | `600` | Safety timer, 1 to 86400. Fires off the main thread, so it works during a stall or a modal alert. |
| `--slow-action-seconds <seconds>` | `8` | How long `Slow Action` and the `stall` command hold the main thread, 1 to 60. The default exceeds the 5,000 ms auto-wait budget. |

`<fixture dir>` is `native/artifacts/<rid>/agent-desktop-evidence/fixture`. Paths passed to the binary must be absolute. `run.sh` resolves relative ones.

When the fixture starts with no arguments, which is how `desktop.launch_app` starts it, the three files live in `$(getconf DARWIN_USER_TEMP_DIR)dev.asura-fixture.desktop-automation/` under the names `events.jsonl`, `fixture.pid` and `control`. `stop.sh --pid-file` works on that pid file too. LaunchServices learns about the bundle the first time `run.sh` opens it.

## Self-test

```bash
./scripts/acceptance/desktop-automation/test.sh
```

The test lints the scripts with shellcheck when it is installed, builds the bundle, and runs the binary with `--self-exercise`. In that mode the fixture activates its own controls from inside the process, reads its own accessibility tree through the client API, and exits. `check_self_exercise.py` then compares the event log with the contract in this file, including the control inventory below. A process may always read its own tree, so the test needs a logged-in graphical session and nothing else. It observes no other application and performs no accessibility action.

A self-exercise log starts with `"selfExercise": true`. An evidence driver must reject such a log, because no accessibility client delivered anything in it.

## Control inventory

Identifier is `AXIdentifier`. Native role is what an accessibility client is told, and `test.sh` asserts it together with the name and the action names. Snapshot role is agent-desktop's normalized role at the pinned commit (`crates/macos/src/tree/roles.rs`). Asura's snapshot nodes carry role, name, value, states and ref. They do not carry the identifier, so a driver finds a control by role and name. Every name is unique within its surface except the twins, which are identical on purpose.

### Main window

Window title `Desktop Automation Fixture`, identifier `fixture.window`, native role `AXWindow` with subrole `AXStandardWindow`.

| Identifier | Native role | Snapshot role | Name | Effect, and what the log shows |
|---|---|---|---|---|
| `fixture.text-field-1` | `AXTextField` | `textfield` | `First Text` | Plain text. `state` with `value` on every change. Return logs an `action`. Starts focused. |
| `fixture.text-field-2` | `AXTextField` | `textfield` | `Second Text` | Same as the first field. |
| `fixture.secure-field` | `AXTextField`, subrole `AXSecureTextField` | `textfield` with state `secure` | `Secret` | `state` carries `length` only. The text is never logged. |
| `fixture.checkbox` | `AXCheckBox` | `checkbox` | `Enable Option` | Each press logs an `action`, then `state` with `value` true or false. Starts unchecked. |
| `fixture.popup` | `AXPopUpButton` | `combobox` | `Flavor` | Items `Vanilla`, `Chocolate`, `Strawberry`, `Pistachio`. Choosing one logs an `action` and `state` with the item title. Starts on `Vanilla`. |
| `fixture.disclosure` | `AXDisclosureTriangle` | `disclosure` | `Details` | Each press logs an `action` and `state` true (expanded) or false. While expanded, the static text `Details are expanded.` (`fixture.disclosure-content`) is in the tree. |
| `fixture.scroll-view` | `AXScrollArea` | `scrollarea` | `Rows` | `state` with `firstVisibleRow` and `lastVisibleRow` whenever the visible range changes. Rows 1 to 6 are visible at launch. |
| `fixture.table` | `AXTable` | `table` | `Rows` | 40 rows. `state` with `value` `"Row N"` for the selected row, or null. |
| `fixture.row-N` | `AXStaticText` inside `AXRow` and `AXCell` | `statictext` | value `Row N` | Only rows scrolled into view have this text element. |
| `fixture.action-button` | `AXButton` | `button` | `Record Action` | Each press logs one `action`. The plain exactly-once target. |
| `fixture.slow-button` | `AXButton` | `button` | `Slow Action` | Logs the `action`, then holds the main thread for `--slow-action-seconds`, then logs `stall-ended`. |
| `fixture.disabled-button` | `AXButton`, not enabled | `button` with state `disabled` | `Disabled Action` | Must never log an `action`. |
| `fixture.sheet-button` | `AXButton` | `button` | `Open Unsaved Changes Sheet` | Logs an `action` and attaches the sheet. |
| `fixture.alert-button` | `AXButton` | `button` | `Show Modal Alert` | Logs an `action` and runs the modal alert after the press returns. |
| `fixture.stale-target` | `AXButton` | `button` | `Stale Target` | Logs an `action`. The `remove-stale-target` command takes it out of the tree. |
| `fixture.twin-button` (two of them) | `AXButton` | `button` | `Twin Action` | Same role, name and identifier. The `action` carries `instance` `first` or `second`. |
| `fixture.status` | `AXStaticText` | `statictext` | `Status` | Its value mirrors the latest logged event. |

Native action names: the buttons, the checkbox and the disclosure triangle advertise `AXPress`. The pop-up button advertises `AXPress` and `AXShowMenu`. The text fields advertise `AXConfirm` and `AXShowMenu`. The scroll area advertises the four `AXScroll...ByPage` actions. The disclosure triangle has no `AXExpanded` attribute; its state is its `AXValue`, 0 or 1, which is the case agent-desktop's expand and collapse fall back to `AXPress` for.

The pop-up's items (`fixture.popup.vanilla` and so on, role `AXMenuItem`) exist in the tree only while its menu is open. The self-test does not open it, so their shape is not asserted.

### Main menu

| Identifier | Native role | Snapshot role | Name | Effect |
|---|---|---|---|---|
| `fixture.menu.application` | `AXMenuBarItem` | `menuitem` | `Desktop Automation Fixture` | Holds Quit. |
| `fixture.menu.quit` | `AXMenuItem` | `menuitem` | `Quit Desktop Automation Fixture` | Command-Q. Logs `quit-requested`. |
| `fixture.menu.edit` | `AXMenuBarItem` | `menuitem` | `Edit` | Undo, Redo, Cut, Copy, Paste, Select All, so the editing chords work in the fields. |
| `fixture.menu` | `AXMenuBarItem` | `menuitem` | `Fixture` | The menu under test. |
| `fixture.menu.record` | `AXMenuItem` | `menuitem` | `Record Menu Action` | Command-R. Logs an `action`. |
| `fixture.menu.variants` | `AXMenuItem` with a submenu | `menuitem` | `Variants` | Opens the submenu. |
| `fixture.menu.variant-1`, `-2`, `-3` | `AXMenuItem` | `menuitem` | `Variant One`, `Variant Two`, `Variant Three` | Each logs an `action` under its own identifier. |

Every menu item advertises `AXPress`, `AXPick` and `AXCancel`.

### Sheet

`Open Unsaved Changes Sheet` or the `show-sheet` command attaches it. The message asks whether to save the changes made to the document Untitled, with typographic quotes around the name.

| Identifier | Native role | Snapshot role | Name | Effect |
|---|---|---|---|---|
| `fixture.sheet` | `AXSheet` | `sheet` | description `alert`, no title | `state` `fixture.sheet` true while attached. |
| `fixture.sheet.save` | `AXButton` | `button` | `Save` | Logs an `action` and closes the sheet. |
| `fixture.sheet.cancel` | `AXButton` | `button` | `Cancel` | Same. |
| `fixture.sheet.dont-save` | `AXButton` | `button` | `Don't Save` (plain apostrophe) | Same. |

### Modal alert

`Show Modal Alert` or the `show-alert` command runs it.

| Identifier | Native role | Snapshot role | Name | Effect |
|---|---|---|---|---|
| `fixture.alert` | `AXWindow`, subrole `AXDialog`, `AXModal` true | `dialog` with state `modal` | title `Fixture Alert`, description `alert` | `state` `fixture.alert` true while it runs. |
| `fixture.alert.ok` | `AXButton` | `button` | `OK` | Logs an `action` and dismisses the alert. |
| `fixture.alert.cancel` | `AXButton` | `button` | `Cancel` | Same. |

## What a snapshot should show

The native facts in this section are asserted by `test.sh`. How agent-desktop and Asura present them is read from the pinned sources and still has to be observed through the governed path.

**Secure field.** Native role `AXTextField` with subrole `AXSecureTextField`. agent-desktop reports role `textfield`, name `Secret`, the state `secure`, and no value. Asura's projection drops any value on a secure node as well. Assert that the node has `secure` in its states and no value while the field holds text, that `desktop.type_text` and `desktop.set_value` on it fail with `desktop_secure_field` before an approval card appears, and that the log has no `fixture.secure-field` `state` line after the launch one.

**Sheet.** The sheet is an `AXSheet` child of the main window, one level below it, and its three buttons are its children. While it is attached, the application's focused window is the sheet, not the main window. The main window's controls stay in the tree. agent-desktop's `Sheet` surface looks for an `AXSheet` on the focused window or among its direct children. Its `Alert` surface also matches an `AXSheet`, so the unsaved-changes sheet answers to both surfaces. Assert a `sheet` node with `Save`, `Cancel` and `Don't Save` buttons.

**Modal alert.** The alert is a top-level window of its own, not a child of the main window. Native role `AXWindow`, subrole `AXDialog`, `AXModal` true, title `Fixture Alert`, and it is the application's focused window while it runs. agent-desktop maps it to role `dialog` and finds it through the `Alert` surface. The `Sheet` surface must not find it. Assert a `dialog` node with state `modal` and `OK` and `Cancel` buttons. The main window stays in the tree while the alert runs.

## Event log

One JSON object per line, UTF-8, keys sorted, appended with a single `write`. Every line has these fields.

| Field | Meaning |
|---|---|
| `event` | The event name. |
| `pid` | The fixture's process id. A log file can hold several runs. |
| `seq` | Starts at 1 for each process and increases by one per line. A gap means a lost line. |
| `time` | UTC, ISO 8601 with milliseconds. |

| Event | Extra fields | Written when |
|---|---|---|
| `launched` | `bundleIdentifier`, `formatVersion` (1), `quitAfterSeconds`, `slowActionSeconds`, `selfExercise` | The process starts, before any window exists. |
| `state` | `control`, and `value`, or `length`, or `firstVisibleRow` with `lastVisibleRow` | Once per control at launch, then whenever the observed state differs from the last one logged. |
| `ready` | `windowNumber`, `windowVisible` | The window is up and the pid file is written. Wait for this line before acting. |
| `action` | `control`, `count`, plus `instance` for a twin and `stallSeconds` for `Slow Action` | AppKit delivered one activation to that control. `count` is that control's running total in this process. |
| `alert-on-screen` | `control` | The application-modal alert's window is on screen. It is logged from inside the modal session, after the alert's `state` line, so a driver that needs the second window waits for this line. |
| `key` | `focus`, `keyCode`, `characters`, `modifiers` | A key-down reached the application. In the secure field the line has only `focus` and `"redacted": true`. |
| `control` | `command`, `accepted`, plus `stallSeconds` for `stall` | A control-file command was read. |
| `stall-ended` | `control` or `command` | The main thread is answering again. |
| `quit-requested` | `sheetAttached` | The Quit menu item or a quit Apple event arrived. With a sheet attached the fixture stays open, like a document application waiting for its prompt. |
| `inventory` | `stage`, `control`, `depth`, `role`, `subrole`, `title`, `description`, `modal`, `actions`, `focusedWindow` | Self-exercise only. |
| `exiting` | `reason`: `quit-after`, `signal`, `quit-requested`, `self-exercise-complete` or `pid-file-failed` | Last line of a process. A SIGKILL leaves none. |

The `state` controls are `fixture.window` (visible), the two text fields (`value` is the text), `fixture.secure-field` (`length`), `fixture.checkbox`, `fixture.popup` (item title), `fixture.disclosure`, `fixture.scroll-view`, `fixture.table` (selected row), `fixture.stale-target` (present in the tree), `fixture.sheet` and `fixture.alert`.

The status label shows the same event as `#<seq> <event> key=value ...` with JSON values, for example `#31 action control="fixture.action-button" count=1`. A snapshot or `desktop.wait` can read it as the value of the `Status` static text. Asura truncates displayed text at 128 bytes, so a long text-field value can cut the mirror short. The log is the authority.

### Reading the log for exactly-once delivery

- A press-like action (`desktop.click`, `desktop.toggle`, `desktop.expand`, a menu pick) is delivered exactly once when the log gains exactly one `action` line for that control, with `count` one higher than before.
- `desktop.press_key` is delivered exactly once when the log gains exactly one `key` line with the expected `focus`, `keyCode` and `modifiers`.
- `desktop.type_text` appends, so a double delivery shows as doubled text in the field's `state`.
- State is sampled every 50 ms and around every action, and identical states are not logged twice. A write that leaves the value as it was, such as `desktop.set_value` with the text already there, leaves no `state` line. Use a different value when the count matters.
- A refusal before dispatch is proven by the absence of lines. Record `seq` before the attempt and assert that no `action`, `key` or `state` line follows it.
- agent-desktop answers an element already in the requested state, such as `desktop.toggle` with `check` on a checked `Enable Option`, as a success with nothing delivered. Asura then reports `already_in_state` true and `delivery` `not_delivered`, and the log gains no `action` line. Whether the fixture's controls are answered this way has not been observed.

## Control file

Append a line to the control file to change the fixture out of band, the way a user or another program would. The fixture polls the file every 50 ms and ignores whatever the file held before launch. Each command is answered by a `control` line. Unknown commands are logged with `"accepted": false`.

| Command | Effect |
|---|---|
| `remove-stale-target` | Removes the `Stale Target` button from the tree. Refs minted for it are now stale. |
| `show-sheet` | Attaches the unsaved-changes sheet without any press. |
| `show-alert` | Runs the modal alert without any press. |
| `close-window` | Closes the main window. The application keeps running with no window. |
| `stall` | Holds the main thread for `--slow-action-seconds` with no input in flight, then logs `stall-ended`. Wait for the `control` line before acting. |
| `reset` | Dismisses the sheet and the alert, clears the fields, unchecks, reselects `Vanilla`, collapses, scrolls to the top, restores `Stale Target`, and shows the window again. Counts and `seq` keep running. |

## ADR 0061 evidence this fixture serves

### Policy runs

Use `Record Action` for interaction and a snapshot of the main window for observation.

| Policy | Expected | Fixture witness |
|---|---|---|
| Off | No `desktop.*` tool reaches the fixture. | No line after `ready`. |
| Ask | Every action waits for an approval card. Approve once. | Exactly one `action`, `count` 1. A denied card leaves no line. |
| Auto | `desktop.snapshot` runs without a card. `desktop.click` still asks, because the broker escalates interaction to exact human approval. | No line for the snapshot. One `action` after the approval. |
| Confirmed YOLO | `desktop.click` dispatches without a card. | Exactly one `action` per tool call. |

### Closed failure codes

The codes are `AgentDesktopStableCodes`. The recipes marked "expected" follow from the pinned agent-desktop source and Asura's mapping. None has been observed through the governed path yet.

| Code | How to produce it with the fixture | Fixture witness |
|---|---|---|
| `desktop_stale_ref` | Snapshot, write `remove-stale-target`, wait for its `control` line, click the old `Stale Target` ref. Asura also refuses with this code, before any approval card, a `desktop.close_app` naming the fixture before any `desktop.list_apps`, `desktop.list_windows` or `desktop.snapshot` result has shown it. | No `fixture.stale-target` `action`. No `quit-requested` line for the close. |
| `desktop_ambiguous_target` | Observed live: start a second instance of the bundle (`open -n`), then snapshot by the display name. Two running applications answer to the name, and Asura refuses it before the library is asked anything. A `Twin Action` ref is not ambiguous: each twin has its own ref, and the live run delivered a twin's click to exactly that twin. Nor is a Window snapshot while the modal alert is up: the library answers `AMBIGUOUS_TARGET` only for the instant before the alert becomes the focused window, then snapshots it. | No `action` in either instance. |
| `desktop_action_outcome_unknown` | Click `Slow Action` with the default 8 second stall. The press is delivered and the reply never arrives inside the budget. Put a second action in the same batch. | Exactly one `fixture.slow-button` `action`, never two. No line from the rest of the batch. |
| `desktop_timeout_before_delivery` | Expected: write `stall`, wait for its `control` line, then click `Record Action`. The application answers nothing, so resolution times out before any press is sent. | No `action` between the `control` line and well after `stall-ended`. |
| `desktop_secure_field` | `desktop.type_text` or `desktop.set_value` on `Secret`. | No `fixture.secure-field` `state` change. |
| `desktop_unsaved_changes` | Attach the sheet, snapshot, then `desktop.close_app` with `app` set to `Desktop Automation Fixture`. The snapshot is also the observation the close is bound to. | No `quit-requested` line. Without the sheet, a `desktop.close_app` approved after a listing or snapshot that showed the fixture logs `quit-requested` with `"sheetAttached": false`, then `exiting`. |
| `desktop_window_not_found` | Expected: write `close-window`, then snapshot with `surface` `window`. | `fixture.window` `state` false. |
| `desktop_app_not_found` | Run `stop.sh`, then snapshot the fixture by name. | `exiting` is the last line. |
| `desktop_policy_denied` | Expected: a double click (`kind` `double`) on `Record Action`. agent-desktop has only a physical double click, and the first slice is headless. Asura itself also answers with this code, before any approval card, a `desktop.type_text` or `desktop.set_value` whose text looks like a literal secret. | No `action`. |
| `desktop_not_supported` | Expected: `desktop.set_value` on `Record Action`, since a button is not editable. | No `action`, no `state`. |
| `desktop_action_failed` | Not settled. Clicking `Disabled Action` is the candidate, but whether agent-desktop answers a disabled target with this code or with a timeout is not established. | No `fixture.disabled-button` `action` in either case. |
| `desktop_permission_denied` | Run Asura without the Accessibility grant and snapshot the fixture. | No line after `ready`. |
| `desktop_invalid_arguments`, `desktop_cancelled` | Any fixture control as the target. The fixture adds nothing specific. | No line when the refusal precedes dispatch. |

`desktop_self_target`, `desktop_unavailable` and `desktop_adapter_faulted` are about Asura's own processes, a missing library and the panic-injection library. The fixture's part there is to be an innocent target whose log stays empty.

### Stale-ref run

1. `run.sh`, wait for `ready`.
2. `desktop.snapshot` of the main window. Keep the `Stale Target` ref.
3. Append `remove-stale-target` to the control file. Wait for the `control` line and the `fixture.stale-target` `state` false.
4. `desktop.click` on the kept ref, approved.
5. Expect `desktop_stale_ref`, and no `action` for `fixture.stale-target` anywhere in this process's log.

### Outcome-unknown run

1. `run.sh` with the default `--slow-action-seconds 8`, wait for `ready`.
2. Snapshot, then propose `desktop.click` on `Slow Action` followed by `desktop.click` on `Record Action` in one batch.
3. Expect `desktop_action_outcome_unknown` for the first, `tool_batch_reconciliation_required` for the second, and `desktop_stale_ref` for every further desktop mutation except `desktop.launch_app` until a fresh snapshot.
4. In the log, expect exactly one `fixture.slow-button` `action` with `count` 1, a `stall-ended` about eight seconds later, and no `fixture.action-button` `action`. A second `fixture.slow-button` `action` is the redispatch the ADR forbids.

### Other tools the fixture gives a target to

- `desktop.toggle` on `Enable Option`, `desktop.select` on `Flavor`, `desktop.expand` on `Details`, `desktop.scroll` on `Rows`, `desktop.clear` and `desktop.type_text` on the text fields.
- `desktop.press_key` with a text field focused. The `key` line names the focused control.
- `desktop.wait` with `condition` `text` for a value of the `Status` text, or with `condition` `window_title` and `value` `Fixture Alert` after `show-alert`. A wait yields no refs; snapshot afterwards.
- `desktop.launch_app` with `app` set to the display name `Desktop Automation Fixture` or the bundle identifier `dev.asura-fixture.desktop-automation`.
- `desktop.focus_window` with a `window_id` taken from a `desktop.list_windows` result the model was shown in the last two minutes; it takes no application name. `desktop.list_apps` should list `Desktop Automation Fixture` once, and `desktop.list_windows` its main window once.
- `desktop.close_app` with `app` set to the display name or bundle identifier exactly as a `desktop.list_apps`, `desktop.list_windows` or `desktop.snapshot` result of the last two minutes showed it. The close is bound to the process that observation reported; a name not observed in the run is refused as `desktop_stale_ref`, and so is a fixture that quit or restarted since.
- A snapshot with `surface` `menubar` for `Fixture`, `Record Menu Action` and the `Variants` submenu.

## Collecting the ADR 0061 evidence

```bash
./scripts/acceptance/desktop-automation/collect-evidence.sh
```

`collect-evidence.sh` verifies the staged agent-desktop build, builds this fixture, and runs `tests/Asura.DesktopAutomationAcceptance.Tests` with `ASURA_DESKTOP_EVIDENCE=1`, the variable that lets those tests launch the fixture. It writes `receipt.json`, `observations.jsonl` and the test results under `.test-results/desktop-automation-evidence/<UTC time>/`. `build_evidence_receipt.py` maps every test to one evidence item, and `validate_evidence_receipt.py` checks the receipt's shape. A test that needs the Accessibility grant is skipped with that reason and never counts as passed. [docs/acceptance/adr-0061-desktop-automation.md](../../../docs/acceptance/adr-0061-desktop-automation.md) lists which item proves which part of the ADR, what is still outstanding, and the manual update-survival procedure.

## What is and is not verified

Verified on macOS 27.0 (Apple Silicon, Swift 6.4): the bundle builds warning-free and passes `codesign --verify --strict`, the window appears on screen, the pid file is written and removed, the timer and SIGTERM exits are logged, every control logs as described when activated in-process, and an accessibility client is told the roles, names and action names in the inventory.

Not verified: anything that needs Asura's Accessibility grant. No `desktop.*` tool has operated this fixture yet, so the snapshot roles, the surface behaviour and every recipe marked "expected" still need a real governed run.
