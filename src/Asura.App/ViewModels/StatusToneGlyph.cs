using FluentIcons.Common;

namespace Asura.App.ViewModels;

/// <summary>
/// The shape a status mark takes when colour cannot carry it.
///
/// A status dot is green, amber, red or gray and nothing else, so with
/// Differentiate Without Color on — or for anyone who cannot tell those apart
/// — every state looks the same. Each tone has its own glyph as well, and a
/// view shows the glyph in place of the dot while the window wears the
/// <c>differentiate-without-color</c> class.
/// </summary>
public static class StatusToneGlyph
{
    public static Symbol For(StatusTone tone) => tone switch
    {
        StatusTone.Success => Symbol.CheckmarkCircle,
        StatusTone.Warning => Symbol.Warning,
        StatusTone.Danger => Symbol.ErrorCircle,
        _ => Symbol.Circle,
    };
}
