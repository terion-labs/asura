using System.Runtime.InteropServices;
using Asura.App;
using Asura.Desktop.LinuxDesktopColors;
using Avalonia.Media;
using Tmds.DBus.Protocol;

namespace Asura.Desktop;

/// <summary>
/// The Linux host's palette: its accent from the settings portal, and its
/// surfaces from whatever the desktop publishes them in.
///
/// Avalonia reads the portal's accent too, but when a desktop has none
/// (XFCE, most window managers, any session without a portal) it reports its
/// own default blue as if the host had chosen it. This source says "absent"
/// in that case, so the brand accent stands in, and carries the host's
/// colour otherwise.
///
/// Surfaces come from, by desktop family: the <c>kdeglobals</c> colour groups
/// the KDE portal exposes; libadwaita's named colours plus the user's
/// <c>gtk-4.0/gtk.css</c> overrides on GNOME; COSMIC's theme files; and, best
/// effort, the selected theme's <c>gtk-3.0/gtk.css</c> on the older GTK
/// desktops. A desktop that publishes nothing leaves the mapper's own
/// palette in charge.
/// </summary>
internal sealed class LinuxSystemPaletteSource : IHostSystemPaletteSource, IDisposable
{
    internal const string AppearanceNamespace = "org.freedesktop.appearance";
    internal const string AccentColorKey = "accent-color";
    internal const string ColorSchemeKey = "color-scheme";
    internal const string GnomeInterfaceNamespace = "org.gnome.desktop.interface";
    internal const string GtkThemeKey = "gtk-theme";

    private readonly object _lifetimeGate = new();
    private readonly ILinuxPortalSettingsClient _client;
    private readonly LinuxDesktopFiles _files;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IDisposable? _subscription;
    private FileSystemWatcher? _watcher;
    private bool _stopped;

    public LinuxSystemPaletteSource()
        : this(new LinuxPortalSettingsClient(), LinuxDesktopFiles.FromEnvironment())
    {
    }

    internal LinuxSystemPaletteSource(ILinuxPortalSettingsClient client, LinuxDesktopFiles files)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public HostSystemPalette Current { get; private set; } = HostSystemPalette.None;

    public event EventHandler? Changed;

    internal Task Initialization { get; private set; } = Task.CompletedTask;

    public void Start() => Initialization = InitializeAsync();

    public void Refresh() => _ = RefreshAfterSignalAsync();

    public void Stop()
    {
        IDisposable? subscription;
        FileSystemWatcher? watcher;
        lock (_lifetimeGate)
        {
            _stopped = true;
            subscription = _subscription;
            _subscription = null;
            watcher = _watcher;
            _watcher = null;
            Changed = null;
        }

        subscription?.Dispose();
        watcher?.Dispose();
        _client.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _refreshGate.Dispose();
    }

    /// <summary>
    /// The portal's accent: a struct of three doubles in [0, 1]. Anything else
    /// is "unset" by the portal's own contract.
    /// </summary>
    internal static Color? ParseAccent(VariantValue value)
    {
        try
        {
            var red = value.GetItem(0).GetDouble();
            var green = value.GetItem(1).GetDouble();
            var blue = value.GetItem(2).GetDouble();
            if (!IsUnit(red) || !IsUnit(green) || !IsUnit(blue))
            {
                return null;
            }

            return Color.FromRgb(ToByte(red), ToByte(green), ToByte(blue));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentOutOfRangeException
            or IndexOutOfRangeException
            or InvalidCastException)
        {
            return null;
        }

        static bool IsUnit(double channel) => channel is >= 0 and <= 1;

        static byte ToByte(double channel) => (byte)Math.Round(channel * 255);
    }

