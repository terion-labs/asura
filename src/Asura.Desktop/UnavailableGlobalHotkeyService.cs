using Asura.Application;
using Asura.Core;

namespace Asura.Desktop;

internal enum GlobalHotkeyUnavailableReason
{
    UnsupportedPlatform,
    Wayland,
    MissingX11Display,
}

internal sealed class UnavailableGlobalHotkeyService : IGlobalHotkeyService
{
    private readonly GlobalHotkeyUnavailableReason _reason;

    public UnavailableGlobalHotkeyService(
        GlobalHotkeyUnavailableReason reason = GlobalHotkeyUnavailableReason.UnsupportedPlatform)
    {
        _reason = reason;
    }

    public event EventHandler? Pressed
    {
        add { }
        remove { }
    }

    public KeyStroke? RegisteredGesture => null;

    public GlobalHotkeyRegistrationResult Register(KeyStroke gesture)
    {
        _ = gesture;
        return Failure();
    }

    public void Unregister()
    {
    }

    public void Dispose()
    {
    }

    private GlobalHotkeyRegistrationResult Failure()
    {
        var (stableCode, message) = _reason switch
        {
            GlobalHotkeyUnavailableReason.Wayland => (
                "global_hotkey_wayland_unsupported",
                "Global Quick Terminal shortcuts require an X11 session; this Wayland compositor does not expose a safe global-shortcut protocol to Asura."),
            GlobalHotkeyUnavailableReason.MissingX11Display => (
                "global_hotkey_x11_display_unavailable",
                "Global Quick Terminal shortcuts require an accessible X11 DISPLAY."),
            _ => (
                "global_hotkey_unsupported",
                "Global Quick Terminal shortcuts are not available on this desktop backend."),
        };

        return new GlobalHotkeyRegistrationResult.Failure(new(
            GlobalHotkeyRegistrationErrorCode.Unsupported,
            stableCode,
            message));
    }
}
