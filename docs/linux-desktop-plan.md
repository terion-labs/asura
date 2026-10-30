# Linux desktop plan

**Status:** Proposed
**Date:** 2026-10-29, reviewed against main at `f8782176` on 2026-10-30
**Scope:** a shippable Linux x64 and arm64 desktop build with the same product
surface as macOS, native-feeling on GNOME and KDE, and usable on every other
X11 or Wayland desktop.

This document records what the repository already has for Linux, what is
missing, the decisions that shape the port, and an ordered delivery plan.
It does not change any accepted ADR; where it needs one, it names it.

## 1. Where Linux stands today

The Linux groundwork is further along than the README's "on the way" suggests.
Most of it was built as a side effect of keeping the platform boundaries honest
while macOS shipped.

### Already in place

| Area | State | Where |
|---|---|---|
| Terminal engine | `libghostty-vt.so` and `libasura_pty.so` build for `linux-x64` and `linux-arm64` through Zig; the `linux-x64` payload is checked in under `native/artifacts/linux-x64` | `scripts/build-libghostty-vt.sh`, `src/Asura.Desktop/Asura.Desktop.csproj` |
| Browser engine | CEF flat runtime layout for `linux64` and `linuxarm64`, the vendored Exclr8CEF shim builds on Linux, the packaging catalog knows both RIDs | `scripts/build-cef-runtime.sh`, `vendor/exclr8cef/native/CMakeLists.txt`, `tools/Asura.Packaging/CefRuntimeCatalog.cs` |
| SQL language worker | GraalVM native image for Linux RIDs through Docker | `scripts/build-sql-language-worker.sh` |
| Workspace network gateway | Go binary builds for `linux-amd64` and `linux-arm64`; it already has Linux-only TAP relay and guest lifecycle code | `native/workspace-network-gateway/internal/relay/tap_linux.go`, `.../guest/network_linux.go` |
| Dependency locks | `packages.linux-x64.lock.json` and `packages.linux-arm64.lock.json` in every first-party project | `src/*/` |
| Platform profiles | `PlatformProfile.Gnome` and `.Kde` with Adwaita- and Breeze-derived metrics; `Automatic` resolves them from `XDG_CURRENT_DESKTOP` | `src/Asura.Core/ThemePreference.cs`, `src/Asura.App/AvaloniaHostAppearanceAdapter.cs` |
| Host preferences | xdg-desktop-portal Settings client reading `org.freedesktop.appearance` and `org.gnome.desktop.interface` (animations, reduced motion, text scale) with live change subscription | `src/Asura.Desktop/LinuxPortalSettingsClient.cs`, `LinuxHostAccessibilityPreferencesSource.cs` |
| Notifications | Portal `org.freedesktop.portal.Notification` with action routing | `src/Asura.Desktop/LinuxNativeNotificationService.cs` |
| Quick Terminal | X11 global hotkey and X11 active-window bounds; Wayland reports unavailable | `src/Asura.Desktop/LinuxX11GlobalHotkeyService.cs`, `LinuxX11ActiveWindowBoundsSource.cs`, `GlobalHotkeyServiceSelector.cs` |
| Secret vault | Secret Service through the `secret-tool` executable with sidecar metadata | `src/Asura.Infrastructure/LinuxSecretServiceSecretVault.cs`, `PlatformSecretVaultFactory.cs` |
| Keymaps | `builtin.terminal.linux-native` (Ctrl+Shift preset) | `src/Asura.Core/BuiltInKeymaps.cs` |
| Paths | XDG data, cache and state directories | `src/Asura.Infrastructure/AsuraDataPaths.cs`, `LocalArtifactPaths.cs` |
| Trash | freedesktop Trash specification implementation | `src/Asura.App/FileTrash.cs` (`FreedesktopFileTrash`) |
| Window lifetime | The app quits with its last window off macOS; the windowless menu bar and Dock menu are macOS-only already | `src/Asura.App/App.WindowLifecycle.cs` |
| Processes | posix_spawn launcher with Linux signal numbers and `/proc` child enumeration | `src/Asura.Processes/PosixSpawn.cs`, `PosixChildProcess.cs` |
| Connection services fallback | Docker/Podman relay provider gives Linux a workspace network boundary for connection services without the VM runtime (ADR 0057); the guest-side `Asura.Backend` payload is already built for `linux-x64` and `linux-arm64` | `src/Asura.Infrastructure/ContainerRelayIsolationProvider.cs`, `runtimes/linux-*/workspace-backend` |
| Updates model | `linux-package-manager` distribution source as `platform-managed` | `src/Asura.Updates/DistributionIdentityReader.cs` |
| Launching | `xdg-open` for files, URLs and diagnostics folders | `src/Asura.App/PlatformLauncher.cs`, `src/Asura.Desktop/DesktopDiagnosticsArtifactPresenter.cs` |
| Acceptance | Docker Xvfb/Openbox packaged run for `linux-arm64`; `Asura.TerminalAcceptance` runner with a `LinuxX11` platform | `scripts/linux-x11-packaged-acceptance.sh`, `docs/platform-terminal-acceptance.md` |

### Changes on main since the survey

Four commits landed after the tree this plan was surveyed on. None changes a
decision; three add Linux work items, recorded in the phases below.

| Commit | What it does | Effect on Linux |
|---|---|---|
| #164, #167 dialogs as sheets | Dialogs render as Avalonia sheets inside their window (`SheetHost`); `DialogWindow` remains the fallback for dialogs with no window to sit in | Good for Linux: no native sheet API is needed. The sheet corner radius is a macOS 26 value (16) and should come from profile metrics; `DialogWindow` as a standalone window needs the same decoration decision as the main window |
| #168 Command-R reload | Adds the shortcut to the macOS shortcut tables plus an ad hoc `!IsMacOS && Control` branch in the browser panel | One more site for the keymap work; confirms that per-view `else if` branches should give way to a Linux keymap feeding `ApplicationMenuLayout` |
| #169 open folder from Dock or CLI | `asura open PATH [--as terminal\|files\|git]`, hand-off to the running instance through the single-instance pipe, a Dock folder drop via `CFBundleDocumentTypes`, and an admin-prompted installer for `/usr/local/bin/asura` that is macOS-bundle-only | The hand-off and the command parsing are cross-platform. Linux needs the `.desktop` equivalent (`Exec=asura %F`, `MimeType=inode/directory;`, desktop Actions for the three views) and a CLI install path that does not need an administrator (`/usr/bin/asura` from the `.deb`, `~/.local/bin` from the AppImage) |

