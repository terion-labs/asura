# Asura design QA harness

Renders the product's real `MainWindow`, styles, and view models at a 1440 × 900
logical viewport and writes one PNG per route without hand-driving the app.

```sh
./.dotnet/dotnet run --project tools/Asura.DesignQa -- artifacts/design-qa/current
```

It captures the shell's routes plus modal editors and confirmations. Dialogs are
shown off-screen and rendered at their own arranged size, so a capture reflects
the dialog's real geometry rather than a fixed frame.

A route captures the window its surface is shown in. Settings routes render the
Settings window at 1100 × 820, its default frame (or the taller height a `-full`
route asks for); the headless platform draws no traffic lights, so the sidebar's
title band is empty in a capture. `settings-diagnostics` and
`settings-diagnostics-full` bind the Data & Diagnostics page's own sections —
the cache and logs, three earlier runs' recovery snapshots, and the bundle — to
sample collaborators, as the product binds its own; without them the page shows
only its first group. `settings-terminal-light`,
`settings-terminal-high-contrast`, `settings-terminal-scale-200` and
`settings-appearance-scale-250` show the grouped form in the appearances it has
to survive; the high-contrast route marks the Settings window with the
`high-contrast` class as the product's App does every window it reaches.
`settings-workspace-editor-ai` turns the workspace's AI override on and scrolls
the editor to its AI configuration, whose rows are otherwise below every
capture's fold. `settings-appearance-density-spacious` chooses Spacious density
on the Appearance page and must visibly loosen against `settings-appearance`; a
route that clicks a segment or a radio button checks it, since a raised click
event alone toggles nothing. `settings-appearance-override` opens the
appearance override the way its header does; a route that names a disclosure
opens it. Workspace-editor routes and
`overlay-layout-designer` render the Settings window showing the editor,
opened the way the product opens it. `overlay-new-panel`
is the Add Panel popover on the title band's "+".

A route that throws is logged as `FAILED <route>: <error>` and the run carries
on with the next route. The run still exits non-zero, lists every failed route
at the end, and never verifies or approves a baseline over an incomplete
matrix.

Some routes vary the appearance rather than the shell route. A route may carry a
`ThemePreference`, which is republished through the product's own appearance
mapper before the capture. `appearance-corners-tight` and
`appearance-corners-round` use this to pin that the corner-radius and density
settings actually reshape the interface: the two themes differ only in those two
values, so if either setting stops reaching the styles the pair becomes two
identical images.

The design-system gallery has focused dark, light, high-contrast, 200%, and
250% text-scale captures. The accessibility variants are named
`design-system-high-contrast`, `design-system-scale-200`, and
`design-system-scale-250`; they use the same mapper and semantic font resources
as the product rather than harness-only colors or scaling.

Pass route names to capture a subset:

```sh
./.dotnet/dotnet run --project tools/Asura.DesignQa -- artifacts/design-qa/current launcher-home settings-appearance
```

## Supported coherence gate

The full repository check runs a native, deterministic visual gate over every
primary implemented surface. Its approved matrix includes the canonical
1440 × 900 viewport, the window's minimum 640 × 480 viewport, dark, light,
high-contrast, 100%, 200%, and 250% text scale, keyboard focus, overlays, and
the shared state vocabulary.

```sh
./scripts/check-design-qa.sh
```

The gate compares exact dimensions and a tightly bounded perceptual fingerprint
of the normalized RGBA pixels for the same route,
synthetic content, interaction state, viewport, and appearance. Consequently a
clipped control, missing focus/state indicator, broken layout, or composition
change fails until the change is reviewed. Indeterminate progress animation is
frozen at a documented synthetic value before capture; sub-pixel renderer noise
is tolerated, but structural drift is not. Captures are left in the reported
temporary directory when invoking the tool directly; the script removes its
temporary output when it exits.

An intentional change must be reviewed visually before replacing the reference:

```sh
./.dotnet/dotnet run --project tools/Asura.DesignQa -- \
  --approve-baseline artifacts/design-qa/review \
  tools/Asura.DesignQa/design-qa-baseline.json
```

