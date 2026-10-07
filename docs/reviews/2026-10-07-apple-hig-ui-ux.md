# Apple HIG UI/UX review, 2026-10-07

This review checks Asura's macOS experience against Apple's Human Interface Guidelines. It uses the guidelines current for macOS 26 and 27, including the Liquid Glass guidance. The review covers every HIG area that applies to a desktop app: foundations, patterns, components, inputs, and writing.

## Scope and method

- **Build under review.** `main` at `a878ce49`, plus the installed 0.1.81 (154) app bundle from 2026-10-06.
- **Rendered screens.** I captured 82 screens with the design-QA harness (`tools/Asura.DesignQa`) at 1440 × 900. They cover dark, light, high contrast, and 200% text. They include the workspace shell, every settings page, the overlays, the agent states, Git, Database, Docker, Redis, Files, Statistics, Kubernetes, and the editor and confirmation dialogs.
- **Running app.** I read the menu bar, window title, and title-bar tab strip of the running app through the macOS accessibility API. I only read; nothing was clicked or changed.
- **Code audit.** Five passes, one per HIG area: menus and keyboard, windows and modality, accessibility, visual foundations, and components plus writing. Each finding cites `path:line`. I re-read the source for every P0 and every high-impact P1 before including it.

Severity scale:

| Level | Meaning |
|---|---|
| P0 | Breaks a platform contract, blocks a user group, or affects other apps. Fix before the next release. |
| P1 | A clear HIG violation that most Mac users will notice. |
| P2 | An inconsistency or a gap that adds up across the app. |
| P3 | Polish. |

Finding IDs use one prefix per area: `M` menus and keyboard, `W` windows and modality, `A` accessibility, `V` visual foundations, `C` components and patterns, `T` writing, `S` observations from specific screens.

## Summary

Asura already has much of what a Mac app needs. The traffic lights are native, there is a token-based design system, the app follows the system accent and appearance, and an Icon Composer app icon is in place. Every icon-only button has an accessible name. Destructive confirmations are never the default button, and file import and export use the native Open and Save panels. Reduce Motion and Reduce Transparency are observed live. Few cross-platform apps get that far.

The gaps come in clusters. The menu bar is still a sketch: the Edit menu is empty, and most commands exist only behind a tmux-style ⌃B prefix or in the command palette. Several defaults collide with macOS: ⌘` is taken system-wide for Quick Terminal, Escape never reaches the shell in Quick Terminal, ⌃B is eaten everywhere, and ⇧⌘3/4/5 are bound to app commands. VoiceOver can't read the two surfaces that matter most, the terminal and the agent transcript. On the visual side, glass sits under content instead of over it. The accent color means six different things, and white-on-accent text fails contrast in dark mode. The UI copy reads like a cross-platform web app: sentence-case buttons, "Ctrl+B, X" shortcut notation, six names for Keychain, and implementation terms such as "adapter" and "desktop milestone".

None of this needs a redesign. Most fixes land in a handful of shared places: `MainWindow.axaml` menus, `BuiltInKeymaps.cs` and `QuickTerminalSettings.cs` defaults, `DialogShell` and `ConfirmationDialog`, `AvaloniaHostAppearanceAdapter`, and the three or four control themes that suppress focus. A short list of changes would remove most of the friction a Mac user feels in the first hour.

### Fix first

1. Fill the Edit menu and add Window, View, and Help items ([M-01](#m-01), [M-03](#m-03), [M-04](#m-04)).
2. Change the Quick Terminal hotkey default and stop capturing Escape ([M-07](#m-07), [M-08](#m-08)).
3. Make the macOS keymap the default application profile and make the ⌃B prefix opt-in ([M-09](#m-09)).
4. Expose terminal and agent text to VoiceOver ([A-01](#a-01), [A-02](#a-02), [A-03](#a-03)).
5. Restore visible keyboard focus on every control, and fix white-on-accent contrast ([A-04](#a-04), [A-05](#a-05)).
6. Give every dialog the same keyboard contract: Esc and ⌘. cancel, Return confirms safe actions, and the SSH key replacement never defaults ([W-02](#w-02), [W-03](#w-03)).
7. Make content surfaces opaque by default, and honor Reduce Transparency and Reduce Motion everywhere ([V-02](#v-02), [A-08](#a-08), [A-11](#a-11)).
8. Fix tab strip overflow, which hides tabs in the running app ([C-11](#c-11)).
9. Stop replacing the Dock icon at runtime ([V-01](#v-01)).
10. Use title case for buttons and menus, and settle one term for each concept ([T-01](#t-01), [T-02](#t-02)).

### Scorecard

| HIG area | Rating | P0 | P1 | P2 | P3 |
|---|---|---|---|---|---|
| Menus, commands, keyboard | Weak | 3 | 5 | 12 | 2 |
| Windows, modality, presentation | Fair | 0 | 6 | 10 | 4 |
| Accessibility and inclusion | Weak for VoiceOver, good for display settings | 2 | 11 | 7 | 1 |
| Visual foundations | Good base, wrong defaults | 0 | 3 | 11 | 6 |
| Components and patterns | Fair | 0 | 5 | 8 | 1 |
| Writing | Fair | 0 | 1 | 5 | 2 |
| Screen observations | n/a | 0 | 0 | 4 | 11 |

Ratings are relative to a native AppKit app. "Good" means a Mac user wouldn't notice the difference.

## 1. Menus, commands, and keyboard

HIG pages: The menu bar, Menus, Context menus, Dock menus, Pull-down buttons, Keyboards, Searching, Undo and redo.

<a id="m-01"></a>
### M-01 · P0 · The Edit menu is empty

The HIG says every Mac app needs an Edit menu with Undo, Redo, Cut, Copy, Paste, Paste and Match Style, Delete, Select All, and a Find submenu, even when the app isn't document-based.

`src/Asura.App/Views/MainWindow.axaml:36-40` declares `<NativeMenuItem Header="Edit">` with an empty `<NativeMenu />`, and nothing fills it at runtime. Undo exists in exactly one place, saved-screen deletion (`ViewModels/SavedScreenDeleteUndoViewModel.cs`).

Mac users find Copy, Paste, and Undo through the menu bar and Help search. Voice Control and Full Keyboard Access depend on those menu items too. CEF may also rely on Edit key equivalents for ⌘C and ⌘V. Menu key equivalents run before Avalonia `KeyDown` (`QuickTerminalController.cs:199-203`), so once the items exist, every handler has to route to the focused control.

**Fix.** Add the standard items with gestures (`Meta+Z`, `Meta+Shift+Z`, `Meta+X/C/V`, `Meta+Alt+Shift+V`, `Meta+A`). Route each one to the focused element: `TextBox.Undo/Cut/Copy/Paste/SelectAll`, the terminal's copy, paste, and select-all commands, or the CEF frame. Bind `IsEnabled` to `CanUndo`, `CanCut`, and `CanPaste`. Add the Find submenu from [M-11](#m-11).

<a id="m-02"></a>
### M-02 · P1 · Most commands are missing from the menu bar

The HIG says the menu bar should list all of an app's commands, including ones that are also reachable elsewhere, so people can find and search them.

The menu bar has 13 items (`MainWindow.axaml:25-63`). The command registry has 34 (`src/Asura.Core/BuiltInCommands.cs:57-106`): split, focus, and zoom panel; rename, move, and select tab; copy mode; find; font size; and clear scrollback. Those are reachable only through prefix keys or the palette. Going the other way, the menu-only actions (New Window, Toggle Agent, Layout Designer, Quick Terminal in `ShellCommandExecutor.cs:6-21`) aren't registry commands. They don't show up in the palette and can't be rebound. Git, Files, Database, and Docker commands live only inside their panels.

**Fix.** Build the native menu from `CommandRegistry` and merge `NativeMenuCommand` into it. Add app-specific menus (Terminal, Tab, Panel, Workspace), plus a Git menu that is dimmed when no Git panel is focused.

<a id="m-03"></a>
### M-03 · P1 · The Window and View menus lack standard commands

The HIG asks for Minimize (⌘M), Zoom, Bring All to Front, and a window list in the Window menu, explicitly so Full Keyboard Access users can manage windows. It asks for Enter Full Screen (⌃⌘F) in the View menu, and Close Window in the File menu.

- `MainWindow.axaml:52-63` has only Add Panel…, Layout Designer…, Previous Tab, Next Tab, and Close Panel.
- Avalonia.Native adds only the app-menu items. There is no `setWindowsMenu:`.
- `BuiltInCommands.cs` has no window or full-screen commands.
- Close Panel (⇧⌘W) lives in the Window menu.
- ⌘W returns early outside a workspace (`MainWindow.RuntimeWorkspace.cs:388-401`).
- ⌘T is New Tab in the menu but New Terminal in the window key handler (`MainWindow.axaml.cs:1993-1997`).

**Fix.** Window menu: Minimize, Zoom, separator, Bring All to Front, then a dynamic list of main windows (radio items, checked for the key window). View menu: Enter/Exit Full Screen. File menu: Close Window on ⇧⌘W, with Close Panel moved to ⌥⌘W. Make ⌘T mean one thing.

<a id="m-04"></a>
### M-04 · P2 · There is no Help menu

The HIG puts a Help menu at the trailing end of the menu bar. Its search field also searches menu items, and ⌘? opens it.

Top-level menus are File, Edit, View, and Window (`MainWindow.axaml:25-63`), and `tests/Asura.Architecture.Tests/NativeMenuContractTests.cs:56` locks that order. Since most commands sit behind prefixes ([M-02](#m-02)), menu search would be especially useful here.

**Fix.** Add Help with Asura Help (the docs URL), Keyboard Shortcuts, Release Notes, and Report an Issue…, and update the contract test.

<a id="m-05"></a>
### M-05 · P2 · The menu bar disappears when a non-main window is key

The HIG says to always show the same set of menus.

`NativeMenu.Menu` is declared only on `MainWindow.axaml:23-65`, and `QuickTerminalWindow.axaml` and `Controls/RuntimePanelHostWindow.cs` declare none. In the running 0.1.81 build, with the main window not key, the accessibility API showed only the Apple and Asura menus. When Quick Terminal, a floating panel, or any dialog window is key, File, Edit, View, and Window are expected to vanish.

**Fix.** Attach the same menu (one shared factory) to every top-level window. Dim items that don't apply instead of removing them.

<a id="m-06"></a>
### M-06 · P3 · App menu details

- The Quit item reads "Quit"; the HIG and every Apple app use "Quit Asura". This was observed in the running app and comes from Avalonia's default.
- "About Asura…" carries an ellipsis and opens a Settings page (`App.axaml:10-20`, `App.axaml.cs:307-312`). The HIG item is "About Asura", followed by a separator, and opens a small About window.
- Check for Updates… shares the About group.

**Fix.** Rename Quit (Avalonia supports overriding the default items). Drop the ellipsis, open a dedicated About window, and put Check for Updates… with Settings….

<a id="m-07"></a>
### M-07 · P0 · The Quick Terminal hotkey takes ⌘` from every app

The HIG lists ⌘` (activate the next window of the frontmost app) as a standard shortcut that apps must not repurpose.

The default is `new KeyStroke("GRAVE", KeyModifiers.Meta)` (`src/Asura.Core/QuickTerminalSettings.cs:27`). It is registered as an exclusive Carbon hotkey (`src/Asura.Desktop/MacOsGlobalHotkeyService.cs:70-76`). Worse, if the user's own hotkey fails to register, `QuickTerminalController.cs:456-463` silently registers ⌘` instead.

While Asura runs, window cycling can break in every app, including Asura's own ⌘N windows. A user who chose another hotkey can still lose ⌘`.

**Fix.** Ship with no hotkey, or one outside Apple's standard table (⌥Space or ⌃`), and offer to set it on first run. Remove the silent fallback and show the registration failure in Settings. The recorder should warn about reserved chords ([M-13](#m-13)).

<a id="m-08"></a>
### M-08 · P0 · Escape never reaches the shell in Quick Terminal