### Missing or macOS-only

| Area | Gap |
|---|---|
| CI | Every job in `repository-gate.yml` runs on macOS runners. Nothing builds, tests or packages on Linux. `check.sh --full` has a Darwin-only native step but no Linux equivalent |
| Window chrome | The shell measures AppKit traffic lights and sits an `NSVisualEffectView` under the window. Off macOS the title band gets a fixed `Thickness(10, 0)` margin and no window controls, which only works with server-side decorations |
| Menus | Menus are `NativeMenu` objects. Avalonia exports them over D-Bus only to desktops with a global menu (KDE, Unity). On GNOME and most others the menu does not exist, so every menu-only command is unreachable |
| Application keymap | The Linux application keymap falls back to the tmux preset; there is no Ctrl-based desktop keymap. `MacOsStandardShortcuts` and `MacOsMenuShortcuts` carry the only desktop conventions |
| Host palette and font | `IHostSystemPaletteSource` and `IScreenColorSampler` are macOS-only. GNOME and KDE profiles hard-code Cantarell 11 and Noto Sans 10 instead of reading the desktop's font and size. Accent colour on Linux is whatever Avalonia reports, which has to be verified per backend |
| Desktop families | `DetectLinuxDesktop` knows GNOME and KDE. XFCE, Cinnamon, MATE, Budgie, LXQt, and tiling compositors all land on `Unknown` and therefore on the Asura profile |
| Native payloads | `native/artifacts/linux-x64` holds a stale `libghostty-vt.so` (old receipt format, one patch instead of three) and no `libasura_pty.so`, CEF runtime or SQL worker; `linux-arm64` has only the gateway. The repository gate builds `osx-arm64` only |
| Git and Docker | `GitPanelSessionFactory` throws `PlatformNotSupportedException` off macOS and `GitAgentToolSet` disables Git tools; `DockerPanelSessionFactory` strips every `docker.container.*` capability off macOS. Both are acceptance scoping from ADR 0047 and 0048, not missing code |
| File operations | `FileTypeIcons` has a macOS implementation only; reveal-in-file-manager opens the parent folder with `xdg-open` and cannot select the item |
| GUI environment | macOS enriches `PATH` for GUI launches (Homebrew, Docker, Podman); Linux adds nothing, so a `.desktop` launch under GDM or SDDM misses tools installed through `~/.profile` |
| VPN trust | OpenConnect is started with `--cafile=/etc/ssl/cert.pem` on macOS and with no CA bundle on Linux |
| Vault | Depends on an installed `secret-tool`; no native D-Bus client; no fallback when there is no session bus |
| Display server | Avalonia 12.0.5 ships `Avalonia.X11` and `Avalonia.FreeDesktop` only; there is no Wayland backend package. Wayland sessions run the app through XWayland |
| Global hotkey on Wayland | Unavailable; no portal `GlobalShortcuts` client |
| CEF on Linux | Nothing sets up `chrome-sandbox`, user-namespace detection, or GPU/OSR flags; no `--password-store` switch is chosen; nothing validates the runtime shared-library dependencies |
| Globalization | Self-contained Linux builds need `libicu` from the distro or an app-local ICU |
| Native AOT | Only `osx-arm64` has an AOT lock file and an AOT release path |
| Packaging | `tools/Asura.Packaging` has macOS bundle, Velopack and legal-closure builders only. No AppImage, `.deb`, `.rpm`, `.desktop`, icon theme, or AppStream metadata. The macOS legal record explicitly excludes Linux |
| Updates | No `linux-x64-stable` or `linux-arm64-stable` Velopack channel |
| Connection engines | `AsuraConnectionEnginesSupported` is `osx-arm64` only; OpenVPN 3, OpenConnect, Tailscale and WireGuard engines are not built for Linux |
| Workspace isolation | Returns `Unavailable` with `LinuxBackendNotPackaged` / `LinuxKvmRequired`. ADR 0053 names the Apple Containerization Linux backend as the thing to prototype |
| Biometrics | None (acceptable; the vault authorizes without it) |
| Docs and website | README and `website/app/content/facts.ts` say Linux is on the way; `platform-terminal-acceptance.md` requires a physical X11 session for sign-off |

## 2. Decisions

These are the decisions the plan assumes. Each one that changes an accepted
ADR gets its own ADR when implemented.

### 2.1 X11 backend now, Wayland through portals, native Wayland later

Avalonia 12.0.5 has no Wayland backend, so the only supported path is X11 and,
on Wayland sessions, XWayland. That is what GNOME, KDE Plasma 6, Sway and
Hyprland all provide out of the box.

Consequences the port has to own:

- Fractional scaling under XWayland blurs the window. The app must read the
  X11 scale that the compositor advertises (Xft.Dpi, `GDK_SCALE`) and render
  crisp at integer scales; at fractional scales it ships as "sharp at 100% and
  200%, scaled otherwise" and says so in the release notes.
- Anything that depends on X11 window geometry or grabs (Quick Terminal bounds,
  global hotkeys, `_NET_ACTIVE_WINDOW` raise) is explicitly X11-only and must
  degrade to a portal equivalent or a visible "unavailable on Wayland" state.
- Everything else goes through xdg-desktop-portal, which works identically on
  X11 and Wayland: settings, notifications, file chooser, OpenURI, and global
  shortcuts.

When Avalonia ships a Wayland backend, the portal work carries over unchanged.
Track it; do not fork Avalonia for it.

### 2.2 Decorations follow the desktop, and the shell never depends on them

