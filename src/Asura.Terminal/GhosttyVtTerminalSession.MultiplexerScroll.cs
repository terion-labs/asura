using Asura.Application;
using Asura.Terminal.GhosttyVt;

namespace Asura.Terminal;

/// <summary>
/// Wheel scrolling in a managed SSH session's tmux history. Asura runs tmux
/// with its mouse mode off, so a drag selects text in Asura, not in tmux;
/// tmux then occupies the alternate screen without mouse reporting, where
/// the wheel had nothing to do. When Asura's startup script has bound two
/// private keys to tmux's own scrolling, it says so with a private OSC, and
/// a scroll in the alternate screen sends those keys instead.
/// </summary>
internal sealed partial class GhosttyVtTerminalSession
{
    /// <summary>Printed by Asura's tmux startup once the scroll keys are bound.</summary>
    internal static ReadOnlySpan<byte> MultiplexerScrollMarker =>
        "\u001b]6973;asura-multiplexer-scroll\u0007"u8;

    /// <summary>Private keys tmux binds to scrolling its history up and down.</summary>
    internal static ReadOnlySpan<byte> MultiplexerScrollUpKey => "\u001b[9101~"u8;

    internal static ReadOnlySpan<byte> MultiplexerScrollDownKey => "\u001b[9102~"u8;

    // tmux scrolls three lines for each key, as a wheel notch does locally.
    private const int MultiplexerLinesPerKey = 3;
    private const int MaximumMultiplexerKeysPerScroll = 64;

    private bool _multiplexerScrollKeys;
    private int _multiplexerMarkerMatched;

    /// <summary>Watches output for the marker, across reads.</summary>
    private void ObserveMultiplexerScrollMarkerUnsafe(ReadOnlySpan<byte> output)
    {
        if (_multiplexerScrollKeys)
        {
            return;
        }

        var marker = MultiplexerScrollMarker;
        foreach (var value in output)
        {
            if (value == marker[_multiplexerMarkerMatched])
            {
                if (++_multiplexerMarkerMatched == marker.Length)
                {
                    _multiplexerScrollKeys = true;
                    _multiplexerMarkerMatched = 0;
                    return;
                }
            }
            else
            {
                _multiplexerMarkerMatched = value == marker[0] ? 1 : 0;
            }
        }
    }

    /// <summary>
    /// The keys that scroll tmux's history for this scroll, or null when the
    /// scroll is the local scrollback's: no managed tmux, the primary screen,
    /// or an application that asked for the mouse itself.
    /// </summary>
    private unsafe byte[]? MultiplexerScrollKeysUnsafe(TerminalViewportScrollInput scrollInput)
    {
        if (!_multiplexerScrollKeys
            || scrollInput.Direction is not (TerminalViewportScrollDirection.Up or TerminalViewportScrollDirection.Down))
        {
            return null;
        }

        GhosttyVtTerminalScreen activeScreen = GhosttyVtTerminalScreen.Primary;
        byte mouseTracking = 0;
        if (GhosttyVtNative.TerminalGet(_terminal, GhosttyVtTerminalData.ActiveScreen, &activeScreen)
                != GhosttyVtResult.Success
            || GhosttyVtNative.TerminalGet(_terminal, GhosttyVtTerminalData.MouseTracking, &mouseTracking)
                != GhosttyVtResult.Success
            || activeScreen != GhosttyVtTerminalScreen.Alternate
            || mouseTracking != 0)
        {
            return null;
        }

        var lines = scrollInput.Unit == TerminalViewportScrollUnit.Page
            ? (long)scrollInput.Amount * Math.Max(1, _rows - 1)
            : scrollInput.Amount;
        var presses = (int)Math.Clamp(
            (lines + MultiplexerLinesPerKey - 1) / MultiplexerLinesPerKey,
            1,
            MaximumMultiplexerKeysPerScroll);
        var key = scrollInput.Direction == TerminalViewportScrollDirection.Up
            ? MultiplexerScrollUpKey
            : MultiplexerScrollDownKey;
        var keys = new byte[key.Length * presses];
        for (var index = 0; index < presses; index++)
        {
            key.CopyTo(keys.AsSpan(index * key.Length));
        }

        return keys;
    }
}
