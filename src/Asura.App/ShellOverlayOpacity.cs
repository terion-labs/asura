using Asura.Core;

namespace Asura.App;

/// <summary>
/// How solid the surfaces floating over the app's own content are painted:
/// the sidebar overlay, a raised overlay card, and the veil — and whether the
/// blurred snapshot under them is drawn at all.
///
/// They are glass only while the window has a material. Reduce Transparency,
/// Increase Contrast, and translucency turned off all leave the window
/// without one, and then every overlay is opaque and the blur goes, so
/// nothing behind an overlay competes with what is read on it.
/// </summary>
internal readonly record struct ShellOverlayOpacity(
    byte SidebarOverlay,
    byte SurfaceOverlay,
    byte Veil,
    bool BlurBehind)
{
    private static readonly ShellOverlayOpacity Opaque =
        new(byte.MaxValue, byte.MaxValue, byte.MaxValue, BlurBehind: false);

    public static ShellOverlayOpacity For(EffectiveTheme theme, byte storedOpacityAlpha)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.MaterialDisposition == MaterialDisposition.Enabled
            ? new(0xCC, storedOpacityAlpha, 0xA8, BlurBehind: true)
            : Opaque;
    }
}