    /// <summary>The portal's colour scheme: 1 is dark, 2 is light, 0 is no preference.</summary>
    internal static bool? ParsePrefersDark(VariantValue value)
    {
        try
        {
            return value.GetUInt32() switch
            {
                1 => true,
                2 => false,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// The palette for one reading of the desktop. Pure, so the decision per
    /// desktop family can be tested without a bus or a file system.
    /// </summary>
    internal static HostSystemPalette Compose(
        LinuxDesktopFamily family,
        Color? accent,
        bool? prefersDark,
        string? gtkTheme,
        string? gtk4Overrides,
        IReadOnlyDictionary<string, string>? kdeColors,
        string? gtk3ThemeCss,
        CosmicThemeFiles? cosmic)
    {
        HostSemanticColors? light = null;
        HostSemanticColors? dark = null;
        bool? themePrefersDark = null;
        // Without a portal accent, the theme's own selection colour is what the
        // desktop's other apps highlight with, so it is the accent to follow.
        Color? themeAccent = null;
        switch (family)
        {
            case LinuxDesktopFamily.Kde:
                if (kdeColors is not null && KdeGlobalsColors.Resolve(kdeColors) is { } kde)
                {
                    // One scheme at a time: Plasma switches schemes wholesale,
                    // and the scheme in use is the preference when the portal
                    // states none.
                    if (KdeGlobalsColors.IsDark(kde))
                    {
                        dark = kde;
                        themePrefersDark = true;
                    }
                    else
                    {
                        light = kde;
                        themePrefersDark = false;
                    }

                    themeAccent = kde.SelectedContentBackground;
                }

                break;
            case LinuxDesktopFamily.Cosmic:
                light = CosmicThemeColors.Resolve(cosmic?.LightBackground, cosmic?.LightPrimary, cosmic?.LightAccent, accent);
                dark = CosmicThemeColors.Resolve(cosmic?.DarkBackground, cosmic?.DarkPrimary, cosmic?.DarkAccent, accent);
                break;
            case LinuxDesktopFamily.Gtk3:
                if (gtk3ThemeCss is not null && Gtk3ThemeColors.Resolve(gtk3ThemeCss, accent) is { } gtk3)
                {
                    // A GTK 3 desktop has no scheme switch of its own; a dark
                    // theme is the user's way of asking for dark.
                    if (KdeGlobalsColors.IsDark(gtk3))
                    {
                        dark = gtk3;
                        themePrefersDark = true;
                    }
                    else
                    {
                        light = gtk3;
                        themePrefersDark = false;
                    }

                    themeAccent = gtk3.SelectedContentBackground;
                }

                break;
            case LinuxDesktopFamily.Gnome:
                // GNOME before 47 has no accent key; its apps are Adwaita blue,
                // or Yaru orange on Ubuntu.
                themeAccent = AdwaitaColors.IsYaru(gtkTheme) ? AdwaitaColors.YaruAccent : AdwaitaColors.DefaultAccent;
                light = AdwaitaColors.Resolve(dark: false, gtkTheme, gtk4Overrides, accent ?? themeAccent);
                dark = AdwaitaColors.Resolve(dark: true, gtkTheme, gtk4Overrides, accent ?? themeAccent);
                break;
            case LinuxDesktopFamily.Unknown:
                // GTK 4 apps on any other desktop draw Adwaita, but only a
                // desktop that states a scheme is known to have one.
                if (prefersDark is not null)
                {
                    light = AdwaitaColors.Resolve(dark: false, gtkTheme, gtk4Overrides, accent);
                    dark = AdwaitaColors.Resolve(dark: true, gtkTheme, gtk4Overrides, accent);
                }

                break;
        }

        var effectiveAccent = accent ?? themeAccent;
        return new HostSystemPalette(
            light,
            dark,
            AccentIsMulticolor: false,
            Accent: effectiveAccent,
            AccentIsAbsent: effectiveAccent is null,
            PrefersDark: prefersDark ?? themePrefersDark);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var subscription = await _client.WatchSettingChangedAsync(OnSettingChanged);
            lock (_lifetimeGate)
            {
                if (_stopped)
                {
                    subscription.Dispose();
                    return;
                }

                _subscription = subscription;
            }
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // No portal: the files alone still describe some desktops.
        }

        StartWatchingFiles();
        await RefreshAfterSignalAsync();
    }

    private void StartWatchingFiles()
    {
        var directory = _files.WatchDirectory;
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            lock (_lifetimeGate)
            {
                if (_stopped)
                {
                    watcher.Dispose();
                    return;
                }

                _watcher = watcher;
            }

            watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (IsUnavailable(exception) || exception is ArgumentException)
        {
            // Watching is a convenience; the next portal signal refreshes too.
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_files.IsRelevantChange(e.FullPath))
        {
            _ = RefreshAfterSignalAsync();
        }
    }

    private void OnSettingChanged(Exception? exception, LinuxPortalSettingChanged setting)
    {
        if (exception is not null)
        {
            return;
        }

        var relevant = string.Equals(setting.Namespace, AppearanceNamespace, StringComparison.Ordinal)
            || string.Equals(setting.Namespace, GnomeInterfaceNamespace, StringComparison.Ordinal)
            || setting.Namespace.StartsWith(KdeGlobalsColors.PortalNamespacePrefix, StringComparison.Ordinal);
        if (relevant)
        {
            _ = RefreshAfterSignalAsync();
        }
    }

    private async Task RefreshAfterSignalAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // Keep the last answer until the desktop speaks again.
        }
    }

    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            if (_stopped)
            {
                return;
            }