The HIG says Esc cancels the current action, and in a terminal the current action belongs to the program running in it.

- `QuickTerminalController.cs:915-924` calls `BeginEscapeCapture()` on activation.
- `MacOsGlobalHotkeyService.cs:121-128` registers Escape as an exclusive hotkey.
- `Views/QuickTerminalWindow.axaml.cs:351-369` handles Escape at the window level with `e.Handled = true` and hides the panel.
- Settings states it outright: "Escape always hides the Quick Terminal panel." (`SettingsPages/QuickTerminalSettingsPageView.axaml:217`).

vim, less, fzf, readline vi-mode, and TUI agents never see Esc in Quick Terminal. Leaving insert mode in vim hides the panel instead.

**Fix.** Remove the global Escape capture. Dismiss with the toggle hotkey, ⌘W, focus loss, or an opt-in "Escape hides Quick Terminal" setting that is off by default. Keep Esc only for Asura's own pending prompts (`TryCancelPendingInteraction`).

<a id="m-09"></a>
### M-09 · P1 · The default keymap eats ⌃B everywhere

The HIG advises against Control as a shortcut modifier because the system and text editing use it.

The default application profile is the tmux-like one (`ApplicationKeyController.cs:32`, `_activeProfileId = BuiltInKeymaps.TmuxApplicationId`). Its prefix is `new("B", KeyModifiers.Control)` with `DiscardAndShowHint` (`BuiltInKeymaps.cs:7, 37-41`). The window tunnel handler claims the key before any control sees it (`MainWindow.axaml.cs:84, 1940-1951`), and the active contexts follow workspace visibility, not focus (`MainWindowViewModel.cs:2152-2169`).

A lone ⌃B (readline back-char, vim page-up, nested tmux) is dropped after 750 ms. ⌃B followed by an unbound key shows a hint. This happens in the agent composer, the SQL editor, and the browser too. The Keybindings page confirms "Active application profile · tmux-like application".

**Fix.** Default to a macOS profile built on ⌘ shortcuts. Make the tmux prefix an explicit choice, on first run or in Settings. When it's on, replay the prefix on timeout and don't arm it while a text field, the editor, or the browser has focus.

<a id="m-10"></a>
### M-10 · P1 · ⇧⌘3, ⇧⌘4, and ⇧⌘5 are bound to "Move tab to workspace"

These are the system screenshot shortcuts.

`BuiltInKeymaps.cs:108-119` binds `MoveTabToWorkspace` to digit + ⌘⇧ for 1 through 9. Three of the nine never reach the app; they take a screenshot instead.