- **KDE, XFCE, Cinnamon, MATE, tiling WMs:** server-side decorations
  (`SystemDecorations.Full`). The window manager draws the frame and buttons.
  The title band holds only the tab strip and toolbar.
- **GNOME and the GTK family:** client-side decorations in the GNOME profile,
  drawn by Asura as an Adwaita-style header bar: rounded top corners, the tab
  strip and toolbar in the bar, window buttons placed according to the
  desktop's `button-layout` setting, drag and double-click handled through
  `BeginMoveDrag` and `_NET_WM_MOVERESIZE`. Mutter draws plain SSD for X11
  clients that do not do this, which looks foreign next to every other GNOME app.
- **Always:** the app has to work with `SystemDecorations.None` and no custom
  resize grips. Tiling compositors strip decorations and resize windows
  themselves. No command may be reachable only through the title band.

Translucency, blur and glass stay off. `_KDE_NET_WM_BLUR_BEHIND_REGION` exists
in Avalonia's X11 backend, but KWin-only blur is not worth a third chrome
variant. The GNOME and KDE profiles stay opaque, as ADR 0006 already allows.

### 2.3 In-window menus replace the native menu off macOS

The command catalog behind `ApplicationMenuBar` is kept. It gets a second
presentation:

- A managed Avalonia `Menu` bar for the KDE profile and the generic profile,
  hidden behind F10 when the user turns it off.
- A "primary menu" button (the GNOME hamburger) in the header bar for the
  GNOME profile, holding the same tree.
- D-Bus menu export stays enabled (`X11PlatformOptions.UseDBusMenu`) so KDE's
  global-menu users get it for free.

`WindowlessMenuBar` and "keep running with no windows" are macOS behaviour.
On Linux the application quits with its last window
(`ShutdownMode.OnLastWindowClose`), as every Linux desktop expects.

### 2.4 One Linux application keymap, Ctrl-based

A `builtin.application.linux` keymap mirroring GNOME Terminal and Konsole
conventions: Ctrl+Shift+T new tab, Ctrl+Shift+W close tab, Ctrl+Shift+N new
window, Ctrl+Shift+C / Ctrl+Shift+V in terminals, Ctrl+, settings, Ctrl+Q
quit, F10 menu, F11 full screen, Ctrl+Page Up / Page Down tab switching.
`KeyModifiers.Control` is the command modifier; `ShortcutText` renders
`Ctrl+Shift+T`, never glyphs.

### 2.5 Appearance is inherited, not imitated

Automatic mode on Linux reads the desktop and applies it live, in this order:
explicit user setting, desktop value, profile default, Asura default.

| Signal | GNOME and GTK family | KDE and Qt family | Fallback |
|---|---|---|---|
| Light/dark | portal `org.freedesktop.appearance color-scheme` | same | `GTK_THEME` suffix, then light |
| Accent | portal `accent-color` (GNOME 47+) | portal `accent-color` (Plasma 6) | bronze |
| UI font and size | portal `org.gnome.desktop.interface font-name` ("Adwaita Sans 11", "Cantarell 11") | portal `org.kde.kdeglobals.General font` ("Noto Sans,10") | fontconfig `sans-serif` at 10 |
| Monospace font | `monospace-font-name` | `org.kde.kdeglobals.General fixed` | bundled JetBrains Mono |
| Text scale | `text-scaling-factor` | Plasma has none; DPI only | 1.0 |
| High contrast | portal `contrast` | portal `contrast` | off |
| Reduced motion | `enable-animations` | none | off |
| Window buttons | `org.gnome.desktop.wm.preferences button-layout` | SSD, not needed | close only, right |
| Cursor theme and size | `cursor-theme`, `cursor-size` through Avalonia/Xcursor | same | Adwaita |
| File icons | freedesktop icon theme plus shared-mime-info | same | glyph fallback |

The desktop family detection grows from two values to a family mapping:
GNOME, Budgie, Cinnamon, MATE, XFCE, Pantheon → GTK family (GNOME profile
metrics); KDE, LXQt, Deepin → Qt family (KDE profile metrics); Sway, Hyprland,
i3, river, and unknown → generic profile with the system font. `ThemePreference`
keeps `Gnome` and `Kde` as the user-visible profiles; the family only picks the
automatic default.

Colour values beyond light/dark and accent are not read from GTK or Qt themes.
Parsing `gtk.css` or kdeglobals colour schemes is brittle and ADR 0006 already
places surfaces on semantic tokens; the accent and scheme are what make the
app read as "this desktop's".

### 2.6 Secret Service over D-Bus, with honest fallbacks

Replace the `secret-tool` process boundary with a native D-Bus Secret Service
client (Tmds.DBus.Protocol is already a dependency): open a session with
`dh-ietf1024-sha256-aes128-cbc-pkcs7`, use the default collection, unlock with
the service's prompt, and store items with Asura attributes. This removes the
`libsecret` tools dependency, works inside Flatpak with `--talk-name=org.freedesktop.secrets`,
and covers GNOME Keyring and KWallet (which exposes the same interface).

Keep `secret-tool` as a diagnostic path. With no session bus or no provider
(headless, minimal WMs), the vault reports `Unavailable` with the existing
guidance; it does not fall back to plaintext.

### 2.7 CEF runs sandboxed or not at all

ADR 0042 already decides this: on Linux the browser engine defaults to the
Chromium sandbox and cannot opt out. Chromium's Linux sandbox needs either the
SUID `chrome-sandbox` helper or unprivileged user namespaces. Ubuntu 24.04
restricts user namespaces through AppArmor; AppImages cannot carry SUID bits;
`.deb` packages can.

- `.deb` and `.rpm`: install `chrome-sandbox` with mode 4755 and ship an
  AppArmor profile for the executable, as Chrome and Electron packages do.
- AppImage: detect usable user namespaces at startup. When they are
  unavailable the browser engine stays off, the browser panel and the
  diagnostics page say why, and the message names the fix: install the
  `.deb`, or allow user namespaces for this executable. No `--no-sandbox`.