            Color? accent = null;
            bool? prefersDark = null;
            string? gtkTheme = null;
            Dictionary<string, string>? kdeColors = null;
            uint? version = null;
            try
            {
                version = await _client.GetVersionAsync();
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                // No portal; every read below is skipped.
            }

            if (version is { } portalVersion)
            {
                accent = await TryReadAsync(AppearanceNamespace, AccentColorKey, portalVersion, ParseAccent);
                prefersDark = await TryReadAsync(AppearanceNamespace, ColorSchemeKey, portalVersion, ParsePrefersDark);
                gtkTheme = await TryReadAsync(GnomeInterfaceNamespace, GtkThemeKey, portalVersion, ReadString);
                if (_files.Family == LinuxDesktopFamily.Kde)
                {
                    kdeColors = [];
                    foreach (var (group, key) in KdeGlobalsColors.Keys)
                    {
                        var value = await TryReadAsync(KdeGlobalsColors.PortalNamespacePrefix + group, key, portalVersion, ReadString);
                        if (value is not null)
                        {
                            kdeColors[group + "/" + key] = value;
                        }
                    }
                }
            }

            var gtkThemeName = gtkTheme ?? _files.Gtk3ThemeName();
            Publish(Compose(
                _files.Family,
                accent,
                prefersDark,
                gtkThemeName,
                _files.Gtk4Overrides(),
                kdeColors,
                _files.Family == LinuxDesktopFamily.Gtk3 ? _files.Gtk3ThemeCss(gtkThemeName) : null,
                _files.Family == LinuxDesktopFamily.Cosmic ? _files.Cosmic() : null));
        }
        finally
        {
            try
            {
                _refreshGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while a refresh was in flight; nothing left to release.
            }
        }
    }

    private async Task<T?> TryReadAsync<T>(
        string settingNamespace,
        string key,
        uint version,
        Func<VariantValue, T?> parse)
    {
        try
        {
            var value = await _client.ReadAsync(settingNamespace, key, version);
            return parse(value);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return default;
        }
    }

    private static string? ReadString(VariantValue value)
    {
        try
        {
            return value.GetString();
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidCastException)
        {
            return null;
        }
    }

    private void Publish(HostSystemPalette next)
    {
        EventHandler? changed;
        lock (_lifetimeGate)
        {
            if (_stopped || Current == next)
            {
                return;
            }

            Current = next;
            changed = Changed;
        }

        changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsUnavailable(Exception exception) => exception is
        DBusConnectionException
        or DBusErrorReplyException
        or IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or ObjectDisposedException
        or OperationCanceledException
        or TimeoutException
        or ExternalException;
}

/// <summary>Which desktop family publishes the colours, decided from the session's desktop names.</summary>
internal enum LinuxDesktopFamily
{
    Unknown,
    Gnome,
    Kde,
    Gtk3,
    Cosmic,
}

/// <summary>The COSMIC theme parts Asura reads, as their RON text.</summary>
internal sealed record CosmicThemeFiles(
    string? LightBackground,
    string? LightPrimary,
    string? LightAccent,
    string? DarkBackground,
    string? DarkPrimary,
    string? DarkAccent);

/// <summary>
/// The desktop's files and names: where the user's overrides, theme and
/// COSMIC parts live for this session. Reads are best effort and never throw.
/// </summary>
internal sealed class LinuxDesktopFiles
{
    private readonly string _home;
    private readonly string _configHome;
    private readonly string? _dataHome;
    private readonly string? _dataDirs;
    private readonly Func<string, string?> _read;

