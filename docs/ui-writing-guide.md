# UI Writing Guide

This guide covers every string a person can see or hear in Asura: labels, buttons, menus, window titles, tooltips, accessibility names, notifications, status lines, and error messages. It follows Apple's Human Interface Guidelines (Writing, Buttons, Menus, Alerts, Notifications) on every platform. Where Windows or Linux use different words for a platform feature, the helper in [Platform terms](#platform-terms) chooses them.

`scripts/check-ui-copy.py` checks most of these rules. [Checking copy](#checking-copy) describes how to run it.

## Language

Write US English (en-US).

| Write | Not |
|---|---|
| canceled, canceling (but cancellation) | cancelled, cancelling |
| color | colour |
| license (noun and verb) | licence |
| behavior | behaviour |
| favorite | favourite |
| center | centre |
| gray | grey |
| analyze, analyzed | analyse, analysed |
| recognize, organize, initialize, customize, authorize, optimize, serialize, synchronize, normalize, minimize, maximize, summarize | the same words with -ise, -ised, -ising, -isation |
| catalog, dialog | catalogue, dialogue |
| labeled, modeled, traveled, signaled | labelled, modelled, travelled, signalled |
| acknowledgment, judgment | acknowledgement, judgement |
| among, while | amongst, whilst |

## Capitalization

### Title style

Capitalize every word except:

- articles: a, an, the
- coordinating conjunctions: and, but, or, nor, for, so, yet
- prepositions of four or fewer letters: as, at, by, down, for, from, in, into, like, near, of, off, on, onto, out, over, past, per, to, up, upon, via, with

Capitalize those words too when they are the first or last word, or the first word after a colon. Capitalize a preposition that belongs to a verb ("Log In", "Zoom Out", "Set Up"). Capitalize both parts of a hyphenated compound ("Built-In", "Read-Only", "Pop-Up"), except after a prefix such as re- or pre- ("Re-enter Password").

Keep names as their owners write them: macOS, iOS, kubectl, GitHub, PostgreSQL. Text inside quotation marks, such as a branch or file name, keeps its own case.

| Right | Wrong |
|---|---|
| New Tab | New tab |
| Check for Updates | Check For Updates |
| Sign In to Git | Sign in to Git |
| Add to the Left | Add To The Left |
| Open in New Window | Open In New Window |
| Save As… | Save as… |

### Which elements use which style

| Title style | Sentence style |
|---|---|
| Button labels | Labels next to controls |
| Menu items, context menu items, and Dock menu items | Checkbox and radio button text |
| Segment labels in a segmented control | Descriptions, help text, and footnotes |
| Tab titles and tab item headers | Placeholder text |
| Window, sheet, and dialog titles | Alert body text |
| Notification titles | Notification body text |
| Command palette command names | Tooltips |
| Settings pane names in the sidebar | Status lines and error messages |
| | Group and section headings inside a pane |
| | Accessibility names and help text, which repeat the visible text they describe |

Alert headings are title style when they are a short fragment ("Delete Branch?") and sentence style when they are a full sentence ("Do you want to save the changes to “notes.md”?").

A command has one canonical title. The menu, the command palette, the keyboard shortcut list, and any button that runs the command all use it.

## Punctuation

- Use the ellipsis character "…" (U+2026), never three periods. In XAML, write the character itself or `&#x2026;`; in C#, the character itself or `…`.
- End a button or menu item with "…" when choosing it opens a window, sheet, dialog, or editor ("Settings…", "Rename…", "Export Workspaces…", "Manage Connections…", "Edit…"). Leave it off when the action happens right away ("Show Sidebar", "Close Tab"). "About Asura" has no "…", as in every Mac app.
- Use one label per action. If "Edit…" opens an editor in one place, it is "Edit…" everywhere.
- Use "…" for progress text that is still running ("Saving…", "Connecting to production-api…").
- Don't end titles, button labels, menu items, segment labels, tab titles, checkbox text, or field labels with a period.
- End descriptions, help text, alert body text, and status messages that are full sentences with a period. A fragment ("No results") has no period.
- Don't use exclamation marks.
- Put names the person chose, such as file, branch, workspace, or connection names, in curly quotation marks: “production-api”.
- Use an en dash for ranges (1–5) and an em dash with no spaces for a break in a sentence.
- Write "and" in sentences. "&" is fine in a short heading or sidebar name ("Security & Privacy").

## Vocabulary

Use one word for each concept everywhere.

| Use | Meaning | Avoid |
|---|---|---|
| Settings | The app's settings window and the things in it | Preferences, Options |
| Keyboard Shortcuts | The list of key combinations and the pane that edits them | Keybindings, hotkeys |
| folder | A container of files, in any UI text | directory |
| working directory, home directory | Established technical terms; keep them | working folder |
| workspace | A named place to work: its tabs, connections, environment, and color, shown in the rail | project, session |
| tab | One page inside a workspace window, shown in the tab strip | view, page |
| screen | A saved arrangement of panels that a tab can open; "saved screen" when the distinction matters | definition, template |
| layout | The grid of slots a screen places its panels in, edited in the Layout Designer | grid, arrangement |
| panel | One tool inside a tab, such as a terminal or a browser | pane (except in Settings), widget, adapter |
| connection | A saved way to reach a host, database, cluster, or network | target, profile, endpoint |
| definition | Never in UI text. Name the thing instead: workspace, screen, layout, connection | definition(s) |
| Sign In, Sign Out | Account and credential flows | Log In, Login |
| Show, Hide | Commands that change visibility; the title names the current action ("Show Sidebar" or "Hide Sidebar") | Toggle |

### Panel types

Each panel type has one noun. Use it in the launcher, the Add Panel chooser, the command palette, menus, tab titles, Settings, and help.

| Noun | Not |
|---|---|
| Terminal | Shell, Console, Local PTY |
| Browser | Web view |
| Files | File Viewer, File Browser |
| Processes | Process Monitor, Process monitor |
| Statistics | Stats, Monitoring |
| Database | SQL, DB |
| Redis | Redis Browser |
| Docker | Containers |
| Kubernetes | K8s, Cluster |
| Git | Repository, Source Control |

In actions, use the noun after the verb: "New Terminal", "Add Browser", "Open Files". Write "Add Terminal", not "+ Terminal".

### Delete, Remove, Terminate

| Verb | Use it when | Example |
|---|---|---|
| Delete | The item and its data are destroyed and can't come back, except from the Trash or a backup | Delete Connection, Delete Branch, Delete File |
| Remove | The item leaves a list or container, but the thing it refers to still exists | Remove Panel, Remove from Favorites, Remove Remote |
| Terminate | A running process, session, or pod is forced to end | Terminate Process |
| Stop | Something running is asked to end and can start again | Stop Container, Stop Forward |
| Close | A window, tab, or panel goes away, and its contents are saved or can be restored | Close Tab |
| Quit | The app ends | Quit Asura |

Destructive verbs (Delete, Terminate, Discard, Reset, Clear) say what they act on when the context doesn't: "Delete Branch", not "Delete".

Don't use "OK" as an alert button. Use the verb that says what happens ("Update Password", "Discard Changes"). "OK" is acceptable only for an alert that only informs.

## Platform terms

The secret store has a different name on each platform. Never write it by hand ("OS vault", "operating-system vault", "system keychain", "OS keychain", "OS keystore", "system credential store", "macOS Keychain"). Use `Asura.Core.PlatformTerms`.

| Property | macOS | Windows | Linux |
|---|---|---|---|
| `SecretStore` | Keychain | Credential Manager | Secret Service |
| `SaveInSecretStore` | Save in Keychain | Save in Credential Manager | Save in Secret Service |
| `StoredInSecretStore` | Stored in Keychain | Stored in Credential Manager | Stored in Secret Service |

The name takes no article: "Stored in Keychain", "Asura couldn't read Keychain".

From C#:

```csharp
CredentialStatus = $"The API key is stored in {PlatformTerms.SecretStore}.";
PasswordStoreLabel = PlatformTerms.SaveInSecretStore;
```

From XAML, with `xmlns:core="using:Asura.Core"` on the root element:

```xml
<CheckBox Content="{x:Static core:PlatformTerms.SaveInSecretStore}" />
```

Tests call `PlatformTerms.SecretStoreFor(HostOperatingSystem.Windows)` and the other `…For` methods to check another platform's text. Add a property to `PlatformTerms` when a new phrase needs the term; don't build the phrase in a view.

## Plurals

- Write real plurals: "1 item", "5 items". Never "item(s)" or "5 item(s)".
- Say "No items" (or what is missing: "No branches") instead of "0 items".
- Choose the whole phrase, not just the noun, when the verb changes too: "1 file was moved", "3 files were moved".
- In C#, branch on the count:

  ```csharp
  Summary = count == 1 ? "1 item" : $"{count:N0} items";
  ```

- Format counts with grouping (`N0`): "1,204 items".

## Settings

Settings follow the Mac settings window: there is no Save, Apply, or OK.

- A setting applies the moment it changes. A change that is previewed, such as the terminal palette, is applied as it is previewed; the page offers Revert, which puts back what the page opened with.
- A value that can't apply yet says why under itself, as a field message, and applies once it can. It doesn't wait behind a button.
- Two edits are drafts, because they only make sense whole:
  - Keyboard Shortcuts. A set of shortcuts has to be free of conflicts before any of it is saved, so the pane keeps Save Shortcuts, and resolving a conflict happens in the draft.
  - A browser profile's HTTP authentication. A password is stored whole or not at all, so the group keeps Save Authentication.
- Leaving a page that holds one of these drafts, or closing the window, asks first. Nothing else asks, because nothing else is unsaved.
- A change that applies as it is made can still fail to save. It stays on show with Try Again and Discard Changes beside it, and until one of them is chosen it counts as a draft: leaving the page asks first.
- An on/off option is a checkbox whose label is its own text, with the box leading and the description under the words. A switch is used only for the setting that turns a whole section on or off, such as Use a network connection.
- Pop-up menu items are menu items, so they are title case ("Confirm Before Open", "Use App Setting").
- A delete that leaves Undo (workspaces, saved screens, file providers) happens at once, so its Delete has no "…". Where a question follows, as for a stored credential, the item keeps "Delete…".

## Error messages

An error message says what happened, why when that is known, and what the person can do next. Write it in sentence style with a closing period.

| Part | Example |
|---|---|
| What happened | Asura couldn't connect to “production-api”. |
| Why | The server didn't answer within 30 seconds. |
| What to do | Check that the host is reachable, then try again. |

- Name the thing the person recognizes (a file name, a connection name), not an internal ID, enum value, or exception type.
- Don't show `exception.Message` directly. Map known failures to a curated message. Put technical details (codes, raw output) behind a "Show Details" disclosure or in diagnostics.
- Don't blame the person, and don't use "illegal", "invalid", "fatal", or "error" when a plain description works ("The port must be a number from 1 to 65535.", not "Invalid port.").
- Say "couldn't" and "can't" in messages. Contractions read naturally.
- When an action partly succeeds, say what worked and what didn't: "Saved “staging”, but the old password couldn't be removed from Keychain. Remove it in Settings › Security."
- A field's validation message sits under that field and talks about that field only.

## Checking copy

```sh
python3 scripts/check-ui-copy.py                         # all of src/
python3 scripts/check-ui-copy.py --paths src/Asura.App/Views/RuntimePanels
python3 scripts/check-ui-copy.py --paths src/Asura.App/Views --exclude src/Asura.App/Views/RuntimePanels
python3 scripts/check-ui-copy.py --summary               # counts per rule
```

The script reads AXAML attribute values that people see (Content, Header, Title, Text, PlaceholderText, Watermark, ToolTip.Tip, AutomationProperties.Name and HelpText, and the label and description properties of Asura's own controls) and C# string literals that are user-facing: those assigned to a member such as Title, Label, Status, or Message, those passed to the shell's status and error helpers, and any other literal that reads as prose. It skips bindings, comments, logging calls, attribute arguments, and lowercase code values.

It reports:

| Rule | Finds |
|---|---|
| `spelling` | British spellings |
| `ellipsis` | "..." instead of "…" |
| `secret-store` | A hand-written name for the secret store |
| `preferences` | "Preferences" |
| `plural` | "(s)" plurals |
| `title-case` | Title style errors in button labels, menu items, tab items, segment labels, window and dialog titles, and C# titles and headers |

When text is right as written, add a line to `scripts/ui-copy-allowlist.txt`:

```text
title-case | src/Asura.App/Views/RuntimePanels/KubernetesRuntimePanelView.axaml | kubectl | Command-line tool name, always lowercase.
```

The fields are the rule (or `*`), a path glob, the flagged word or the whole string, and the reason. Allowlist only code identifiers, third-party names, and established technical terms. Fix everything else.