- `--password-store=basic`. Chromium's own cookie encryption on Linux would
  otherwise reach into the keyring under its own name; Asura already protects
  browser storage itself (ADR 0060) and keeps CEF state ephemeral per logical
  profile (ADR 0051).
- Off-screen rendering stays on the CPU path at first. Linux shared-texture
  OSR is a research item, not a dependency.
- Runtime shared libraries (`libnss3`, `libgbm1`, `libasound2`, `libxkbcommon`,
  `libatk`, `libcups2`, and `libfido2` for security keys) become package
  dependencies and an `ldd` check in the packaged acceptance run.

### 2.8 Native AOT, app-local ICU, glibc 2.35 baseline

Linux release builds use Native AOT like macOS, with `clang` and `lld` on
the build host, so startup, trimming and the "no reflection" guarantees stay
identical across platforms. Build inside an Ubuntu 22.04 container so the
produced binary links against glibc 2.35 and runs on Ubuntu 22.04+, Debian 12+,
Fedora 38+, openSUSE Leap 15.5+ and Arch. Ship app-local ICU
(`Microsoft.ICU.ICU4C.Runtime`) instead of depending on the distro's `libicu`.

### 2.9 Distribution: AppImage through Velopack, plus .deb and .rpm

- **GitHub direct lane:** Velopack produces an AppImage per RID and a
  `linux-x64-stable` / `linux-arm64-stable` channel. In-app updates work the
  same as on macOS (`UpdateManager` understands AppImages). The distribution
  manifest's `github-release` + `velopack` pair gains Linux RIDs.
- **Package-manager lane:** `.deb` and `.rpm` built from the same publish
  output, marked `linux-package-manager` + `platform-managed` so the About
  page shows no Velopack actions. An apt/dnf repository is a later step;
  GitHub Releases host the packages first.
- **Flatpak:** evaluated after the first release. Chromium inside Flatpak needs
  the `zypak` wrapper and loses the namespace sandbox; the portal work in this
  plan is a prerequisite either way.
- **Desktop integration:** `sh.asura.Asura.desktop` with `StartupWMClass`,
  hicolor icons rendered from `assets/logo.svg`, an AppStream `metainfo.xml`,
  and a D-Bus-free single instance (the existing pipe coordinator maps to a
  Unix socket under `$XDG_RUNTIME_DIR`).
- **Signing:** no platform code signing exists. Publish `SHA256SUMS` and a
  detached signature (minisign or GPG, key published on the website) with
  every release; the `.deb` and `.rpm` carry the same signature. Release
  evidence binds the packages to the source tree exactly as the macOS lane does.

### 2.10 Workspace isolation on Linux: user namespaces first, VM second

ADR 0053 names the Apple Containerization Linux backend (cloud-hypervisor,
KVM, virtiofsd) as the prototype. That boundary is strong but needs KVM,
which laptops in corporate images, many VMs and every container lack.

The recommended Linux provider is a rootless namespace container:

- user namespace plus mount, PID, UTS, IPC and network namespaces, created
  by a small app-owned helper (no `bubblewrap` dependency);
- a persistent rootfs directory unpacked from the existing
  `packaging/workspace-images/ubuntu-24.04` image, reused across restarts
  exactly as the ext4 disk is on macOS;
- host directories as bind mounts, read-only or read-write per the
  `WorkspaceDefinition` mounts;
- one TAP interface inside the workspace's network namespace, owned by the
  host-owned gateway, which already has Linux TAP relay code. The gateway's
  packet contract and every provider adapter (direct, proxy, WireGuard,
  OpenVPN, OpenConnect, Tailscale) stay unchanged;
- structured exec, PTY resize and route leases over the same Unix control
  channel as the macOS runtime.

This satisfies "persistent Linux container with its own root filesystem and
network namespace" without root, KVM or a kernel image. It does not provide a
separate kernel. The capability flags say so, and the KVM-backed SDK backend
remains the later option for users who need the VM boundary. Distributions
that disable unprivileged user namespaces report
`LinuxHostSharingRuntimeRequired` with the sysctl or AppArmor fix to apply.

Connection engines (OpenVPN 3 Core, OpenConnect, Tailscale, WireGuard) are
built for `linux-x64` and `linux-arm64` in the same Ubuntu 22.04 container
and packaged under `runtimes/<rid>/connection-engines`.

## 3. Desktop support matrix

Target state after the plan is delivered.

| Desktop | Session | Chrome | Menus | Appearance source | Quick Terminal | Notes |
|---|---|---|---|---|---|---|
| GNOME 46+ | Wayland (XWayland) | CSD header bar | primary menu button | portal (gnome/gtk backend) | portal GlobalShortcuts (GNOME 48+), bounds from primary monitor | integer scaling crisp |
| GNOME | X11 | CSD header bar | primary menu button | portal | X11 hotkey and bounds | |
| KDE Plasma 6 | Wayland (XWayland) | SSD (Breeze) | menu bar + D-Bus global menu | portal (kde backend) | portal GlobalShortcuts | |
| KDE Plasma 5/6 | X11 | SSD | menu bar + D-Bus global menu | portal | X11 hotkey and bounds | |
| XFCE, Cinnamon, MATE, Budgie | X11 | SSD | menu bar | portal (gtk backend) or gsettings keys through portal | X11 | GTK family metrics |
| LXQt | X11 | SSD | menu bar | portal (lxqt backend) | X11 | Qt family metrics |
| Sway, Hyprland, river | Wayland (XWayland) | none | menu bar | portal (wlr/hyprland backends have no Settings; env fallback) | portal GlobalShortcuts where provided | generic profile |
| i3, bspwm, Openbox | X11 | WM decides | menu bar | env fallback | X11 | generic profile; CI harness uses Openbox |
| No session bus | any | SSD | menu bar | defaults | X11 only | today the app refuses to start (Phase 0 item 8); target: vault unavailable, notifications in-app only |

## 4. Delivery plan

