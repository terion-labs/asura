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
/// did nothing. The page still sees the original key first; only when
/// neither it nor Chromium used the key, and a text field has focus, is
/// the command performed here, with what Chromium does understand: Home and
/// End move within the line, and selecting everything and collapsing it
/// reaches the start or end of the text, in whichever frame has focus.
/// </summary>
internal sealed class CefMacTextNavigation
{
    private const int LeftKey = 0x25;
    private const int UpKey = 0x26;
    private const int RightKey = 0x27;
    private const int DownKey = 0x28;
    private const int BackspaceKey = 0x08;
    private const int ForwardDeleteKey = 0x2E;

    private volatile bool _editing;

    /// <summary>
    /// Chromium reports a text-input mode while an editable field has focus,
    /// and none when it loses it.
    /// </summary>
    public void OnTextInputModeChanged(Cef.CefTextInputMode mode) =>
        _editing = mode != Cef.CefTextInputMode.None;

    public void Reset() => _editing = false;

    /// <summary>
    /// Performs the editing command for a key that the page and Chromium both
    /// left unhandled. Returns whether it did.
    /// </summary>
    public bool TryPerform(CefBrowser browser, PreKeyEventArgs key)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(key);
        const Cef.CefModifiers other = Cef.CefModifiers.Control | Cef.CefModifiers.Alt;
        if (!_editing
            || key.Type is not (Cef.CefKeyEventType.RawKeyDown or Cef.CefKeyEventType.KeyDown)
            || (key.Modifiers & Cef.CefModifiers.Command) == 0
            || (key.Modifiers & other) != 0)
        {
            return false;
        }

        var shift = (key.Modifiers & Cef.CefModifiers.Shift) != 0;
        switch (key.WindowsKeyCode)
        {
            case LeftKey:
                Press(browser, Home, shift);
                return true;
            case RightKey:
                Press(browser, End, shift);
                return true;
            case UpKey or DownKey when !shift:
                // Selecting the field's whole text and collapsing the
                // selection lands at its start or end, in the focused frame.
                browser.SelectAll();
                Press(browser, key.WindowsKeyCode == UpKey ? Left : Right, shift: false);
                return true;
            case UpKey or DownKey:
                ExtendToTextBoundary(browser, backward: key.WindowsKeyCode == UpKey);
                return true;
            case BackspaceKey:
                Press(browser, Home, shift: true);
                Press(browser, Backspace, shift: false);
                return true;
            case ForwardDeleteKey:
                Press(browser, End, shift: true);
                Press(browser, ForwardDelete, shift: false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Extends the selection to the start or end of the text. No key does
    /// this on macOS, so the focused document's selection is moved, through
    /// same-origin frames; a cross-origin frame is left alone.
    /// </summary>
    private static void ExtendToTextBoundary(CefBrowser browser, bool backward) =>
        browser.ExecuteJavaScript(
            "(() => { let d = document;"
            + " while (d.activeElement && d.activeElement.tagName === 'IFRAME') {"
            + " const inner = d.activeElement.contentDocument; if (!inner) return; d = inner; }"
            + $" d.getSelection().modify('extend', '{(backward ? "backward" : "forward")}', 'documentboundary'); }})()");

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
    private static readonly NavigationKey Left = new(LeftKey, 0x7B, '');
    private static readonly NavigationKey Right = new(RightKey, 0x7C, '');
    private static readonly NavigationKey Backspace = new(BackspaceKey, 0x33, '\x7F');
    private static readonly NavigationKey ForwardDelete = new(ForwardDeleteKey, 0x75, '');
}
