using System.Runtime.InteropServices;
using Asura.App;
using Avalonia.Media;
using Tmds.DBus.Protocol;

namespace Asura.Desktop;

/// <summary>
/// The Linux host's accent, read from the settings portal's
/// <c>org.freedesktop.appearance accent-color</c> and watched for changes.
///
/// Avalonia reads the same key, but when a desktop has none (XFCE, most
/// window managers, any session without a portal) it reports its own default
/// blue as if the host had chosen it. This source says "absent" in that case,
/// so the brand accent stands in, and carries the host's colour otherwise.
/// </summary>
internal sealed class LinuxSystemPaletteSource : IHostSystemPaletteSource, IDisposable
{
    internal const string AppearanceNamespace = "org.freedesktop.appearance";
    internal const string AccentColorKey = "accent-color";

    private readonly object _lifetimeGate = new();
    private readonly ILinuxPortalSettingsClient _client;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IDisposable? _subscription;
    private bool _stopped;

    public LinuxSystemPaletteSource()
        : this(new LinuxPortalSettingsClient())
    {
    }

    internal LinuxSystemPaletteSource(ILinuxPortalSettingsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public HostSystemPalette Current { get; private set; } = HostSystemPalette.None;

    public event EventHandler? Changed;

    internal Task Initialization { get; private set; } = Task.CompletedTask;

    public void Start() => Initialization = InitializeAsync();

    public void Refresh() => _ = RefreshAfterSignalAsync();

    public void Stop()
    {
        IDisposable? subscription;
        lock (_lifetimeGate)
        {
            _stopped = true;
            subscription = _subscription;
            _subscription = null;
            Changed = null;
        }

        subscription?.Dispose();
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

            await RefreshAsync();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // No portal at all is the same answer as a portal without an accent.
            Publish(HostSystemPalette.None with { AccentIsAbsent = true });
        }
    }

    private void OnSettingChanged(Exception? exception, LinuxPortalSettingChanged setting)
    {
        if (exception is not null
            || !string.Equals(setting.Namespace, AppearanceNamespace, StringComparison.Ordinal)
            || !string.Equals(setting.Key, AccentColorKey, StringComparison.Ordinal))
        {
            return;
        }

        _ = RefreshAfterSignalAsync();
    }

    private async Task RefreshAfterSignalAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // Keep the last answer until the portal speaks again.
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

            Color? accent;
            try
            {
                var version = await _client.GetVersionAsync();
                var value = await _client.ReadAsync(AppearanceNamespace, AccentColorKey, version);
                accent = ParseAccent(value);
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                accent = null;
            }

            Publish(accent is { } color
                ? HostSystemPalette.None with { Accent = color }
                : HostSystemPalette.None with { AccentIsAbsent = true });
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