Phases are ordered so each one leaves a build that is better than the last
and verifiable in CI. Sizes: S under a day of focused work, M a few days,
L a week or more.

### Phase 0: a green Linux lane

Goal: `linux-x64` restores, builds, tests and smoke-runs in CI on every pull
request; `linux-arm64` builds.

1. Add `ubuntu-24.04` and `ubuntu-24.04-arm` jobs to `repository-gate.yml`:
   locked restore for Linux RIDs, Release build, the six test sections with
   Avalonia headless, `check.sh --quick`. (M)
2. Build and cache the Linux terminal payload (`build-libghostty-vt.sh --rid
   linux-x64` and `linux-arm64`) in CI the way the macOS payload is. The
   checked-out `linux-x64` payload is stale and incomplete; rebuild it with
   the current patch set and add `libasura_pty.so`, the CEF runtime and the
   SQL worker so a local Linux `dotnet run` works. (S)
3. Make `check.sh --full` run its native step on Linux (Porta.Pty descriptor
   boundary test, libghostty ABI probe), and extend
   `NativeTerminalBuildContractTests` so the gate's Linux lane is a tested
   convention like the macOS one. (S)
4. Fix whatever the first run shows. The Xvfb history records one startup
   crash from a D-Bus type mismatch; every portal and D-Bus client must be
   wrapped so a missing bus, missing portal, or unexpected variant never
   stops startup. (M)
5. Make the vendored Exclr8CEF shim build on Linux. As of 2026-10-30 it
   compiles with neither compiler: GCC rejects the ambiguous
   `WindowOpenDisposition` typedef inherited from two CEF handler bases
   (`shim/exclr8cef_osr.h`), and the shim's Linux flags add the GCC-only
   `-Wno-stringop-truncation` under `-Werror`, which clang refuses. Decide
   the Linux compiler (clang, matching macOS), fix both in the reviewed
   patch set with the `ASURA-PATCHSET.sha256` manifest and receipt digests
   updated, and add `bzip2` and `libx11-dev` to the documented CEF build
   prerequisites. (S)
6. Keep `linux-x11-packaged-acceptance.sh` runnable: its source copy must
   carry `native/artifacts/<rid>` (the unanchored `--exclude=artifacts`
   dropped it), the container must get the SDK `global.json` pins (the
   floating `dotnet/sdk:10.0` tag has moved past it, and the muxer reads
   `global.json` from the working directory, so the inner script must run
   dotnet from the source root), CEF's runtime libraries must be installed,
   and the driver needs a Secret Service (see item 8). All fixed in the
   working tree alongside this plan. (S)
