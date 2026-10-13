using Avalonia.Controls;

namespace Asura.App.Views;

/// <summary>
/// The one way a window asks for a transparency level.
///
/// Avalonia.Native 12.0.5 walks a hint list skipping any level that is
/// already active, so handing it the active list again selects the next
/// fallback: a window on AcrylicBlur that re-asks for
/// [AcrylicBlur, Blur, Transparent] drops to Transparent, with the material
/// hidden and the window's backing cleared. Asked again it is back on the
/// blur, so every replay flips the window between the two. That is how the
/// Settings sidebar went fully clear after an appearance republish.
/// </summary>
internal static class WindowTransparencyHint
{
    /// <summary>
    /// Asks <paramref name="window"/> for <paramref name="hint"/> unless it
    /// already has it. When the list is unchanged but the native mode drifted
    /// off its first choice, the request is made again to repair it.
    /// </summary>
    public static void Request(Window window, IReadOnlyList<WindowTransparencyLevel> hint)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(hint);
        if (window.TransparencyLevelHint.SequenceEqual(hint))
        {
            if (OperatingSystem.IsMacOS()
                && hint.Count > 0
                && window.ActualTransparencyLevel != hint[0])
            {
                // A new list notifies Avalonia even though its values match.
                window.TransparencyLevelHint = [.. hint];
            }

            return;
        }

        window.TransparencyLevelHint = hint;
    }
}