The JSON baseline is a compact approval artifact, not a substitute for that
review. Never approve a reference merely to make the gate green.

## Website assets

Website mode exports every coherent app screen as a Retina 2880 × 1800 PNG of
a 1440 × 900 window. It locks the interface to Normal density and 100% text
scale (not Spacious), forces the runtime tabs to the top, and uses Asura
bronze instead of the host accent. Low-level component probes and
alternate-density QA comparisons are omitted. The shell backdrop keeps its PNG
alpha so a site can place its own blurred background underneath it. Because
the headless platform has no native decorations, this mode adds the standard
macOS traffic lights and applies the same rounded window silhouette to every
frame.

Each frame is the headless compositor's own output with the window's render
scaling set to 2, so text and strokes are rasterized once at the density of a
Retina display. (Redrawing a 1x tree onto a 2x surface through a
`VisualBrush`, as an earlier version did, upscales glyphs that were already
rasterized at 1x and leaves every label soft.)

The interface font is SF Pro Text, read from `SF-Pro-Text-*.otf` files rather
than asked of the host, since the headless platform has no system font to
answer with and Skia's own macOS default is Helvetica. The export looks in
`ASURA_WEBSITE_FONT_DIR`, then the repository root (where those files are
ignored by Git; Apple's license allows the font on Apple platforms but not in a
repository), then `~/Library/Fonts` and `/Library/Fonts`, where Font Book
installs Apple's [SF Pro download](https://developer.apple.com/fonts/). The
export fails rather than fall back to another face.

Default workspace panels use deterministic terminal and browser previews in
website mode. The general QA set keeps the real unavailable-adapter state, but
marketing artwork never substitutes a harness warning for product content. The
browser preview is example.com as Chromium draws it for a dark color scheme.
The full-window adapter routes are `workspace-browser`, `workspace-file-viewer`,
`workspace-statistics`, and `workspace-process-monitor`.

A surface that opens in a window of its own (Settings, and the workspace
editor and Layout Designer it shows) is captured as that window floating over the workspace:
its own rounded silhouette and shadow, traffic lights where the product shows
them, and the workspace blurred behind its translucent material. Settings is
framed at 1180 × 760.

```sh
./.dotnet/dotnet run --project tools/Asura.DesignQa -- --website artifacts/design-qa/website
```

The directory also contains `window-chrome-mask.png`, a white alpha silhouette
at the exact screenshot dimensions for CSS masking. Route names may follow the
output directory to generate a subset while iterating.
`scripts/export-website-shots.sh` runs the export for the routes the site uses
and writes the downscaled WebP copies the site serves.

## Why it exists

Screenshotting the real window needs macOS Screen Recording permission for the
host process, and driving it needs Accessibility permission. This harness
renders in-process through `RenderTargetBitmap`, so it needs neither, never
captures unrelated windows, and produces byte-stable output for diffing.

It runs on Avalonia's headless platform with real Skia drawing: no window ever
appears on screen and nothing steals focus, while text, layout, and colour
render exactly as the desktop platform would draw them offscreen.

## What it does and does not prove

It uses the compiled application resources, the real `MainWindow`, the real
`MainWindowViewModel`, and the shipped styles, so layout, typography, spacing,
and colour are faithful. It replays the app's own appearance-resource mapping so
`ShellFontSize*` and the platform metrics resolve exactly as they do at runtime;
without that step every `DynamicResource` size silently falls back and captures
misreport the typography.

Collaborators are deterministic and in-memory. The harness never touches SQLite,
the OS vault, terminal sessions, or the user's profile, and it writes nothing to
any store. `QaData` is sample content shaped to exercise the product's density.
It is not, and must not be presented as, the user's real
connections, screens, or sessions.

The agent runs offline (`QaOfflineAgentRuntime`), so the workspace route shows
the product's genuine "no provider configured" boundary rather than a simulated
conversation. Routes that need a live PTY render an empty canvas; this harness
does not replace live-terminal or interaction acceptance.

The reference frames were drawn with `#FF8400` as the example host accent, so
the harness supplies that accent to keep captures directly comparable. The
product's own bronze fallback still applies whenever a host reports no accent.