7. Ship one Linux `libSkiaSharp.so`. Avalonia.Skia 12.0.5 depends on
   `SkiaSharp.NativeAssets.Linux` 3.119.4 while PDFtoImage depends on
   `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.150.1; NuGet unifies the
   managed `SkiaSharp` to 4.150.1 but both native package ids survive, and
   the Linux publish kept the milestone-119 library, which SkiaSharp 4.150
   refuses to load. Every Avalonia window then fails in `SkiaApi`'s type
   initializer, including the startup-failure dialog, so the packaged app
   exited with only secret-safe diagnostic codes. macOS and Windows never hit
   this because their native packages share one id. Fixed by pinning the
   fontconfig-linked package to 4.150.1 and excluding the other's native
   asset; regenerated every lock file except `packages.windows.lock.json`,
   which only a Windows host can write. (S, done in the working tree)
8. Decide what Asura does with no Secret Service. A fresh profile bootstraps
   application encryption by storing its keys in the vault, so with no
   session bus or no `org.freedesktop.secrets` provider the desktop refuses
   to start, and the failure dialog cannot explain it when the window stack
   itself is broken. With a locked keyring and no prompter the app hangs at
   startup. Minimal window managers, SSH sessions and containers all land
   here. Options: an explicit "no OS keystore" mode with a passphrase-wrapped
   file vault, or a clear startup dialog with the install command. The
   Xvfb harness now provides a passwordless GNOME Keyring on its session bus,
   which is also the recipe for CI. (M)
9. Fixed in the working tree, found by the first harness run that got past
   the window stack: `LinuxSecretServiceSecretVault` closed the `secret-tool`
   stdin pipe underneath its `StreamWriter`, and the child-process dispose
   then threw "Cannot access a closed pipe", so every successful store was
   reported as a vault failure and no fresh profile could open on Linux.
   macOS never runs this code. Also fixed: the Linux CEF shim's RPATH is the
   working directory, so `libcef.so` only resolved when Asura was started
   from its own folder; `BrowserEngineRuntime` now maps `libcef.so` by
   absolute path before the shim loads. The proper fix is `$ORIGIN` in the
   vendored CMake, with the patch-set manifest updated. (S, done)
10. Bring the harness's expectations up to date with the shell. On
    2026-10-30, with the fixes above, the linux-arm64 run passes startup,
    PTY, Unicode round trip, viewport resize, the `less` TUI, SGR mouse and
    the OSC 52 write policy, with no diagnostics in `asura.log`. Three
    checks still fail for harness reasons that need confirming: the
    close-confirmation and paste-confirmation checks look for separate X11
    windows that became in-window sheets in #164, and the Quick Terminal
    check waits for a window titled `Asura Quick Terminal` while the window
    is now titled `Quick Terminal`. Update the driver, then re-run to see
    whether anything real remains behind those three. (S)
11. Turn `linux-x11-packaged-acceptance.sh` into a CI job on `linux-x64`
   with Xvfb and Openbox: start the self-contained build, open a PTY, run the
   bounded terminal checks, take a screenshot artifact. (M)

Exit: a PR cannot merge if Linux does not build and test.

### Phase 1: baseline desktop integration (X11)

Goal: a self-contained Linux build that is complete and correct on any X11
desktop with server-side decorations, before any desktop-specific polish.

1. Managed in-window menu bar bound to `ApplicationMenuBar`'s command tree;
   F10 focus; `UseDBusMenu` enabled for global-menu desktops. (M)
2. `builtin.application.linux` keymap and Ctrl as the command modifier
   everywhere the Meta/Control switch is made today: `MainWindow`,
   `ManagedTerminalSurface`, `ManagedTerminalOverlay`, `DialogWindow`,
   `FileActionViewModel`, the database and browser panel views including
   `BrowserRuntimePanelView.MenuBar`, `ShortcutText`, and the
   `KeyGestureDisplayConverter`; a Linux `MenuShortcutResolver` so menu
   items show their shortcuts. New shortcuts land in the keymap, not in
   per-view `else if` branches. (M)
3. Title band off macOS. `MainWindow.axaml` asks for the extended client area
   on every platform, which on X11 removes the window manager's frame. Decide
   explicitly: outside the GNOME profile the window keeps server-side
   decorations (`ExtendClientAreaToDecorationsHint` off) and the band reserves
   no button space; verify `BeginMoveDrag` on the empty band and double-click
   to maximize. `SettingsWindow` and a standalone `DialogWindow` take the
   same path; the sheet corner radius moves into `PlatformMetrics`. (S)
4. Lift the macOS gates in `GitPanelSessionFactory`, `GitAgentToolSet`,
   `DockerPanelSessionFactory` and `DockerPanelSession.Control` once the
   Linux acceptance runs for ADR 0047 and 0048 pass; until then the Linux
   build shows Git and Docker control as unavailable with a reason. (M,
   mostly evidence)
5. Reveal in file manager through `org.freedesktop.FileManager1.ShowItems`
   with the existing `xdg-open` of the parent as fallback. (S)
6. `LinuxFileTypeIcons` from shared-mime-info and the current icon theme
   (`index.theme` inheritance, hicolor fallback), cached per MIME type;
   glyph fallback stays. (M)
7. GUI `PATH`: when launched from a `.desktop` entry, read the login shell's
   `PATH` once (`$SHELL -l -c`) through the posix_spawn launcher and merge it
   into `PathConnectionExecutableLocator`, as the macOS branch does with its
   fixed list. (S)
8. X11 platform options: `EnableIme = true` (XIM, so ibus and fcitx work in
   terminals), `WmClass = "Asura"`, cursor theme from the portal. (S)
9. CEF on Linux: subprocess dispatch verified, `chrome-sandbox` and
   user-namespace detection, the fail-closed path from 2.7,
   `--password-store=basic`, `--ozone-platform=x11`, runtime library check. (M)
10. App-local ICU and the Native AOT publish for `linux-x64` and `linux-arm64`
    with their lock files; trim and AOT warnings fixed at the owning project. (M)
11. Secret Service D-Bus client replacing `secret-tool`, with the prompt
    flow, locked-collection handling, and `Unavailable` when no bus. (L)
12. Notification activation on X11: raise through `_NET_ACTIVE_WINDOW`. (S)
13. OpenConnect CA bundle on Linux: resolve the distro bundle
    (`/etc/ssl/certs/ca-certificates.crt`, `/etc/pki/tls/certs/ca-bundle.crt`)
    and pass `--cafile` as macOS does. (S)

Exit: the Xvfb/Openbox acceptance passes every bounded check that is not
tagged `NOT_PROVEN` for physical hardware.

### Phase 2: inheriting the desktop

Goal: Automatic mode looks like a GNOME app on GNOME and a KDE app on KDE,
and follows the user's live changes.

1. Desktop family detection (2.5) and automatic profile selection by family. (S)
2. `LinuxHostSystemPaletteSource` implementing `IHostSystemPaletteSource`
   with accent from the portal and a live `SettingChanged` subscription;
   verify what Avalonia's X11 `IPlatformSettings` already reports and avoid
   reading the same key twice. (M)
3. Desktop font and size: read `font-name` / kdeglobals `font` through the
   portal, parse the Pango or Qt font string, and feed family and size into
   `PlatformMetrics` instead of the hard-coded Cantarell 11 and Noto Sans 10;
   `monospace-font-name` becomes the default terminal font when the user has
   not chosen one. (M)
4. GNOME header bar: CSD in the GNOME profile with window buttons from
   `button-layout`, rounded top corners, `_GTK_FRAME_EXTENTS` shadow margins,
   edge resize through `_NET_WM_MOVERESIZE`; SSD toggle in Appearance settings
   for people who prefer it. (L)
5. Primary-menu button for the GNOME profile presenting the same command
   tree as the menu bar. (M)
6. Scaling: honour Xft.Dpi and `GDK_SCALE` for integer scales; verify
   `RenderScaling` propagation to CEF and terminal glyph atlases; document
   XWayland fractional scaling. (M)
7. High contrast and `differentiate without colour` from the portal
   `contrast` key; reduced motion already present. (S)
8. Design QA: extend `tools/Asura.DesignQa` and `check-design-qa.sh` with
   GNOME and KDE reference captures from the Xvfb harness running a GNOME
   session (`gnome-session` under Xvfb is heavy; use `xfwm4` with the GTK
   portal backend for the GTK family and `kwin_x11` for the Qt family). (M)

Exit: screenshots in Settings › Appearance previews match the running desktop
without the user touching a profile.

### Phase 3: packaging, updates, and release lane

Goal: a Linux release that installs, updates and uninstalls cleanly, with the
same evidence standard as macOS.

1. `tools/Asura.Packaging`: `LinuxPublishCommand` (publish AOT per RID into a
   flat tree with `runtimes/<rid>/native`, CEF flat runtime, terminal payload,
   SQL worker, gateway, legal files), `LinuxAppImageCommand` (Velopack `vpk`,
   `--channel linux-x64-stable`), `LinuxDebCommand` and `LinuxRpmCommand`
   (from the same tree; `nfpm` or `dpkg-deb`/`rpmbuild` in the container). (L)
2. `licenses/linux-release-legal.json` and a Linux legal-closure validator
   mirroring `MacOsReleaseLegalClosure`; the macOS record keeps excluding Linux. (M)
3. `.desktop` with `Exec=asura %F`, `MimeType=inode/directory;` and
   Actions for "Open as Terminal / Files / Git" (the Linux side of #169),
   icons, AppStream metainfo, AppArmor profile, post-install script for
   `chrome-sandbox` mode, `/usr/bin/asura` from the package and an
   administrator-free `~/.local/bin` install from the AppImage in place of
   the macOS `CommandLineToolInstaller`. (M)
4. Distribution manifests for both lanes; `DistributionIdentityReader` accepts
   Linux RIDs for `github-release` + `velopack`; About page strings for
   AppImage and package-manager installs. (S)
5. Release workflow: `linux-release.yml` on `ubuntu-24.04` and
   `ubuntu-24.04-arm` building inside the Ubuntu 22.04 container, publishing
   AppImage, `.deb`, `.rpm`, `SHA256SUMS`, signature and release evidence on
   the same tag as macOS. (M)
6. `docs/linux-packaging.md` with the install, update, uninstall and data
   location contract; `linux-release-rehearsal.md` mirroring the macOS one. (S)
7. Update README and `website/app/content/facts.ts` when the first package
   ships, not before. (S)

Exit: a tag produces signed Linux artifacts next to the macOS ones.

### Phase 4: Wayland-era features through portals

Goal: the features that are X11-only today work on Wayland sessions through
portals, and the Wayland gaps that remain are stated in the product.

1. `LinuxPortalGlobalShortcutsService` implementing the global hotkey
   service over `org.freedesktop.portal.GlobalShortcuts` (KDE 5.27+, GNOME 48+,
   Hyprland); the selector prefers it when the portal reports the interface,
   falling back to X11 grabs. (M)
2. Quick Terminal bounds on Wayland from the primary monitor's working area
   (Avalonia `Screens`), with the X11 active-window source kept on X11. (S)
3. Notification activation tokens: raise through `xdg_activation_v1` is not
   reachable from XWayland; keep the token, document the limit, re-evaluate
   with a Wayland backend. (S)
4. Clipboard: verify OSC 52 and image paste under XWayland on GNOME and KDE;
   primary selection paste on middle click in terminals. (S)
5. Track Avalonia's Wayland backend; when it ships, enable it behind a
   setting and re-run the acceptance matrix. (ongoing)

### Phase 5: isolation and connection engines

Goal: the "Isolate workspace" switch works on Linux with the same provider
contract, kill switch and route leases as macOS.

1. ADR: Linux workspace isolation provider (2.10). (S)
2. `linux-workspace-runtime`: a Go helper in `native/` (next to the gateway,
   same toolchain) creating the namespace set, pivoting into the persistent
   rootfs, applying bind mounts, and serving the same Unix control protocol
   as the macOS runtime (`acquire`, `release`, `inspect`, `exec`, `resize`,
   `route lease`). Inside the namespace it starts the same `Asura.Backend`
   guest payload that the VM and the container relay already run as UID 1000,
   so the guest side needs no new code. (L)
3. Gateway TAP attachment inside the workspace network namespace; reuse the
   existing Linux TAP relay and the guest network setup from the relay
   provider. The container relay provider (ADR 0057) stays as the fallback
   on hosts that disable user namespaces but have Docker or Podman. (M)
4. Boot assets on demand: the Ubuntu image downloaded and unpacked as a
   rootfs directory with the same manifest and digest checks as the macOS
   boot image (ADR 0055). (M)
5. Capability detection: user namespaces (`/proc/sys/user/max_user_namespaces`,
   `kernel.unprivileged_userns_clone`, AppArmor `userns` restriction) with
   actionable `WorkspaceIsolationPlatformLimitation` messages. (S)
6. Connection engines for Linux RIDs: OpenVPN 3 Core engine, OpenConnect with
   `libopenconnect.so`, Tailscale, WireGuard; `AsuraConnectionEnginesSupported`
   and the packaging closure extended. (L)
7. The workspace database backend and relay fallback (ADR 0056, 0057) verified
   inside the namespace provider. (M)

### Phase 6: acceptance and sign-off

1. Physical or self-hosted X11 runs of `Asura.TerminalAcceptance` on a GNOME
   X11 session and a KDE X11 session, as `platform-terminal-acceptance.md`
   requires; add a Wayland (XWayland) observation profile that records which
   checks are expected `NOT_PROVEN`. (M)
2. Vault acceptance on GNOME Keyring and KWallet per `platform-vault-acceptance.md`. (S)
3. Accessibility: AT-SPI through `Avalonia.FreeDesktop.AtSpi`; Orca reads the
   terminal and the agent panel; extend `platform-accessibility-acceptance.md`. (M)
4. Soak run mirroring `macos-arm64-soak-acceptance.md` on `linux-x64`. (M)

## 5. What this plan deliberately leaves out

- A separate `Asura.Platform.Linux` project. `architecture.md` sketches
  per-platform projects, but the selector-per-seam pattern in `Asura.Desktop`
  has carried macOS, Windows and Linux branches without trouble. Adding a
  project boundary now would move code without removing a decision.
- Theme colour extraction from GTK CSS or kdeglobals (see 2.5).
- KWin blur, GNOME-style libadwaita widgets, or any further chrome variant.
- Biometric vault authorization. Linux has no portable equivalent; the vault
  authorizes through the Secret Service prompt.
- Snap packaging. The confinement model fights the terminal, PTY and
  namespace work in this plan; Flatpak is the sandboxed format to evaluate.
- Changing the terminal, agent, database, Kubernetes or file provider code.
  Those layers have no Linux gaps today.

## 6. Risks and open questions

- **XWayland scaling** is the most visible defect on default GNOME and KDE
  installs, which are Wayland. Integer scaling is crisp; 125% and 150% are
  not. The honest options are to ship and document, or wait for a Wayland
  backend. Ship and document.
- **Chromium sandbox on Ubuntu 24.04 AppImages** leaves the browser panel
  off for a popular target. The `.deb` is the recommended install there and
  the download page has to say so.
- **D-Bus variance.** wlroots portals lack `Settings`; some distros start no
  session bus for console logins. Every client must time out fast and report
  `Unavailable` rather than throw. The Phase 0 wrapping is what prevents the
  2026-07 startup crash from recurring in another shape.
- **Native AOT on Linux arm64** has had linker regressions in past SDKs. The
  self-contained non-AOT publish stays as the fallback lane and is what the
  current Xvfb evidence was captured with.
- **glibc baseline** of 2.35 excludes RHEL 8 and Ubuntu 20.04. Acceptable.
- **Isolation without a VM** is a weaker boundary than macOS. The UI must
  describe it as "container with its own filesystem and network" and the
  KVM-backed SDK backend remains available later for the VM boundary.
- **Avalonia Wayland** timing is outside this repository's control.

## 7. Testing Linux from a Mac host

All of the work above except the physical sign-off can be developed and
checked on an Apple silicon Mac. Four tiers, from cheapest to most faithful.

| Tier | Tool | What it proves | What it cannot prove |
|---|---|---|---|
| Container with Xvfb | OrbStack or Docker with a `linux/aarch64` daemon running `scripts/linux-x11-packaged-acceptance.sh` | Build, restore, PTY, terminal state engine, bounded Openbox window checks. This is the CI stand-in and works today | Any real desktop behaviour, portals, vault, hotkeys on a compositor |
| Headless VM | Lima (`--vm-type vz --mount-type virtiofs --rosetta`) or an OrbStack machine; an amd64 OrbStack or Lima machine runs `linux-x64` through Rosetta | The full repository gate on Linux, `check.sh --quick`, the test sections, Native AOT publish, `.deb`/AppImage assembly, x64 smoke runs | Compositor behaviour; x64 under Rosetta is a smoke test, not sign-off, and the CEF sandbox differs there |
| Desktop VM | UTM (Apple Virtualization backend, virtio-gpu) or VMware Fusion with Ubuntu GNOME, Fedora KDE, and one XFCE image; Lima `--video` after installing a desktop in the guest also works | GNOME Wayland and X11, Plasma 6, portals, GNOME Keyring and KWallet, header bar and SSD chrome, menus, accent and font inheritance, scaling, notifications, GlobalShortcuts portal (GNOME 48+, Plasma 6) | GPU-dependent rendering, sleep/wake, real HiDPI panels, non-arm64 hardware |
| Physical or self-hosted X11 | `Asura.TerminalAcceptance --platform LinuxX11` on a named machine | What `platform-terminal-acceptance.md` requires for a release | — |

Notes for the VM tiers:

- The repository's `.dotnet` SDK and `.nuget` cache are macOS binaries. Give
  the guest its own clone or a copy of the tree (the Xvfb harness already
  excludes `.dotnet`, `bin`, `obj`), run `bootstrap.sh` there, and keep the
  mount for source only.
- libghostty-vt and the PTY shim cross-compile for Linux from macOS through
  Zig; their tests only run when host and target match, so run
  `build-libghostty-vt.sh` inside the guest once per payload change. The CEF
  shim must be built on a Linux host, so it is a guest job.
- A desktop VM needs a login session for the portals, keyring and session
  bus to exist; SSH-only logins reproduce the "no session bus" row of the
  support matrix, which is useful in its own right.
- For scaling tests set the guest display to 200% and to 150% to see the
  integer and fractional XWayland cases.
- Nested virtualization is not needed by anything in this plan; the
  namespace isolation provider runs inside any of these guests.

### Observations from the desktop VM, 2026-10-30

One Ubuntu 26.04 Lima VM (`--vm-type vz --video`) with GNOME 50, KDE Plasma
6.6 and XFCE 4.20 installed side by side, GDM autologin, and the linux-arm64
package under `/opt/asura`. `scripts/acceptance/linux-desktop-vm-provision.sh`
sets the guest up and `scripts/acceptance/linux-desktop-vm-test.sh <user>
<session> <out>` switches the autologin session, launches Asura, probes the
portal and the Secret Service, and captures light, dark and blue-accent
screenshots. Session names are `ubuntu`, `plasma` and `xfce`.

| Desktop | Starts | Chrome | Scheme follows live | Accent | Secret Service |
|---|---|---|---|---|---|
| GNOME 50, Wayland (XWayland) | yes, no diagnostics | Mutter server-side title bar above the tab strip | yes | follows the portal: switching GNOME to blue recoloured tabs, buttons and badges within seconds | GNOME Keyring, works |
| Plasma 6.6, Wayland (XWayland) | yes | Breeze server-side title bar | yes, `plasma-apply-colorscheme BreezeDark` | follows the portal (Breeze blue) | GNOME Keyring on the bus, works |
| XFCE 4.20, X11 | yes | xfwm4 title bar | not tested (no portal scheme) | was wrong: the portal has no `accent-color`, yet Avalonia reported its default blue as the host's. Fixed the same day: `LinuxSystemPaletteSource` in `Asura.Desktop` reads the portal's accent itself and reports it absent when the key or the portal is missing, and the adapter treats that like macOS Multicolor, so the bronze fallback applies. Verified on XFCE (bronze) and GNOME (still follows blue) | works |

Other notes: GNOME 50 ships no X11 session any more, so GNOME is XWayland
only. The virtual display is 3296x2064 at 96 DPI, so every desktop and Asura
rendered at 1x; fractional scaling is still untested. XDG_CURRENT_DESKTOP was
`ubuntu:GNOME`, `KDE` and `XFCE`, so GNOME and KDE picked their profiles and
XFCE landed on the generic one, as the family mapping in 2.5 predicts.

## 8. ADRs this plan creates

1. Linux display backend: X11 and XWayland now, portals for shared features,
   Wayland backend when available (amends ADR 0006 notes on Linux adapters).
2. Linux window chrome and menus per desktop family (amends ADR 0006).
3. Secret Service over D-Bus (amends ADR 0014's platform list).
4. Linux distribution: Velopack AppImage and package-manager lanes (extends ADR 0052).
5. Linux workspace isolation provider over user namespaces (amends ADR 0053 and 0054).