    internal LinuxDesktopFiles(
        LinuxDesktopFamily family,
        string home,
        string configHome,
        string? dataHome,
        string? dataDirs,
        Func<string, string?> read)
    {
        Family = family;
        _home = home ?? throw new ArgumentNullException(nameof(home));
        _configHome = configHome ?? throw new ArgumentNullException(nameof(configHome));
        _dataHome = dataHome;
        _dataDirs = dataDirs;
        _read = read ?? throw new ArgumentNullException(nameof(read));
    }

    public LinuxDesktopFamily Family { get; }

    /// <summary>The configuration root, watched for theme and override changes.</summary>
    public string? WatchDirectory => Directory.Exists(_configHome) ? _configHome : null;

    public static LinuxDesktopFiles FromEnvironment()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(home, ".config");
        return new LinuxDesktopFiles(
            DetectFamily(
                Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
                Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
                Environment.GetEnvironmentVariable("DESKTOP_SESSION")),
            home,
            configHome,
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetEnvironmentVariable("XDG_DATA_DIRS"),
            ReadFile);
    }

    internal static LinuxDesktopFamily DetectFamily(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var description = string.Join(':', values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (description.Contains("KDE", StringComparison.OrdinalIgnoreCase)
            || description.Contains("PLASMA", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxDesktopFamily.Kde;
        }

        if (description.Contains("COSMIC", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxDesktopFamily.Cosmic;
        }

        if (description.Contains("GNOME", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxDesktopFamily.Gnome;
        }

        if (description.Contains("XFCE", StringComparison.OrdinalIgnoreCase)
            || description.Contains("CINNAMON", StringComparison.OrdinalIgnoreCase)
            || description.Contains("MATE", StringComparison.OrdinalIgnoreCase)
            || description.Contains("BUDGIE", StringComparison.OrdinalIgnoreCase)
            || description.Contains("PANTHEON", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxDesktopFamily.Gtk3;
        }

        return LinuxDesktopFamily.Unknown;
    }

    public bool IsRelevantChange(string path) =>
        path.Contains("gtk-4.0", StringComparison.Ordinal)
        || path.Contains("gtk-3.0", StringComparison.Ordinal)
        || path.Contains("cosmic", StringComparison.Ordinal)
        || path.Contains("xsettings.xml", StringComparison.Ordinal)
        || path.EndsWith("kdeglobals", StringComparison.Ordinal);

    public string? Gtk4Overrides() => _read(Path.Combine(_configHome, AdwaitaColors.UserOverrideRelativePath));

    /// <summary>The GTK 3 theme name from XFCE's xsettings or the GTK settings file.</summary>
    public string? Gtk3ThemeName()
    {
        var xfce = _read(Path.Combine(_configHome, "xfce4", "xfconf", "xfce-perchannel-xml", "xsettings.xml"));
        if (xfce is not null && Gtk3ThemeColors.ThemeFromXfceXsettings(xfce) is { } fromXfce)
        {
            return fromXfce;
        }

        var ini = _read(Path.Combine(_configHome, "gtk-3.0", "settings.ini"));
        return ini is null ? null : Gtk3ThemeColors.ThemeFromSettingsIni(ini);
    }

    public string? Gtk3ThemeCss(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return null;
        }

        foreach (var candidate in Gtk3ThemeColors.CandidateStylesheets(theme, _home, _dataHome, _dataDirs))
        {
            if (_read(candidate) is { } css)
            {
                return css;
            }
        }

        return null;
    }

    public CosmicThemeFiles Cosmic() => new(
        _read(Path.Combine(_configHome, CosmicThemeColors.LightThemeDirectory, "background")),
        _read(Path.Combine(_configHome, CosmicThemeColors.LightThemeDirectory, "primary")),
        _read(Path.Combine(_configHome, CosmicThemeColors.LightThemeDirectory, "accent")),
        _read(Path.Combine(_configHome, CosmicThemeColors.DarkThemeDirectory, "background")),
        _read(Path.Combine(_configHome, CosmicThemeColors.DarkThemeDirectory, "primary")),
        _read(Path.Combine(_configHome, CosmicThemeColors.DarkThemeDirectory, "accent")));

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
