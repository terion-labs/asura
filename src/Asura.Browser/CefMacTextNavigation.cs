using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Exclr8Cef;

namespace Asura.Browser;

/// <summary>
/// The Command-key text navigation a Mac user expects in a web form:
/// Command-Left and Command-Right to the start and end of the line,
/// Command-Up and Command-Down to the start and end of the text, with Shift
/// to select, and Command-Delete to delete to the start of the line.
///
/// Chrome gets these from Cocoa as editing commands (moveToBeginningOfLine:
/// and the like); Chromium's own key table has no Command-arrow entries on
/// macOS. An off-screen browser receives no editing commands, so the keys
/// did nothing. While a text field has focus they are performed here, as
/// the keys Chromium does understand: Home and End move within the line.
/// Nothing reaches the beginning or end of the text, so those use the
/// selection's documentboundary movement.
/// </summary>
internal sealed class CefMacTextNavigation
{
    private readonly Func<CefBrowser?> _browser;
    private readonly HashSet<Key> _translated = [];
    private volatile bool _editing;

    public CefMacTextNavigation(Control host, Func<CefBrowser?> browser)
    {
        ArgumentNullException.ThrowIfNull(host);
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        host.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Chromium reports a text-input mode while an editable field has focus,
    /// and none when it loses it.
    /// </summary>
    public void OnTextInputModeChanged(Cef.CefTextInputMode mode) =>
        _editing = mode != Cef.CefTextInputMode.None;

    public void Reset()
    {
        _editing = false;
        _translated.Clear();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The physical-input gate above decides first; a held Control or
        // Option is another shortcut.
        if (e.Handled
            || !_editing
            || (e.KeyModifiers & KeyModifiers.Meta) == 0
            || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0
            || _browser() is not { IsInitialized: true } browser)
        {
            return;
        }

        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (e.Key)
        {
            case Key.Left:
                Press(browser, Home, shift);
                break;
            case Key.Right:
                Press(browser, End, shift);
                break;
            case Key.Up:
            case Key.Down:
                browser.ExecuteJavaScript(
                    $"getSelection().modify('{(shift ? "extend" : "move")}', "
                    + $"'{(e.Key == Key.Up ? "backward" : "forward")}', 'documentboundary')");
                break;
            case Key.Back:
                Press(browser, Home, shift: true);
                Press(browser, Backspace, shift: false);
                break;
            case Key.Delete:
                Press(browser, End, shift: true);
                Press(browser, ForwardDelete, shift: false);
                break;
            default:
                return;
        }

        _translated.Add(e.Key);
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        // The release of a key performed here is not the page's to see.
        if (_translated.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private static void Press(CefBrowser browser, NavigationKey key, bool shift)
    {
        var modifiers = shift ? Cef.CefModifiers.Shift : Cef.CefModifiers.None;
        browser.SendKeyEvent(Cef.CefKeyEventType.RawKeyDown, key.WindowsCode, key.MacCode,
            modifiers, key.Character, key.Character, isSystemKey: false);
        browser.SendKeyEvent(Cef.CefKeyEventType.KeyUp, key.WindowsCode, key.MacCode,
            modifiers, key.Character, key.Character, isSystemKey: false);
    }

    // Windows virtual-key code, Carbon key code, and the Cocoa character
    // Chromium's macOS key translation expects on both edges.
    private readonly record struct NavigationKey(int WindowsCode, int MacCode, char Character);

    private static readonly NavigationKey Home = new(0x24, 0x73, '');
    private static readonly NavigationKey End = new(0x23, 0x77, '');
    private static readonly NavigationKey Backspace = new(0x08, 0x33, '\x7F');
    private static readonly NavigationKey ForwardDelete = new(0x2E, 0x75, '');
}