**Fix.** Use ⌥⇧⌘digit, or a "Move Tab to Workspace ▸" submenu. Add the reserved-shortcut check from [M-13](#m-13).

<a id="m-11"></a>
### M-11 · P1 · No Edit ▸ Find, and ⌘F works in only two places

The HIG Find submenu includes Find (⌘F), Find Next (⌘G), Find Previous (⇧⌘G), and Use Selection for Find (⌘E).

⌘F exists only in the terminal keymap (`BuiltInKeymaps.cs:144`) and the browser panel (`BrowserRuntimePanelView.axaml.cs:259-275`). Files, Git, Database, Docker, and Kubernetes have search fields that ⌘F doesn't reach. Next and previous match use Return or F3 in the terminal and Return or ⇧Return in the browser.

**Fix.** Add Edit ▸ Find that routes to the active panel's search or filter field, with ⌘G, ⇧⌘G, ⌘E, and ⌥⌘F to focus the panel search.

<a id="m-12"></a>
### M-12 · P2 · Shortcuts appear in five notations

The HIG shows shortcuts with the modifier symbols ⌃⌥⇧⌘, in that order.

| Where | Notation | Source |
|---|---|---|
| Command palette | "Ctrl+B, X", "Alt+Meta+ARROWLEFT" | `MainWindowViewModel.cs:9558` → `KeyStroke.ToString` |
| Keybindings | "Ctrl+B, ←", "Option+Cmd+1" | `KeySequenceDisplay.cs:56-83` |
| Quick Terminal | "Command + `" | `QuickTerminalHotkeyText.cs:73-102` |
| Shortcut recorder | "Meta+K" | `ShortcutRecorderDialog.axaml.cs:117-119` |
| Native menus and one tooltip | ⌘K, "(⌘⏎)" | `DatabaseWorkspaceView.axaml:391` |

Most tooltips for actions that have a shortcut (New tab, Close tab, Toggle agent panel, Add panel) don't show it.

**Fix.** Use one formatter that emits ⌃⌥⇧⌘ and key glyphs (← ↩ ⌫ ⎋) everywhere, and add shortcuts to tooltips. On macOS, write the prefix sequence as "⌃B X".

<a id="m-13"></a>
### M-13 · P2 · Keymap validation ignores menu and system shortcuts

`Core/KeymapConflictValidator.cs:31-80` checks conflicts only within a profile. A user can bind ⌘W, ⌘Q, ⇧⌘4, or ⌃Space without a warning, and the binding never fires.

**Fix.** Add "shadowed by menu" and "reserved by macOS" checks, seeded from the HIG standard-shortcut table. Use them in both the keybinding recorder and the Quick Terminal hotkey field.

<a id="m-14"></a>
### M-14 · P2 · Custom shortcuts reuse standard ones for unrelated commands

The HIG says not to make a new shortcut by adding a modifier to a standard one.

- Toggle Agent Panel is ⇧⌘A (`MainWindow.axaml:48`); that's Deselect All in many apps.
- Clear Scrollback is ⇧⌘K beside the ⌘K palette (`BuiltInKeymaps.cs:148-153`). In Terminal and iTerm2, ⌘K clears.
- The database grid uses ⌘I for Add Row (`DatabaseWorkspaceView.ContextMenu.cs:60`); ⌘I is Get Info.

**Fix.** Move Agent and Add Row to chords no other app uses. Consider ⌘K for Clear, matching Terminal, and moving the palette to ⇧⌘P or ⌘P.

<a id="m-15"></a>
### M-15 · P2 · Context menus are missing in key places and inconsistent where present

The HIG asks for context menus wherever people expect them. Order items by use, put destructive items last behind a separator, keep to about three groups, and use title case.

- No context menu on the terminal (right-click goes to the PTY, `ManagedTerminalSurface.cs:960-966`), tabs, Docker containers and images, Kubernetes resources, processes, or rail tiles.
- Casing varies. The Git branch menu says "New Branch…" and "Copy Branch Name" (`GitRuntimePanelView.axaml:596-608`); the commit menu says "New branch…" and "Copy commit SHA" (`:1223-1242`). Files says "New folder…" and "Open with default app" (`FileActionViewModel.cs:114-131`).
- Destructive items aren't last. On a branch, Delete… comes before Copy Branch Name (`GitRuntimePanelView.axaml:599-608`).
- The database grid menu has up to nine separators (`DatabaseWorkspaceView.axaml:676-812`).

**Fix.** Add terminal (Copy, Paste, Select All, Clear Scrollback, Split ▸, Find…), tab (Rename Tab…, Change Icon…, Move to Workspace ▸, Close Tab, Close Other Tabs), and list-object menus that mirror each panel's toolbar. Use one casing and ordering rule, and also expose every item in the menu bar ([M-02](#m-02)).

<a id="m-16"></a>
### M-16 · P2 · Git's "Repository" menu is a menu bar inside the panel

The HIG keeps menus short, with one submenu level and roughly five items per submenu, and keeps settings in Settings.

`GitRuntimePanelView.axaml:253-331` puts an Avalonia `Menu` (a menu bar control) in the panel with one Repository item. It holds about 50 items across Git Flow, Tracking, Worktrees, Submodules, Git LFS, and Hosting submenus. It mixes preferences ("Git preferences…", "Identity and signing…"), diagnostics ("Benchmark status read"), and destructive commands. One `IsEnabled="{Binding CanMutateRepository}"` disables read-only items like Statistics while the repository is busy.

**Fix.** Split it into a few pull-down buttons (Branch, Remote, Tools). Move preferences to Settings, hide Benchmark outside debug builds, and decide enablement per item. Mirror the commands in a Git menu-bar menu.

<a id="m-17"></a>
### M-17 · P2 · Terminal find is a painted field

`ManagedTerminalSurface.cs:2302-2303` draws `"Find: {query}▏"` as text. Lines 1374-1377 swallow every ⌘, ⌥, and ⌃ key while find is open, and Backspace is the only editing key. People can't paste an error message to search for it, can't move the caret, and can't undo, and VoiceOver doesn't see a field.

**Fix.** Use a real `TextBox` find bar. The browser panel already has one (`BrowserRuntimePanelView.axaml:331-355`).

<a id="m-18"></a>
### M-18 · P2 · The Files panel lacks Finder keys, and deletes are permanent

- Only ⌘C, ⌘X, and ⌘V are handled (`FileRuntimePanelView.axaml.cs:372-391`), and those lines also accept Control on macOS (`:828-830`).
- Opening an item is double-click only.
- No list in the app enables type-to-select (`IsTextSearchEnabled` appears nowhere).
- Every local delete is "Delete permanently" (`Views/Confirmations.cs:24-28`).

**Fix.** Return or ⌘↓ opens, ⌘↑ goes to the enclosing folder, ⌘⌫ deletes, ⇧⌘N makes a new folder. Turn on type-to-select in file, branch, container, and pod lists. For local files, move to the Trash with `NSWorkspace recycle` and support ⌘Z.

<a id="m-19"></a>
### M-19 · P2 · Menus built from buttons

`WorkspaceView.axaml:119-186` builds the Workspaces menu as a Flyout of `Button.FlyoutMenuItem` rows. Each row has an inline × "Terminate this workspace…" next to Open, and the menu contains a `ToggleSwitch` ("Show workspaces panel"). The Docker narrow-mode picker marks the current view with a style class instead of a checkmark (`DockerRuntimePanelView.axaml:221-224, 444-449`). Arrow keys and type-select don't work, VoiceOver announces buttons rather than menu items, and Terminate sits right next to Open.

**Fix.** Use `MenuFlyout` with checkable `MenuItem`s. Make Terminate a separate confirmed item, and move Show Sidebar to the View menu.

<a id="m-20"></a>
### M-20 · P2 · Keyboard focus can't leave a terminal

`Controls/ManagedTerminalInput.cs:84` sends every Tab to the PTY, and no ⌃F6 or ⌃Tab handler exists. Panel focus moves only through the ⌃B arrow prefix. A keyboard-only user can't reach the tab strip, panel header buttons, or the agent panel from a terminal.

**Fix.** Add Move Focus to Next/Previous Area (⌃F6 and ⌃⇧F6), as AppKit does.

<a id="m-21"></a>
### M-21 · P2 · View menu items lack verbs and state

- `MainWindow.axaml:45-48` lists "Launcher", "Quick Terminal", and "Toggle Agent Panel". The HIG uses changing labels such as "Show Agent Panel" and "Hide Agent Panel".
- "Launcher" does the same thing as New Tab (`MainWindow.axaml.cs:548`).
- The sidebar toggle exists only inside the Workspaces flyout.
- Text size is bound to "+" only (`BuiltInKeymaps.cs:145`), so plain ⌘= may not work.

**Fix.** Use state-bound Show/Hide titles. Add Show/Hide Sidebar, Enter Full Screen, and Bigger, Smaller, and Actual Size (⌘+ accepting ⌘=, ⌘-, ⌘0). Drop "Launcher".

<a id="m-22"></a>
### M-22 · P3 · Menu items never dim

Every native item uses only `Header`, `Gesture`, and `Click` (`MainWindow.axaml:28-60`), and `ShellCommandExecutor.ExecuteNativeAsync` runs unconditionally. Previous Tab and Close Panel silently do nothing outside a workspace.

**Fix.** Use `NativeMenuItem.Command` with `CanExecute` driven by the active command contexts.

## 2. Windows, modality, and presentation

HIG pages: Windows, Panels, Sheets, Alerts, Popovers, Modality, Settings, Going full screen, Multitasking, Launching, Loading, File management.

<a id="w-01"></a>
### W-01 · P1 · Settings replaces the workspace instead of opening a window

The HIG says Settings… opens a separate, non-modal settings window. Its title names the current pane, it reopens on the last pane, and its minimize and zoom buttons are dimmed.

- Settings is a route inside the main window (`MainWindow.axaml:100-196, 552-554`), with a Back arrow and a static "Settings" header (`SettingsView.axaml:15-41`).
- ⌘, always lands on Appearance (`MainWindow.Settings.cs:95-98`, `MainWindowViewModel.cs:2503`).
- Esc and ⌘W don't leave Settings (`MainWindow.axaml.cs:1957-1969`).
- About also routes here (`App.axaml.cs:307-312`).
- Each window has its own Settings instance.
- The sidebar section is headed "Preferences" while the menu says Settings… (`SettingsView.axaml:66`).

Opening Settings hides live terminals, so people can't watch a setting take effect. In a terminal app, that's most settings.

**Fix.** Host `SettingsView` in one `SettingsWindow` with `CanMinimize="False"` and `CanMaximize="False"`. Keep the sidebar, set `Title` to the pane name, and persist the last pane. ⌘, brings the existing window forward and ⌘W closes it.

<a id="w-02"></a>
### W-02 · P1 · Dialogs don't share one keyboard contract

The HIG's alert and sheet rules: Esc and ⌘. cancel, Return activates the default button, and a sheet has a Cancel button.

- 14 of 34 dialogs have no Esc path at all: no `IsCancel`, no handler, no `ShowsClose`. They are AddWorkspaceTab, ConnectionSecretEditor, DatabasePasswordPrompt, FileAccessControl, FileTransfer, GitConflict, GitFileHistory, GitHosting, GitLfs, GitOutput, GitRebase, GitStatistics, GitSubmodule, and SecretEditor.
- `DialogShell`'s close button has `IsCancel="True"` but is visible only when `ShowsClose` is set (`Styles/DesignSystem.axaml:1552-1557`), which six dialogs do.
- Only 8 dialogs mark a default button.
- No `OemPeriod` handler exists, so ⌘. does nothing.
- The editor dialogs (Connection, AI provider, MCP server, Network connection, Saved screen, Saved connections) have no Cancel button. The rendered New Connection dialog offers only "Run diagnostics", "Save", and a × in the corner.

**Fix.** Give a shared `DialogWindow` base, or `DialogShell`, a footer that does the following:

- `Cancel` sets `IsCancel`.
- The primary action sets `IsDefault`, unless it is destructive.
- A tunnelled Esc and ⌘. handler runs Cancel.

Enforce it with an architecture test, as `NativeMenuContractTests` does for menus.

<a id="w-03"></a>
### W-03 · P1 · "Replace trusted key" is the Return default when a host key changes

The HIG says not to give a default button to an alert people must read, and to style unexpected destructive actions as destructive.

`Views/SshHostKeyReviewDialog.axaml:36` makes the confirm button `PrimaryButton` with `IsDefault="True"`. `SshHostKeyReviewDialog.axaml.cs:18-20` labels it "Replace trusted key" when `RequiresExplicitReplacement` is true, which is the possible-interception case. A reflexive Return accepts a possible man-in-the-middle key. The app's own rule in `ConfirmationDialog.axaml.cs:90-99` says destructive confirmations never default. `McpServerTrustConfirmationDialog.axaml:262-264` ("Trust and save", default) is similar.

**Fix.** In the replacement case, remove `IsDefault` and use the destructive style. Keep the default only for a first-seen key.

<a id="w-04"></a>
### W-04 · P1 · Quitting with several windows can stack alerts and leave the app half-closed

The HIG says to show one alert at a time, and to name it after the action.

Nothing handles `ShutdownRequested`. Each `MainWindow.OnClosing` cancels the close and runs its own async flow (`MainWindow.axaml.cs:154-173`, `ShellCloseCoordinator.cs:109-160`). Idle windows close immediately (`App.axaml.cs:1215-1226`), so if the user cancels in a later window, the earlier windows are already gone. A secondary window's close alert reads "Close Asura?" (`Confirmations.cs:344`) with "Close anyway" (`:361`).

**Fix.** Handle `ShutdownRequested`: collect running sessions across all windows and Quick Terminal, and show one alert, such as "Quit Asura? 3 sessions in 2 windows are still running", with Quit and Cancel. For a single window, ask "Close this window?" with a Close Window button.

Confidence is medium. Confirm at runtime how Avalonia 12.0.5 dispatches `Closing` on ⌘Q.

<a id="w-05"></a>
### W-05 · P1 · Window frames and extra windows aren't restored

The HIG says to relaunch in the state people left: the same windows, in the same places.

- `MainWindow.axaml:14-21` hard-codes 1440 × 900 and `CenterScreen`, and nothing persists the frame.
- All windows write the same recovery key, `"desktop.main-window"` (`ViewModels/RuntimeWorkspaceRecovery.cs:11`), so the last writer wins and the other windows are lost.
- Only the primary window restores (`App.axaml.cs:1259-1285`).
- ⌘N windows also open centered (`App.axaml.cs:249-267`), exactly on top of the existing window, so it can look like nothing opened.

**Fix.** Persist position, size, screen, and `WindowState` per window ID, or call `setFrameAutosaveName:` through the existing interop. Restore every window, and cascade new ones.

<a id="w-06"></a>
### W-06 · P1 · Starting an isolated workspace blocks the whole window

The HIG asks for loading progress in place, with the rest of the app usable and a way to cancel.

`MainWindow.axaml:643-656` puts an opaque, hit-testable `Border` at `ZIndex="1100"` over the rail, tab strip, and title-bar drag band while the container starts. `MainWindowViewModel.WorkspaceIsolation.cs:96-115` forces the workspace route during the start, which can include an image pull. There's no Cancel, other workspaces can't be reached, and the window probably can't be dragged.

**Fix.** Limit the progress state to that workspace's canvas, keep the chrome live, add Cancel, and show determinate progress when the provider reports it.

<a id="w-07"></a>
### W-07 · P2 · Every window is titled "Asura"

The HIG asks for window titles that tell windows apart.

`MainWindow.axaml:13` sets `Title="Asura"` and nothing updates it. The running app's accessibility tree confirms the title. Mission Control, the Window menu (once it exists), ⌘` previews, and the VoiceOver window chooser all show identical names.

**Fix.** Bind `Title` to "Workspace · Active tab", or "Settings". The visual title can stay hidden.

<a id="w-08"></a>
### W-08 · P2 · Alerts and editors are free-floating windows, not sheets

The HIG says a sheet attaches to its parent window, and an alert shows the app icon with a bold message and no window chrome.

- `ConfirmationDialog.axaml:1-13` is an ordinary `Window` titled "Confirm close", "Confirm delete", or "Asura error", with the question repeated below the title.
- No dialog sets `CanMinimize="False"`, so a modal dialog can be minimized while its parent stays blocked.
- `DialogShell` repeats the window title in its content and adds a second close glyph.
- The alert icon is a Fluent trash or warning glyph (`StateOverlayPresentation.cs:87-88`). The rendered "Delete connection?" alert stacks a danger callout and a warning callout inside the alert body.

**Fix.** Present confirmations with `NSAlert` through `beginSheetModalForWindow:` using the existing Objective-C interop. Present editors as sheet-positioned, non-minimizable windows with the native title hidden, or build an in-window sheet presenter that dims only the owner window.

<a id="w-09"></a>
### W-09 · P2 · Overlays black out the window

The HIG says to keep modal tasks short, avoid an "app within your app", and let transient UI close on an outside click.

- `ShellScrimBrush` is `#F20B0B0C`, 95% black, in both themes (`App.axaml:177-180`). It covers the whole window (`MainWindow.axaml:556-578`) and hides native views. The command palette blacks out the window even in light mode.
- The workspace editor is a 900 × 600-minimum modal with its own sidebar and navigation (`WorkspaceEditorView.axaml:12-70`). The layout designer is similar.
- The palette and the New Panel chooser close only with Esc, not with an outside click.

**Fix.** Use a light themed scrim (20–30% in light, 40–50% in dark) and close on an outside click. Anchor New Panel to its "+" as a flyout. Move the workspace editor and layout designer into non-modal windows.

<a id="w-10"></a>
### W-10 · P2 · Modals open on top of modals

The HIG says to dismiss one modal before presenting another, and only an alert may sit on top. Saved Connections (modal) opens the Connection Editor (modal), which opens the repository picker, the SSH host key review, or the secret editor (`MainWindow.axaml.cs:1009-1015, 1105-1130`; `ConnectionEditorDialog.axaml.cs:103, 119, 197`). Combined with [W-02](#w-02), Esc behaves differently at each level.

**Fix.** Make Saved Connections a Settings pane or a non-modal window, and create credentials inline.

<a id="w-11"></a>
### W-11 · P2 · Inspector-style Git windows are modal

File History, Statistics, LFS, and Submodules open with `ShowDialog` (`GitRuntimePanelView.Specialists.cs:13, 20`), which blocks the workspace while someone browses history. The HIG uses panels or separate windows for tools people consult while they work.

**Fix.** Use `Show(owner)`.

<a id="w-12"></a>
### W-12 · P2 · Settings mixes save models and silently drops drafts

Toggles apply immediately, but Appearance has Apply terminal and Cancel, Keybindings has Save keybindings, Browser has Save profile, and history retention has Apply (`SettingsView.axaml:156-159, 488, 799, 1061`). Leaving the Appearance pane discards the terminal draft without warning (`MainWindowViewModel.cs:1183-1190, 4846-4850`). Mac settings conventionally apply at once.

**Fix.** Apply immediately, with Revert or Undo. Where a draft must exist, ask before discarding it.

<a id="w-13"></a>
### W-13 · P2 · Closing the last window quits Asura

`App.axaml.cs:1215-1222` calls `desktop.Shutdown()` when no main window remains. Clicking the red button on the last window kills the Quick Terminal global hotkey and any background sessions. Mac terminal apps (Terminal, iTerm2, Ghostty) keep running and reopen a window from the Dock. There's also no Dock menu (`NativeDock` and `IActivatableLifetime` are unused).

**Fix.** Keep running, at least while Quick Terminal is enabled. Reopen on a Dock click (`ActivationKind.Reopen`), and add a Dock menu with New Window, New Terminal Tab, and Show Quick Terminal.

<a id="w-14"></a>
### W-14 · P2 · The minimum window size blocks Split View and tiling

`MainWindow.axaml:16-17` sets `MinWidth="1080" MinHeight="680"`. A half-screen tile on a 13–14" MacBook is about 735 pt wide, so Asura can't sit beside another app.

**Fix.** Lower the minimum to about 640 × 480 and collapse the rail and agent panel responsively. The responsive panel headers from `281b32f0` show the pattern already works.

<a id="w-15"></a>
### W-15 · P2 · Quick Terminal isn't configured as a panel

The HIG says panels float, join the current Space, and stay out of window cycling. `QuickTerminalWindow.axaml:11-20` sets `Topmost`, no decorations, and `ShowInTaskbar="False"`, but no `collectionBehavior` is set anywhere. Over another app's full-screen Space it may switch Spaces or fail to appear.

**Fix.** Set `canJoinAllSpaces | fullScreenAuxiliary | ignoresCycle | transient`, and consider a non-activating `NSPanel`. Confidence is medium-low; this needs a runtime check.

<a id="w-16"></a>
### W-16 · P2 · The close-with-running-sessions alert can't be silenced or undone

`Confirmations.cs:329-365` has no "Don't ask again" option, and there's no Reopen Closed Tab. Heavy terminal users will see this alert many times a day.

**Fix.** Add a suppression checkbox backed by a Settings toggle, as Terminal's "Ask before closing" does. Consider Reopen Closed Tab (⇧⌘T, once [M-03](#m-03) frees the shortcut).

<a id="w-17"></a>
### W-17 · P3 · The startup-failure window isn't laid out as an alert

`src/Asura.Desktop/DesktopStartupFailurePresenter.cs:70-124` shows a plain titled window with left-aligned "Try again" and "Open recovery workspace" buttons. It has no default button and no Quit.

**Fix.** Put Quit on the leading side, Try Again as the trailing default, and recovery as the alternate button.

<a id="w-18"></a>
### W-18 · P3 · A Settings gear sits in the title-bar toolbar

`WorkspaceView.axaml:410`. Mac apps rarely put Settings in a toolbar; ⌘, and the app menu cover it. Low priority, and harmless once [W-01](#w-01) lands.

<a id="w-19"></a>
### W-19 · P3 · A custom picker replaces the Open panel for local folders

The custom `GitRepositoryPickerDialog` is used even for local repositories (`GitRuntimePanelView.axaml.cs:270-273`), and worktree paths are typed (`:505-512`).

**Fix.** Use `StorageProvider.OpenFolderPickerAsync` for local targets, and keep the custom picker for remote ones.

<a id="w-20"></a>
### W-20 · P3 · Alert button copy

`FileAccessControlDialog.axaml:103` uses "Close" for cancel. The "Unable to close" error alert has a single "Close" button (`Confirmations.cs:367-377`). A notice explains its own buttons ("Close this dialog if you want to keep the definition…", `MainWindow.axaml.cs:1691`).

**Fix.** Cancel for cancel, OK for an acknowledgement, and no text that explains buttons.

## 3. Accessibility and inclusion

HIG pages: Accessibility, Inclusion, Color, Motion, Right to left, Charting data, Focus and selection.

<a id="a-01"></a>
### A-01 · P0 · VoiceOver can't read terminal output

The HIG requires every element people use to expose its content to VoiceOver, not just a label.

`Controls/ManagedTerminalSurface.cs:119-126` sets only the name "Interactive terminal", a HelpText, a polite live setting, and an ItemStatus of `"{Rows} rows by {Columns} columns…"` (`:2474-2498`). There's no automation peer and no text or value pattern. In the whole app only `LiveRegionTextBlock` overrides `OnCreateAutomationPeer`. The HelpText points to copy mode, which is pointer-only ("COPY MODE · scroll/select with the pointer", `TerminalRuntimePanelView.axaml:206`).

Blind users can type into the shell but can't hear output, prompts, or errors. The terminal is the product's main surface.

**Fix.** Add a peer that exposes the visible screen, or a scrollback window, as text (`IValueProvider` or a text-role peer). Announce new lines politely, with throttling. Make copy mode keyboard-driven, with arrow keys, Shift-selection, and a line cursor, and put the panel title in the name.

<a id="a-02"></a>
### A-02 · P0 · Agent replies and Markdown previews are invisible to VoiceOver

`Views/Components/SelectableMarkdownDocument.cs:21, 49` is a custom focusable `Control` with no peer. Its text sits in `_plainText` (`:51, 55`) and is never exposed. Every agent message and the Files Markdown preview render through it (`AgentWorkspaceView.axaml:424-428` and others; `FileRuntimePanelView.axaml:694`). VoiceOver users hear the container's name and none of the answer.

**Fix.** Add a peer that returns `_plainText` with a document role, or one child peer per block with heading levels. This is a small change with a large payoff.

<a id="a-03"></a>
### A-03 · P1 · The agent never announces anything, and approval cards don't take focus

`AgentWorkspaceView.axaml:202-207` deliberately keeps `LiveSetting` off the agent surface to avoid an Avalonia 12.0.5 macOS bridge crash. `AgentWorkspaceViewContractTests.cs:560` enforces that. No code calls `Focus()` on `AgentApprovalCardView`. Run started, finished, failed, and "Approval required" are all silent, so the agent can sit blocked on an approval that a screen-reader user never hears about.

**Fix.** Reuse the crash-safe `LiveRegionTextBlock` as one off-screen, never-detached status region, the same pattern `WorkspaceView.axaml:744-767` uses. Feed it discrete events, not the stream. Move focus to approval and question cards when they appear, or post `NSAccessibilityAnnouncementRequested` natively.

<a id="a-04"></a>
### A-04 · P1 · Keyboard focus is invisible on many controls

The HIG requires a clearly visible focus indicator.

`Styles/AsuraTheme.axaml:73-75` sets `FocusAdorner="{x:Null}"` on Button, ToggleButton, CheckBox, RadioButton, ComboBox, TextBox, NumericUpDown, ListBoxItem, and TabItem. Replacement `:focus-visible` styles exist for some of these and are missing for others:

| Control | Uses | Replacement focus style |
|---|---|---|
| CheckBox | 31 | None |
| ListBoxItem | 33 lists | None |
| TabItem | 22 | None |
| `WorkspaceRailTileAction` | | None |
| `Button.PrimaryButton` | 89 | Sets `BorderThickness 0` on an accent fill, so even the shared 1 px border can't show |
| TextButton, ListRow, FlyoutMenuItem, NavButton | 62, 13, 11, … | Probably overridden by a later `BorderThickness 0` |

`GitDiffView.axaml:17-27` sets the selected diff line to Transparent.

**Fix.** Apply one focus ring token (the existing `ShellFocusRingShadow`, a 3 px accent halo) with `:focus-visible` to every interactive type, including an outset ring on primary buttons. Add a headless test that tabs through each control theme and expects a visual change.

<a id="a-05"></a>
### A-05 · P1 · White text on the accent fails contrast in dark mode

`AvaloniaHostAppearanceAdapter.cs:364-373` forces white text on accent fills in every non-high-contrast dark theme. The code comment acknowledges about 2.5:1. Measured on accent fills:

| Accent | White text contrast |
|---|---|
| macOS yellow `#FFD60A` | 1.41:1 |
| Green `#32D74B` | 1.92:1 |
| Orange `#FF9F0A` | 2.06:1 |
| Asura orange `#FF8400` | 2.46:1 |
| Bronze `#B8793A` | 3.60:1 |
| Blue `#0A84FF` | 3.65:1 |

This hits primary buttons (89 uses), the active tab and its close glyph, checked segments, and the identity tiles. These are the most important labels in the UI, and none of these accents reach 4.5:1.

**Fix.** Always use the measured black-or-white choice, or darken the fill until white reaches 4.5:1. That's the same `EnsureContrast` routine already used for the accent.

<a id="a-06"></a>
### A-06 · P1 · About a quarter of form fields have no accessible name

100 of 390 TextBox, ComboBox, NumericUpDown, ToggleSwitch, and Slider instances have no `AutomationProperties.Name` or `LabeledBy`. 83 of them sit inside `controls:LabeledField`, which draws a label but never links it (`DesignSystem.axaml:1054-1083`). Examples include Host, Port, Username, and Authentication in the connection editor (`ConnectionEditorDialog.axaml:51, 88-94`). The switch inside `ToggleField` has no name either. `RuntimePanelViewContractTests.cs:1119` counts LabeledField ancestry as "labelled", so the test locks the gap in.

**Fix.** Have `LabeledField`, `SettingRow`, and `ToggleField` set `LabeledBy`, or `Name`, on their content when unset, and use the hint as HelpText. Change the test to check the runtime peer name.

<a id="a-07"></a>
### A-07 · P1 · Selected and toggled state is visual only

36 buttons carry state through `Classes.selected` or `Classes.active`, and one exposes it. In the running app, the title-bar tabs come through as `AXButton "Activate tab Browser"` with no selected state, no tab role, and no position. Docker, Git, Database, and Kubernetes section switchers have the same problem. So do the Git diff toggles "Ignore whitespace changes" and "Side-by-side comparison" (`GitDiffView.axaml:83-100`). The Settings sidebar gets it right with ItemStatus "Current page" (`AsuraTheme.axaml:440-445`).

**Fix.** Use ToggleButton for toggles, and a ListBox or tab control for tabs and section switchers. As a stopgap, set ItemStatus in the same style that sets the visual class.

<a id="a-08"></a>
### A-08 · P1 · Endless pulse animations ignore Reduce Motion

The HIG says to make motion optional and to honor Reduce Motion.

The app adds a `motion-disabled` window class (`App.axaml.cs:965-967`), but only the Kubernetes drawer reads it (`KubernetesRuntimePanelView.axaml:76-94`). These ignore it:

- Five infinite animations: `PanelAgentGlow`, `PanelAgentActivity`, and `AgentToolbarActivityPulse` (`DesignSystem.axaml:1615-1655`), `AgentActivityPulse` (`AgentWorkspaceView.axaml:80`), and `Ellipse.calling` (`WorkspaceView.axaml:47`).
- The agent glow, a 224 px accent wash that breathes over the panel content for as long as a run lasts. You can see it in the rendered agent screens.
- Rail tile width and opacity transitions, and the notification pulse.

**Fix.** Add `Window.motion-disabled` overrides that remove animations and transitions. Replace the glow with a static 2 pt accent outline, and give "calling" a static badge.

<a id="a-09"></a>
### A-09 · P1 · Rail tiles hide focusable actions and expose no state

The close and save buttons inside a rail tile are `IsVisible=True` whenever the tile can close or save. Only `:pointerover` widens the tile to show them (`DesignSystem.axaml:765-815`). Tab focus can land on an invisible "Terminate this workspace". The inner buttons are named "Open this workspace" and "Terminate this workspace" with no workspace name (`:594, 617, 638`). Current, running, and attention states are only saturation, a ring, and a dot (`WorkspaceRailTile.cs:308-316`).

**Fix.** Expand on `:focus-within` as well as on hover, put the workspace name in each button name, and set ItemStatus (for example "Current, running, needs attention").

<a id="a-10"></a>
### A-10 · P1 · Several hit targets are under the macOS minimum

The HIG gives 28 × 28 pt as the default and 20 × 20 pt as the minimum.

| Control | Size | Where |
|---|---|---|
| Tab close button | 18 × 18 with a 9 pt glyph; 18 × 18 in the running app | `RuntimeTabStripView.axaml:223-226` |
| Git ref-tree disclosure chevrons | 8 × 8 | `GitRuntimePanelView.axaml:506-509` |
| Agent "Send raw secrets" switch track | 28 × 16 | `AgentWorkspaceView.axaml:32-36` |
| Panel chrome compact buttons (up to eight per panel) | 24 × 24 | `AsuraTheme.axaml:127-130` |

**Fix.** Make tab close at least 20 × 20 (24 is better). Widen chevrons through padding, and move compact buttons toward 28 where the header allows.

<a id="a-11"></a>
### A-11 · P1 · Reduce Transparency misses the floating agent panel, veils, and blur

`App.axaml.cs:697-705` always publishes `ShellSidebarOverlayBrush` at alpha `0xCC`, and `:731-737` always publishes `ShellVeilBrush` at `0xA8`. `BlurBehindSurface` has no accessibility gate. The floating agent panel, the Quick Terminal veil, and the database and Mermaid overlays use these (`WorkspaceView.axaml:82`, `QuickTerminalWindow.axaml:40`, `DatabaseWorkspaceView.axaml:1063`, `DatabaseMermaidDiagramView.axaml:44`). People who turned transparency off still read agent text over blurred terminals. `docs/design-qa.md` check 4 covers only the docked agent.

**Fix.** Publish these brushes opaque, and hide the blur, when `WindowIsTranslucent` is false or Increase Contrast is on.

<a id="a-12"></a>
### A-12 · P1 · Selection is nearly invisible

- List selection is an 18% accent blend (`AsuraTheme.axaml:406-408`), only 1.09–1.17:1 against the panel surface.
- Sidebar rows use the same `ShellSidebarSelectionBrush` (`#0DFFFFFF`) for selected and hover (`AsuraTheme.axaml:441-453`), so only the text color tells them apart.
- Selected Git diff lines are transparent (`GitDiffView.axaml:25-27`), although that selection drives "Stage selection".
- In the rendered Database screen, the inspector shows "Row 3" while no row in the grid looks selected.

**Fix.** Selected rows get an accent fill with contrasting text while the view has focus, and a gray fill otherwise, as AppKit does. Hover gets no fill or a much fainter one. Diff-line selection gets a gutter mark.

<a id="a-13"></a>
### A-13 · P1 · Agent text ignores the text-size setting, and fixed sizes clip at large text

- `SelectableMarkdownDocument.cs:23-27` and `MarkdownPreviewView.axaml.cs:37-40` hard-code body text at 13 and headings from 23 down to 13, ignoring the `ShellFontSize*` tokens and the app's text-size override.
- The panel title has `MaxWidth="160"` (`DesignSystem.axaml:279`), and the floating agent panel is fixed at 352 × 620 (`WorkspaceView.axaml:65, 75`).
- Views contain 246 literal `Height` values.
- At 200% the design-system status chip row runs off the right edge (rendered `design-system-scale-200`).

**Fix.** Read the font tokens through `TryFindResource` and re-layout on change. Scale fixed widths with the text factor, and let chip rows wrap.

<a id="a-14"></a>
### A-14 · P2 · Accent-colored text falls below 4.5:1

`EnsureContrast` guarantees only 3:1 against the window background (`AvaloniaHostAppearanceAdapter.cs:339-343`), yet the accent is used for body-size text: TextButton.Accent, eyebrows, and the current Settings nav label. Measured ratios:

| Pair | Ratio |
|---|---|
| macOS light `#007AFF` on `#F5F5F7` | 3.69:1 |
| macOS dark | 4.26:1 |
| Bronze on the Asura light surface | 2.93:1 |

**Fix.** Add a separate `ShellAccentTextBrush` checked against 4.5:1 on every surface.

<a id="a-15"></a>
### A-15 · P2 · Muted text fails on hover and selected rows

Muted text drops below 4.5:1 on several fills, all from the palette at `AvaloniaHostAppearanceAdapter.cs:244-277`:

| Pair | Ratio |
|---|---|
| Dark `#A1A1A6` on hover `#3A3A3C` | 4.41:1 |
| Light `#6E6E73` on hover `#E5E5EA` | 4.04:1 |
| Light `#6E6E73` on the selected tint | 4.01:1 |

**Fix.** Darken light-mode muted to about `#636366`, or lighten the hover fill.

<a id="a-16"></a>
### A-16 · P2 · Attention dots aren't announced

`SignalDot.cs:18` is an empty control with no name. It marks the panel header, the tabs, the Workspaces button (named only "Workspaces"), collapsed panels, and rail tiles.

**Fix.** Fold attention into the host control's name or ItemStatus, for example "Workspaces, 1 needs attention".

<a id="a-17"></a>
### A-17 · P2 · Color carries state alone, and Differentiate Without Color isn't read

- The network globe shows Direct, connecting, and connected only by color (`WorkspaceView.axaml:84-93, 233-240`).
- The rail shows running and resting by saturation.
- Layout slot badges use white digits on orange and green, 2.46:1 and 2.28:1 in light mode (`App.axaml:183-186`).
- `MacOsHostAccessibilityPreferencesSource.cs:71-77` never reads `accessibilityDisplayShouldDifferentiateWithoutColor`.
- Status dots in the Git, Docker, and Files panel headers are hard-coded "available" green whatever the real state (`GitRuntimePanelView.axaml:240`, `DockerRuntimePanelView.axaml:244`, `FileRuntimePanelView.axaml:109`).

**Fix.** Use a distinct glyph per state. Read the preference and add shapes or text where it's on. Bind every dot to real state, with a tooltip and an accessible name.

<a id="a-18"></a>
### A-18 · P2 · Charts give no values

The HIG asks charts to label axes and units and to describe their data to VoiceOver.

`TimeSeriesChart.cs` draws unlabeled grid lines with no hover readout. Memory and network charts are auto-scaled with only "Now · auto scale" and no maximum (`StatisticsRuntimePanelView.axaml:93-145`). The accessible names are static ("CPU usage history over the last two minutes"). The agent context meter is a `ProgressBar` (`AgentWorkspaceView.axaml:1128-1131`), although a `ContextWindowDonut` exists.

**Fix.** Label the y-axis maximum with units and add a hover value. Expose min, max, latest, and trend in ItemStatus. Use a gauge for context usage.

<a id="a-19"></a>
### A-19 · P2 · Some actions are pointer-only

Changing a tab's icon is double-click only, on a non-focusable border (`RuntimeTabStripView.axaml:139-145`). The collapsed-panel preview appears only on pointer enter (`WorkspaceView.axaml:589-590`). Terminal copy mode is pointer-only ([A-01](#a-01)).

**Fix.** Add Change Tab Icon… as a command and a context-menu item, and show the preview on focus.

<a id="a-20"></a>
### A-20 · P2 · The app isn't ready for localization or right-to-left text

- There are no `.resx` files or string catalogs. About 2,966 English strings sit in XAML attributes, and 51 `StringFormat` concatenations fix the word order.
- Dates use `InvariantCulture` in Files and Processes (`FileRuntimePanelViewModel.cs:31`, `SystemMonitorRuntimePanelViewModels.cs:586`) but the current culture in Git.
- The rendered screens show "08/20/2026 21:15" in Files, "Aug 8, 2026 at 14:22" in Docker, and "2026-08-02T21:14:09Z" in Database.
- Redis shows "2,1 KB", with a culture decimal comma inside an English UI.
- `FlowDirection` is never set, so Arabic or Hebrew agent output renders left to right.

**Fix.** Format every user-facing date with the current culture and a consistent style now. Detect paragraph direction in `SelectableMarkdownDocument`. Plan the move to string resources.

<a id="a-21"></a>
### A-21 · P3 · Rail glyphs at 78% opacity

`DesignSystem.axaml:518`. A white glyph on a blue or bronze tile then reaches only 2.8:1, under the 3:1 minimum for non-text elements.

**Fix.** Use full opacity at rest.

## 4. Visual foundations

HIG pages: Color, Dark Mode, Materials, Typography, Icons, SF Symbols, App icons, Layout, Motion, Branding.

<a id="v-01"></a>
### V-01 · P1 · The running app replaces the Liquid Glass Dock icon with a flat one

The HIG says people choose Default, Dark, Clear, or Tinted icons, and the system renders that choice with Liquid Glass.

On macOS 26 and later, `App.axaml.cs:341-348` starts a 2-second `DispatcherTimer`. It calls `MacOsApplicationIcon.TryApply(accent)` (`:432-439`), which renders a flat SVG (black, or the accent color, at `rx="224"`, `MacOsApplicationIcon.cs:95-114`) and sets it with `setApplicationIconImage:`. Appearance is guessed from the undocumented `AppleIconAppearanceTheme` default (`:117-130`). While Asura runs, the Dock and ⌘Tab show a flat icon with no glass, highlight, or shadow, and Clear and Tinted modes are ignored. With the Multicolor accent, Avalonia reports blue, so the Dock icon turns blue while Finder shows the orange `Asura.icon`.

**Fix.** Delete the runtime override and its timer, and let `Assets.car` handle every appearance. For live state, use `NSDockTile` badges.

<a id="v-02"></a>
### V-02 · P1 · Content sits on glass by default

The HIG reserves Liquid Glass for the floating layer of controls and navigation: "Don't use Liquid Glass in the content layer."

`ThemePreference.DefaultFor(MacOS)` sets `hasGlassPanels: true` (`src/Asura.Core/ThemePreference.cs:101-113`). The window base is painted at alpha 0 (`App.axaml.cs:553-555`). Surface and raised brushes are 78% opaque (`:707-708`). The terminal background follows the same opacity (`TerminalRuntimePanelView.axaml:179`). Overlay cards and the floating agent panel add more translucent layers. By default, terminal output, diffs, SQL grids, and the chat transcript sit on a blurred wallpaper, legibility changes with the desktop picture, and stacked layers look muddy.

**Fix.** Default `hasGlassPanels` to false on macOS and keep content opaque. Put translucency on the chrome: title band, rail, and sidebars. Keep terminal transparency as an opt-in terminal setting.

<a id="v-03"></a>
### V-03 · P2 · The "macOS Liquid Glass" profile changes only metrics

`AvaloniaHostAppearanceAdapter.cs:609-621` changes radii and heights. Nothing in `src` or `native` uses `NSGlassEffectView`, and the chrome band is plain and transparent (`AsuraTheme.axaml:703-706`). The profile's name promises more than it delivers, and combined with [V-02](#v-02) the glass sits under content instead of over it.

**Fix.** Invert the layers: opaque content, glass on the rail, sidebar, and tab strip, with capsule-grouped toolbar controls. Or rename the profile to match what it changes.

<a id="v-04"></a>
### V-04 · P1 · In light mode the off switch is white on white

`AsuraTheme.axaml:1044-1065` draws the track with `ShellControlSurfaceBrush` and no border, and the knob as `Fill="White"`. The macOS light palette sets `ControlSurface` to `#FFFFFF` on a `#F5F5F7` card (`AvaloniaHostAppearanceAdapter.cs:264-277`). The off state reads as a blank pill at about 1.1:1.

**Fix.** Give the off track a hairline border or a gray fill (about `#E5E5EA`), and the knob a small shadow, as `NSSwitch` does.

<a id="v-05"></a>
### V-05 · P2 · Popups render in bundled Inter at 14 pt

Avalonia Fluent 12.0.5 sets `ContentControlThemeFontFamily` to Inter and `ControlContentThemeFontSize` to 14 for `PopupRoot`. Asura overrides only `Window` (`AsuraTheme.axaml:15-20`), and `.WithInterFont()` makes Inter resolvable (`Program.cs:605`). Menus, flyouts, combo lists, and tooltips can switch typeface and size relative to the window behind them.

**Fix.** Override both resources in `App.axaml` with `ShellUiFontFamily` and `ShellBaseFontSize`. Confidence is medium; check a packaged build.

<a id="v-06"></a>
### V-06 · P2 · Every small text token renders at 13 pt

macOS text styles step down from Body 13 to Callout 12, Subheadline 11, and Footnote or Caption 10. Hierarchy comes from size and weight.

`AvaloniaHostAppearanceAdapter.cs:206-220` returns `Math.Max(BaseFontSize, size × scale)`. The code comment explains this is intentional: small labels shouldn't render below the host body size. The result is that `ShellFontSize8` through `12` all render at 13. That's 521 uses across metadata, hints, and descriptions. Meanwhile `ShellPillFontSize` stays at 10 and the terminal overlays use 9 ([V-08](#v-08)), so chips end up smaller than "captions".

In the rendered screens, titles and their secondary lines are the same size with no gap between them: the Add Panel connection rows, the Settings workspaces list, and the container rows. Lists scan poorly as a result.

**Fix.** I'd revisit the floor. Map tokens to the macOS styles with a 10 pt minimum (11 for secondary text) and let weight and color carry hierarchy. Keep the text-scale multiplier. If the floor stays, delete the 8–12 tokens so the names stop lying, and add 2–4 pt between primary and secondary lines.

<a id="v-07"></a>
### V-07 · P2 · Status colors are hard-coded and inconsistent

The HIG says the same status should use the same color everywhere, with light, dark, and increased-contrast variants. Hex literals in C# give:

| Status | Values in code | Token |
|---|---|---|
| OK green | `#3FB950`, `#72B57B` | `#77D797` / `#147A3F` |
| Failure red | `#FF7A55`, `#FF8577`, `#D96B6B`, `#FF5C33` | `#FF7B72` / `#B42318` |
| Amber | `#D79B57`, `#FFB224`, `#E1A45F` | |
| Neutral | four different grays | |

In light mode these dots measure 1.8–2.6:1 on white.

**Fix.** Route every status through the success, danger, and warning tokens plus a new neutral one. View models should expose a status enum, not a hex string.

<a id="v-08"></a>
### V-08 · P2 · Terminal find and link overlays are dark-only, 9 pt, and in Inter

`ManagedTerminalSurface.cs:33-36` hard-codes `#1D1D20`, `#70492E`, `#F2F1EF`, and `#D08A4B`. Lines `2272-2283` and `2316-2320` draw with `new Typeface("Inter, SF Pro Text, …")` at 11 and 9 pt. That's below the minimum size, in the wrong face, and a dark box in light mode.

**Fix.** Use `ShellUiFontFamily`, at least 11 pt, and token brushes. Better still, make these real overlay controls (see [M-17](#m-17)).

<a id="v-09"></a>
### V-09 · P2 · The macOS palette copies system color values by hand

The HIG says not to hard-code system color values; they change between releases.

`AvaloniaHostAppearanceAdapter.cs:244-277` copies values such as `#1B1B1B`, `#F5F5F7`, `#A1A1A6`, and `#3A3A3C`, with the comment "What Finder's window is…". Selection is non-native: a 5% sidebar wash with accent text, and an 18% blend for lists.

**Fix.** On macOS profiles, resolve `windowBackgroundColor`, `labelColor`, `secondaryLabelColor`, `separatorColor`, `controlColor`, `selectedContentBackgroundColor`, and `unemphasizedSelectedContentBackgroundColor` through the existing interop. Re-resolve on `NSSystemColorsDidChangeNotification`, and keep the literals as fallbacks.

<a id="v-10"></a>
### V-10 · P2 · The accent color means too many things

The HIG on branding says to apply the accent judiciously, and the color guidance says not to use one color for different meanings.

The accent drives all of these:

- The Notice tone (`DesignSystem.axaml:76-79, 954-958`), so with an orange accent, Notice looks like Warning.
- Eyebrow text and nav labels.
- Empty-state and callout glyphs.
- The active panel border and the agent glow.
- Focus, selection, switches, and sliders.
- Chart lines (`StatisticsRuntimePanelView.axaml:78, 116`).

The rendered Docker screen has accent outlines on all three stack headers, plus the selected row, the active tab, and an accent Shell button. Six accent marks with no single focal point. Redis shows three accent buttons at once (Save field, Add fields, and the Browser segment) beside an accent selected row and an accent tab.

**Fix.** Keep the accent for interactive and selected state only. Give Notice its own info tone, use secondary label color for static emphasis, and give charts a dedicated palette.

<a id="v-11"></a>
### V-11 · P2 · Workspace colors override the user's accent, and Multicolor isn't detected

The HIG says an app accent applies only when the user's system accent is Multicolor.

`App.axaml.cs:470-477` replaces the accent with the workspace color. `ThemePreference.cs:316-321` falls back to bronze only when the host reports no accent, which on macOS never happens, because Avalonia reports blue for Multicolor. The code holds three brand oranges: `#FF8400` (`App.axaml:42, 87`), `#B8793A` (`ThemePreference.cs:92`), and P3 `0.969, 0.510, 0.106` (`assets/macos/Asura.icon/icon.json`).

**Fix.** Treat a missing `AppleAccentColor` default as Multicolor and use the brand accent then. Confine workspace colors to identity marks (rail tile, tab chip). Pick one brand orange.

<a id="v-12"></a>
### V-12 · P2 · Icons come from Microsoft's Fluent set at 17 sizes

The HIG prefers SF Symbols, or a set that matches them in weight and optical size, sized relative to text.

All 346 `SymbolIcon`s come from `FluentIcons.Avalonia` (`Directory.Packages.props:29`). 285 use a literal `FontSize`, across 17 values from 9 to 40; 58 use tokens. At 200% text, most icons stay small. In the rendered Files panel, every file (`.csv`, `.dylib`, `.md`, `.zip`, `.json`) gets the same `</>` glyph.

**Fix.** Add an icon size ramp tied to text tokens. On macOS, render SF Symbols through `NSImage(systemSymbolName:)` and keep Fluent elsewhere. For local files, use the `NSWorkspace` file-type icon.

<a id="v-13"></a>
### V-13 · P2 · The window material ignores focus

`Views/MacOsWindowMaterial.cs:29-30, 75` pins `NSVisualEffectStateActive`. Inactive Asura windows don't dim like other Mac windows, so with several open it's harder to tell which one has focus.

**Fix.** Use `followsWindowActiveState`.

<a id="v-14"></a>
### V-14 · P2 · The fallback `.icns` has no margin

The extracted `icon_512x512@2x.png` is opaque from x=0 and has no shadow (`scripts/build-macos-icon.sh:46-55`). `Info.plist.template:13-16, 29-30` declares `CFBundleIconFile` with `LSMinimumSystemVersion 13.0`. Development builds use it too (`run-macos-development.sh:11`). On macOS 13–15, unless `Assets.car` carries back-deployed renditions, the icon looks oversized next to others.

**Fix.** Export the fallback on the pre-26 grid (an 824 px body on a 1024 canvas, with a shadow), or confirm actool emits legacy renditions.

<a id="v-15"></a>
### V-15 · P3 · Heading sizes are ad hoc

Headings use 14, 15, 17, 18, 20, 22, 24, and 25 pt, and 174 of 192 weight declarations are SemiBold. Settings page titles are 25 pt SemiBold; Large Title is 26 Regular.

**Fix.** Define role tokens (Large Title 26, Title 1 22, Title 2 17, Title 3 15, Headline 13 bold), and make form row labels Regular.

<a id="v-16"></a>
### V-16 · P3 · Monospace faces are inconsistent

Data surfaces ask for `"SF Mono, Menlo, …"` (`App.axaml.cs:820-821`). SF Mono isn't resolvable by name on a stock Mac; this machine has Apple's developer fonts installed, which hides that. Docker logs and Mermaid use JetBrains Mono.

**Fix.** Use one `ShellDataFontFamily`: the terminal profile font, or `NSFont.monospacedSystemFont`.

<a id="v-17"></a>
### V-17 · P3 · Segmented control corners aren't concentric

The inner radius is `control − 2` (`AvaloniaHostAppearanceAdapter.cs:448`), but the segment sits inside a 1 pt border plus an inset of about 6 pt. On the Liquid Glass profile, the outer radius is 10, so a concentric inner radius would be about 4; it draws 8.

**Fix.** Compute inner radius as outer radius minus inset minus border, or apply `Concentric.IsEnabled`.

<a id="v-18"></a>
### V-18 · P3 · Theme previews use the old palette

`AppearanceSettingsPageView.axaml:38-41, 56-59, 75-78` hard-codes `#111111` and `#F2F3F0`, while the macOS profile renders `#1B1B1B` and `#FFFFFF`.

**Fix.** Bind the swatches to the mapper's output.

<a id="v-19"></a>
### V-19 · P3 · Paths truncate at the end

All 106 `TextTrimming` uses are `CharacterEllipsis`. Finder truncates in the middle so the file name stays visible.

**Fix.** Use path or middle truncation for paths and references.

<a id="v-20"></a>
### V-20 · P3 · Asura has its own Light/Dark override

`ThemePreference.cs:5-10`. The HIG discourages app-level appearance settings, since they create more work for people. The default is System, which is right. Consider moving the explicit Light and Dark choices under an advanced section.

## 5. Components and patterns

HIG pages: Buttons, Toggles, Pop-up and pull-down buttons, Segmented controls, Text fields, Search fields, Lists and tables, Sidebars, Tab views, Progress indicators, plus the Feedback, Entering data, Searching, Drag and drop, Notifications, Settings, and Charting data patterns.

<a id="c-01"></a>
### C-01 · P1 · Most confirmations are styled as deletions

The HIG reserves the destructive style for actions that destroy data, and says not to alert for actions people can undo.

`ConfirmationDialog.axaml.cs:45` defaults `Intent` to `Destructive`, which adds a trash glyph, a red button, and no Return key. Of 33 confirmations, 2 opt out. So these all look like deletions: Merge, Push, Trust repository, Authorize live target, and Update stored password (`Confirmations.cs:116-149, 175-191, 272-286`; `AvaloniaNetworkPasswordPrompt.cs:18-27`). Saved-screen delete warns that the screen "will be permanently removed" and then offers Undo (`Confirmations.cs:34`, `MainWindow.axaml.cs:1708-1727`). When routine actions look dangerous, people stop reading the warnings.

**Fix.** Add a `Confirm` intent: app icon, accent default button, Return works. Remove the default value so each call site chooses. Where Undo exists, drop the confirmation and extend that pattern to connections and workspaces.

<a id="c-02"></a>
### C-02 · P1 · Validation errors appear far from the field

The HIG says to show an error near its cause, say how to fix it, and keep what the person typed.

`Controls/LabeledField.cs` has no error slot. Git preferences are validated after `GitWorkflowDialog` closes (`GitRuntimePanelView.Preferences.cs:30-35`). The error then opens `GitOutputDialog`, an 850 × 600 read-only log window, and the input is lost. There are 14 such call sites, including "Invalid line range" and "Invalid executable". `ConnectionEditorDialog.axaml:499-502` stacks four error lists below the cards of a 780 pt scrolling dialog.

**Fix.** Add `Error` and `HasError` to `LabeledField`. Validate inside the dialog and keep it open. Scroll to and focus the first bad field. Keep `GitOutputDialog` for real command output.

<a id="c-03"></a>
### C-03 · P1 · Search fields don't look or behave like search fields

The HIG's search field has a magnifying glass, a clear button, and a placeholder.

- 4 of 21 search and filter fields show a magnifier, and none has a clear button.
- Placeholders split 10 with an ellipsis ("Filter resources…") and 11 without ("Search files", "Filter refs"), and use three verbs: Search, Filter, and Find.
- "Search files" is actually a filter; its accessible name is "Filter file items".
- "Search settings ⌘K" in the Settings sidebar (`SettingsView.axaml:46-59`) opens the global palette. The palette indexes connections, screens, workspaces, sessions, and commands, not settings.
- Docker log search runs only from an accent Search button.

**Fix.** Make one `SearchField` theme with a leading glyph, `clearButton`, and Esc to clear. Choose one verb per meaning. Either index settings rows or make the field filter the Settings sidebar. Docker search should run on Return.

<a id="c-04"></a>
### C-04 · P2 · Switches appear where checkboxes belong

The HIG on toggles says switches are for prominent settings in the window body. Lists, hierarchies, and minor options use checkboxes, and a checkbox should never be replaced by a switch.

There are 34 `ToggleSwitch`, 31 `CheckBox`, and 8 `ToggleField`. Switches show up in places the HIG reserves for checkboxes:

| Where | Location |
|---|---|
| Every permission row in File Access | `FileAccessControlDialog.axaml:67` |
| A log toolbar ("Follow") | `DockerRuntimePanelView.axaml:553` |
| Preview modes in the Files inspector ("Show raw") | `FileRuntimePanelView.axaml:586, 619` |
| The agent composer ("Send raw secrets") | `AgentWorkspaceView.axaml:1081` |
| A flyout menu | `WorkspaceView.axaml:178-183` |
| Next to six checkboxes | `ConnectionEditorDialog.axaml:178` |

Git forms render 13 booleans as Yes/No pop-ups (`GitRuntimePanelView.Preferences.cs:19-24, 59`, and others).

**Fix.** Use checkboxes for rows and minor options, a checkable menu item in menus, and a toggle button for "Follow". Render `bool` as a checkbox in `GitWorkflowDialog`.

<a id="c-05"></a>
### C-05 · P1 · Drag and drop barely works

Mac users expect to drop a Finder file on a terminal and get its shell-escaped path. No `AllowDrop` exists on the terminal, the agent composer, or the SQL editor. Files can't be dragged out of the Files panel (no `DoDragDrop`). Tab dragging doesn't cancel on Esc (`RuntimeTabDragController.cs`), although rail and panel drags do.

**Fix.** Terminal: insert escaped paths on drop. Agent: accept file and image drops as attachments with a highlighted target. Files panel: drag out to Finder. Tab drag: handle Esc.

<a id="c-06"></a>
### C-06 · P2 · Several primary buttons compete in one view

The HIG gives the primary style to the one most likely action. Settings › Security has three accent buttons ("Turn on", "Store credential" twice, `SettingsView.axaml:981, 1116, 1148`), and Settings › AI also has three. Docker has "Open shell" and "Search". "Reset to retry", which clears the agent session, is primary (`AgentWorkspaceView.axaml:883`). See also the Redis screen in [V-10](#v-10).

**Fix.** One primary button per section, and make "Reset to retry" secondary with a name that says what it clears.

<a id="c-07"></a>
### C-07 · P2 · Git forms all say "Apply", including force push

`GitWorkflowDialog.axaml:6` and its default `action = "Apply"` mean that Clone, Open pull request, Save stash, Compare revisions, and Review force push all confirm with an accent "Apply" (`RepositoryTools.cs:24-31, 98`).

**Fix.** Pass a verb per form ("Clone", "Open Pull Request", "Force Push"). Make force push destructive with no default button.

<a id="c-08"></a>
### C-08 · P2 · Destructive buttons are styled as destructive about one time in three

10 buttons labeled Delete, Discard, Remove, Reset, or Clear use the destructive style, and 23 don't. The 23 include "Clear cookies", "Clear history", "Reset profile", "Remove account", "Discard selection…", and "Delete connection". "Clear" is destructive at `SettingsView.axaml:1080` and plain at `:443`.

**Fix.** Add a design-QA or analyzer rule that requires an explicit role class for these verbs.

<a id="c-09"></a>
### C-09 · P2 · Ellipsis use on buttons is uneven

The HIG puts "…" on a button that opens another window or view. The real "…" character is used everywhere, never "...", which is good. But "Edit" is plain in Settings (`SettingsView.axaml:263, 288, 322, 510, 1400`) and "Edit…" in Saved Connections. The same is true of "Add provider", "Add connection", "Manage", "Manage all" (which elsewhere is "Manage connections…"), and "New workspace".

**Fix.** Add "…" to every button that opens a window or editor, and use one label per action.

<a id="c-10"></a>
### C-10 · P2 · Notifications show while Asura is in front, and Settings can't control them

The HIG says to handle notifications in the app's own UI while it's frontmost, let people control them, and title-case notification titles.

- `MacOsUserNotificationCenter.Interop.cs:12` always passes sound, alert, list, and banner as foreground options.
- Suppression applies only when the exact source panel is visible, so an agent run banners over Asura itself when the agent panel is hidden.
- Settings has no notification controls; `docs/notifications.md` lists this as gap 4.
- Titles are sentence case ("File transfer completed"). "Background work finished." ends with a period. "Agent finished" has only the workspace name as its body.

**Fix.** While Asura is active, pass no presentation options and rely on in-app attention marks. Add Settings › Notifications with per-source switches and authorization status. Put the outcome in the body, and use title case for titles.

<a id="c-11"></a>
### C-11 · P1 · The tab strip overlaps and hides tabs when it overflows

The HIG says tabs must stay reachable when they overflow, show a close button on hover, and have a context menu.

In the running 0.1.81 app, with seven tabs in a 1280 pt window, the selected "Browser" tab is drawn over the second "Kubernetes" tab and covers its close button. A "hetzner" tab is completely hidden behind it: it is present in the accessibility tree at x=932 but not visible on screen. Four tabs share the title "Browser" and can't be told apart.

In code:

- Tabs are a fixed 158 pt wide (`RuntimeTabStripView.axaml:26`).
- An 18 pt close button is always visible (`:222-233`).
- The title tooltip is "Double-click to rename" (`:174`), so a truncated name can't be read.
- Overflow is a hidden scrollbar plus a fade (`:79`), with no overflow menu.
- There's no context menu and no middle-click close (`RuntimeTabStripView.axaml.cs:211-222`).

**Fix.** Fix the overflow layout so selected tabs never cover neighbors: compress tabs to a minimum width, then scroll, with an overflow pull-down. Show the close button on hover and on the active tab. Use the full title as the tooltip. Title browser tabs with the page title or host. Add the context menu from [M-15](#m-15).

<a id="c-12"></a>
### C-12 · P2 · Data tables don't look like Mac tables

`AsuraTheme.axaml:859-891` gives every DataGrid full grid lines, no alternating rows, a hover highlight, and the faint 18% selection. All 7 grids allow only single selection. The process monitor is hand-built from Buttons and TextBlocks (`ProcessMonitorRuntimePanelView.axaml:123-190`).

**Fix.** Use horizontal or no grid lines with alternating rows, selection as in [A-12](#a-12), no hover fill, and extended selection for SQL results, Kubernetes resources, and processes.

<a id="c-13"></a>
### C-13 · P2 · Four ways to switch views, and one has nine segments

The HIG keeps segmented controls to a few stable segments with one content type. Asura uses push buttons with a `selected` class (Docker, Git), ToggleButton segments (Database, Redis), and `TabControl.Segmented` (Kubernetes). The Kubernetes inspector shows up to nine conditional segments that appear and disappear with the selection (`KubernetesRuntimePanelView.axaml:800-1157`).

**Fix.** Use one `SegmentedControl` for 2–5 stable views. Use a pop-up button or a real tab view above that, and disable segments rather than hiding them.

<a id="c-14"></a>
### C-14 · P3 · Key information lives only in tooltips, and buttons use the hand cursor

The Keychain checkbox (`ConnectionEditorDialog.axaml:420-426`) and "Send raw secrets" (`AgentWorkspaceView.axaml:1085-1088`) explain themselves only in tooltips. Rail and chooser tiles set `Cursor="Hand"` (`DesignSystem.axaml:497, 1245`); Mac buttons use the arrow. `ToggleField` repeats "On" or "Off" under the label.

**Fix.** Show the explanation as visible help text, use the arrow cursor, and drop the On/Off text.

## 6. Writing

HIG page: Writing, plus the label rules on the Buttons, Menus, Alerts, and Notifications pages.

<a id="t-01"></a>
### T-01 · P1 · Buttons and menus use sentence case

On macOS, buttons, menu items, window titles, tab titles, and segment labels use title case. Labels, checkboxes, descriptions, and alert body text use sentence case.

There are 169 sentence-case button labels against about 5 in title case. In-app menu items are 105 sentence case and 17 title case, while all 14 native menu items are title case. The same command appears both ways: "New Tab" in the menu (`MainWindow.axaml:29`) and "New tab" in the palette and keybindings (`BuiltInCommands.cs:57`). Close Tab, Next Tab, and Previous Tab follow the same pattern. The Git panel uses "New Branch…" and "New branch…". One database menu has "Copy Cell Value" next to "Export current page…".

**Fix.** Keep one canonical title per command in the registry, and apply macOS casing to buttons, menus, segments, tabs, and window titles. If other platforms want sentence case, apply a per-platform transform.

<a id="t-02"></a>
### T-02 · P2 · Keychain goes by six names

The HIG asks for consistent terms and the platform's own words. The secret store is called:

| Term | Uses | Example |
|---|---|---|
| "OS vault" or "operating-system vault" | about 52 | `AiProviderProfileEditorViewModel.cs:160` |
| "system credential store" | 16 | `GitCredentialDialog.axaml:16` |
| "system keychain" | 8 | `SettingsView.axaml:1425` |
| "OS keychain" | 7 | `ConnectionEditorDialog.axaml:425` |
| "OS keystore" | 1 | `SettingsView.axaml:949` |
| "macOS Keychain" | 2 | `ConnectionEditorDialog.axaml:424` |

For a product whose pitch includes "credentials stay in your OS vault", this inconsistency undermines trust.

**Fix.** Route every mention through one platform term: Keychain on macOS, Credential Manager on Windows, Secret Service on Linux.

<a id="t-03"></a>
### T-03 · P2 · Implementation terms leak into the UI

| Where | Text |
|---|---|
| Add a panel subtitle | "Choose an adapter supported by this desktop milestone." (`NewPanelChooserView.axaml:21`) |
| Panel description | "Local PTY" |
| 88 strings | "definition(s)", for example "New definition" |
| Agent approval card (rendered) | the raw panel ID "01a0df93caed736baaa598b6c213744d" |
| Agent run card (rendered) | "terminal.run", "Mutation", "Run a bounded command" |
| Command palette | command IDs plus "key=value" arguments under an all-caps "COMMAND · Panels" group (`MainWindowViewModel.cs:9685-9695`) |
| Command palette footer | "Unavailable results stay visible but cannot be opened." |
| Connection editor footer | "Opening revalidates runtime, credentials, and platform support." |
| Connection editor callout | "Save is allowed without a test; opening validates the profile again." |
| Docker | "The same command boundary works locally and through saved SSH connections." |
| About | "native .NET in process…", "…release-license work still open." |
| Raw enum shown as a choice | "FastForward" |
| 28 call sites | raw `{exception.Message}` shown to people |

**Fix.** Rewrite in user terms ("Choose a panel type", "Fast-forward only", "Run command in production-api"). Hide IDs behind a details disclosure. Map exceptions to curated messages with a next step.

<a id="t-04"></a>
### T-04 · P2 · British and American spelling are mixed

"cancelled" appears 64 times and "canceled" 3 times. "colour" appears in 16 of 22 strings. "licences" and "recognised" also appear. One Appearance subtitle pairs "Customize" with "colour scheme" in the same sentence (`AppearanceSettingsPageView.axaml:16`). Apple writes US English.

**Fix.** Pick en-US and add a spelling lint for string sources.

<a id="t-05"></a>
### T-05 · P2 · Panel types have different names on different screens

The Launcher says "New terminal", "File Viewer", and "Process monitor" (`LauncherView.axaml:112-120`), mixing cases in one grid. The Add Panel chooser says "Terminal", "Files", and "Processes" (`NewPanelChooserView.axaml:28-36`). Docker says "Stats" for what the panel calls "Statistics". The palette groups "Create · database", "Create · Docker", "Create · files", and "COMMAND · Panels", each cased differently.

**Fix.** Use one noun per panel type and one description, defined once and reused.

<a id="t-06"></a>
### T-06 · P2 · Core concepts drift

- Screen, layout, definition, and tab overlap: "Saved screens" sits beside a "Layouts" button, and "New definition" beside "New workspace".
- The Settings nav says "Workspaces & screens" and the page heading says "Workspaces & saved screens".
- "Preferences" and "Settings" are both used, as are "Keybindings" and "shortcuts".
- "directory" appears 33 times and "folder" 34; macOS says folder.
- Delete, Remove, and Terminate are used for similar actions.

**Fix.** Keep a glossary in `docs/` and lint against it.

<a id="t-07"></a>
### T-07 · P3 · Window titles are sentence case and don't match their headers

28 window titles are sentence case ("Create tag"). The file transfer window says "Queue transfer" and its header says "Queue file transfer" (`FileTransferDialog.axaml:14, 16`). 12 dialogs set no title in XAML.

**Fix.** Use title case, matching the dialog header.

<a id="t-08"></a>
### T-08 · P3 · Copy leftovers

- "OK" as an alert action (`AvaloniaNetworkPasswordPrompt.cs:39`), and Yes/No pop-up values.
- "Toggle Agent Panel".
- "5 item(s)" in Files.
- `ToLowerInvariant()` on labels produces "Enter repository url." (`GitWorkflowDialog.axaml.cs:53`).
- "Quick Look Editor" reuses Apple's feature name for a database cell editor (`DatabaseWorkspaceView.axaml:688`).
- "+ Terminal" style labels.

**Fix.** Use specific verbs, Show/Hide titles, real plurals ("1 item", "5 items"), per-field messages, "Open Cell Editor", and "Add Terminal".

## 7. Observations from specific screens

These come from the rendered screens and the running app, and aren't covered above.

| ID | Sev | Screen | Observation | Suggested change |
|---|---|---|---|---|
| S-01 | P2 | Agent panel | The "File transfers" toast sits on top of the agent composer, covering the send button and the model picker ("Ask approval", "GPT-5.6 Terra" are clipped). | Stack toasts above the composer, or inset them from the agent panel's frame. |
| S-02 | P2 | Add a panel | The Browser tile is disabled with no reason given. | Show why ("Browser runtime not installed") and how to fix it. |
| S-03 | P2 | Git diff | The toolbar has "Previous", "Next", "Previous change", "Next change". The first pair's object is unclear, and the toolbar wraps to two rows at 1440 pt. | "Previous File"/"Next File" and "Previous Change"/"Next Change" as two compact segmented pairs with ↑↓ glyphs, and checkboxes in a View pop-up. |
| S-04 | P2 | Keybindings | Four rows are all named "Focus panel" (left, right, up, down are visible only in the shortcut column). | "Focus Panel Left", "…Right", "…Up", "…Down". |
| S-05 | P3 | Database | The filter row starts with an unlabeled accent checkbox. | Label it ("Filter") or replace it with a filter toggle button. |
| S-06 | P3 | Database | "200 / 6" next to the pager reads as page 200 of 6. | "Rows per page: 200 · 6 rows". |
| S-07 | P3 | Database | One toolbar mixes a filled "Add row", a plain-text "Delete", and filled "Revert"/"Save". The status bar uses ":" as a separator where every other panel uses "·". | One button style per toolbar, and "·" everywhere. |
| S-08 | P3 | Docker | "Shell" is both a detail tab and an accent button beside it. | Keep one. |
| S-09 | P3 | Files | The "more" button is a vertical ⋮, an Android/Material pattern. | Use a horizontal ellipsis in a circle, like `ellipsis.circle`. |
| S-10 | P3 | Redis | "Connect" and "Disconnect" are both enabled while connected. | Show one button that changes with state. |
| S-11 | P3 | Keybindings | Every row of a read-only preset shows three disabled buttons (Record, Unbind, Reset). | Hide row actions for read-only presets; show them on hover or selection otherwise. |
| S-12 | P3 | Settings › Workspaces | A red trash icon sits on every row. | Use the macOS list pattern (select, then "–" below the list, or a context menu) and keep Edit… as the row action. |
| S-13 | P3 | Connection editor | "Keep alive" shows the label, an "Off" line, and a switch floating mid-row between two columns. | Use a checkbox in the label column ("Keep connection alive") and indent the two numeric fields under it. |
| S-14 | P3 | Statistics | The "Statistics unavailable" card floats over the middle of the chart grid, covering parts of two charts. | Replace the grid with the empty state, or attach the error to the panel header. |
| S-15 | P3 | Command palette | Every row has its own "Open" button, and the selected row is accent-filled, so the list has 9 buttons and no clear focus. | Drop per-row buttons; Return activates the selection, and the footer already says so. |

## What already works

Credit where it's due. These match the HIG and should be kept as they are.

- **Window chrome.** Traffic lights are native. The title band is draggable through `ElementRole="TitleBar"`, and its margins track the real traffic-light positions, including in full screen (`MainWindow.axaml.cs:2094-2138`, `MacOsWindowTitleBar.cs`).
- **Design tokens.** Every text size in views comes from a token, and only 16 hex literals remain in view XAML. The accent follows the host with live updates, and custom accents are checked for contrast.
- **Light, dark, and high contrast.** All three palettes exist per profile, with a high-contrast accent held to 4.5:1. The window material goes opaque under Reduce Transparency and Increase Contrast. Quick Terminal and the Kubernetes drawers honor Reduce Motion.
- **App icon.** A layered Icon Composer file with dark and tinted variants, compiled with actool 26.
- **SF Pro isn't bundled.** The repo-root `SF-Pro-Text-*.otf` files are gitignored and unreferenced, and macOS profiles use the system font. No Light, Thin, or Ultralight weights anywhere.
- **Accessible names on icon buttons.** All 167 icon-only buttons have names. The running app's title bar exposes "Open a new tab", "Activate tab …", and "Close tab …". Error callouts are assertive live regions with an icon and title.
- **Destructive confirmations.** They never default, Esc cancels, the verbs are specific ("Delete branch", "Discard changes"), and the copy says plainly when something can't be undone.
- **Tone.** No exclamation marks, no "Oops", no "we", and the real "…" character everywhere.
- **Native file panels.** Export, import, upload, download, and Git output all use the system Open and Save panels.
- **Progress.** Determinate progress for transfers, updates, and agent steps. Loading states appear inside panels, not as blocking spinners.
- **Launch and onboarding.** No splash screen. Onboarding is a dismissible card in the launcher. A second launch activates the existing instance.
- **Drag feedback.** Ghost previews, insertion bars, and Esc-to-cancel on rail and panel drags.
- **Dirty-state protection.** Layout, workspace, keybinding, database, and Kubernetes edits ask before discarding.

## Suggested order of work

**Release blockers** (one focused pass, mostly shared code):

1. [M-07](#m-07) and [M-08](#m-08): Quick Terminal hotkey default and Escape capture.
2. [M-09](#m-09) and [M-10](#m-10): macOS keymap as default; drop the ⇧⌘3/4/5 bindings.
3. [M-01](#m-01), [M-03](#m-03), [M-04](#m-04), and [M-06](#m-06): fill the standard menus.
4. [A-01](#a-01) and [A-02](#a-02): automation peers for the terminal and Markdown documents.
5. [W-03](#w-03): no default button on SSH key replacement.
6. [C-11](#c-11): tab strip overflow.

**Next cycle:**

- Dialog keyboard contract and confirmation intents: [W-02](#w-02), [C-01](#c-01), [C-07](#c-07), [C-08](#c-08).
- Focus, contrast, and selection: [A-04](#a-04), [A-05](#a-05), [A-12](#a-12), [V-04](#v-04).
- Glass and motion defaults: [V-02](#v-02), [A-08](#a-08), [A-11](#a-11), [V-01](#v-01).
- Settings window and window restoration: [W-01](#w-01), [W-05](#w-05), [W-07](#w-07), [W-13](#w-13).
- Agent announcements and form labels: [A-03](#a-03), [A-06](#a-06), [A-07](#a-07).

**Later:**

- Writing pass with a glossary and casing rules: T-01 to T-08.
- Command registry driving the menu bar: [M-02](#m-02).
- Component consolidation: search field, segmented control, tables, switches. [C-03](#c-03), [C-13](#c-13), [C-12](#c-12), [C-04](#c-04).
- Native colors and SF Symbols: [V-09](#v-09), [V-12](#v-12).
- Localization: [A-20](#a-20).

## Open questions

These need someone at a Mac with the packaged build. They can raise or lower some severities above.

1. Does AppKit inject Start Dictation, Emoji & Symbols, or AutoFill into the empty Edit menu? Do ⌃⌘F and ⌘M do anything today?
2. Does registering ⌘` succeed and override window cycling, or does the system keep it, so the default hotkey silently does nothing?
3. Do ⌘C, ⌘V, and ⌘A work inside the CEF browser without Edit menu key equivalents?
4. With a dialog, Quick Terminal, or a floating panel key, does the menu bar keep File, Edit, View, and Window? The background read in this review suggests it doesn't.
5. On ⌘Q with two windows that both have running sessions, do two close alerts appear?
6. Does `TextButton:focus-visible` survive the later `BorderThickness 0` in Avalonia 12.0.5?
7. Does the Avalonia DataGrid expose cells, headers, and sort state to VoiceOver?
8. How does Quick Terminal behave over another app's full-screen Space?
9. Does `FontFamily.Default` resolve to SF Pro in a packaged build, and do popups render in Inter?
10. Does actool emit pre-26 renditions into `Assets.car` for macOS 13–15?

## Appendix A. Contrast measurements

WCAG 2 ratios on opaque surfaces. Glass makes real values vary with the wallpaper. macOS rows use the shipping palette at `AvaloniaHostAppearanceAdapter.cs:244-277`, Asura rows `:278-305`.

| Pair | Theme | Foreground / background | Ratio | Result |
|---|---|---|---|---|
| Muted on background | macOS dark | `#A1A1A6` / `#1B1B1B` | 6.70 | Pass |
| Muted on hover row | macOS dark | `#A1A1A6` / `#3A3A3C` | 4.41 | Fail |
| Muted on surface | macOS light | `#6E6E73` / `#F5F5F7` | 4.66 | Pass, barely |
| Muted on hover row | macOS light | `#6E6E73` / `#E5E5EA` | 4.04 | Fail |
| Text on accent | macOS dark, blue | `#FFFFFF` / `#0A84FF` | 3.65 | Fail |
| Text on accent | macOS dark, yellow | `#FFFFFF` / `#FFD60A` | 1.41 | Fail |
| Text on accent | Asura dark | `#FFFFFF` / `#B8793A` | 3.60 | Fail |
| Text on accent | macOS light | `#000000` / `#007AFF` | 5.23 | Pass |
| Accent as text | macOS dark | `#0A84FF` / `#242424` | 4.26 | Fail |
| Accent as text | macOS light | `#007AFF` / `#F5F5F7` | 3.69 | Fail |
| Accent as text | Asura light | `#B8793A` / `#E7E8E5` | 2.93 | Fail |
| Danger on surface | macOS dark | `#FF7B72` / `#242424` | 6.16 | Pass |
| Warning on warning tint | macOS light | `#8A4B08` / `#FFF0D6` | 6.05 | Pass |
| Success on surface | Asura light | `#147A3F` / `#E7E8E5` | 4.39 | Fail |
| Selected row fill vs surface | macOS dark | `#182E44` / `#242424` | 1.12 | Fail (3:1 for UI) |
| Selected row fill vs surface | macOS light | `#D1E7FF` / `#F5F5F7` | 1.16 | Fail |
| Off switch track vs card | macOS light | `#FFFFFF` / `#F5F5F7` | 1.09 | Fail |
| Slot badge digit on orange or green | light | `#FFFFFF` / `#FF8400`, `#22C55E` | 2.46, 2.28 | Fail |
| Rail glyph at 78% opacity | any | white over `#0A84FF` | 2.78 | Fail |
| High-contrast muted | HC dark | `#E6E6E6` / `#000000` | about 17 | Pass |

## Appendix B. Type ramp against macOS text styles

macOS profile at 100% text scale.

| Asura token or use | Renders as | Closest macOS style | Verdict |
|---|---|---|---|
| `ShellFontSize8`–`12` (hints, metadata, descriptions) | 13 Regular or Medium, clamped | Callout 12 down to Caption 10 | Hierarchy collapsed |
| `ShellPillFontSize` (chips) | 10 SemiBold | Caption 2 | Size fine; inconsistent with the clamp |
| Terminal overlay text | 9 and 11, Inter | Footnote 10 | Below minimum, wrong face |
| Popups through Fluent `PopupRoot` | 14, Inter | Body 13 | Wrong size and face |
| `ShellBaseFontSize` (body) | 13 Regular | Body 13 | Matches |
| Row and field labels | 13 SemiBold | Body | Mac form labels are Regular |
| `ShellFontSize13` Bold | 13 Bold | Headline 13 | Matches |
| `ShellFontSize14` (section headers) | 14 SemiBold | between Body and Title 3 | Off the ramp |
| `ShellFontSize15` (dialog titles) | 15 SemiBold | Title 3 | Matches |
| `ShellFontSize17` | 17 SemiBold | Title 2 | Matches |
| `ShellFontSize18`, `20` | 18 and 20 SemiBold | between Title 2 and Title 1 | Off the ramp |
| `ShellFontSize22` | 22 | Title 1 | Matches |
| `ShellFontSize25` (Settings page titles) | 25 SemiBold | Large Title 26 Regular | 1 pt and one weight off |

## Appendix C. Tooling note

`tools/Asura.DesignQa` no longer completes a full run. The Kubernetes routes render a Browser panel instead of the sample cluster, and `workspace-kubernetes-manifest` throws `Sequence contains no matching element` in `SelectKubernetesInspectorTab` (`tools/Asura.DesignQa/Program.cs:3018`). That aborts the run, so every later route (Docker, Git, Database, Redis, the dialogs, and the design-system gallery) is skipped unless requested by name. This review captured those routes individually.
