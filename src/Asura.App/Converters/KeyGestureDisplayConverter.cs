using System.Globalization;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Data.Converters;
using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using CoreKeyModifiers = Asura.Core.KeyModifiers;

namespace Asura.App.Converters;

/// <summary>
/// Writes a menu item's gesture the way every other shortcut in the shell is
/// written. The stock menu printed Avalonia's own notation beside context-menu
/// items — "Cmd+Down Arrow", "Alt+Cmd+R", "Backspace" — where a Mac menu, and
/// the menu bar above it, write ⌘↓, ⌥⌘R and ⌘⌫.
/// </summary>
public sealed class KeyGestureDisplayConverter : IValueConverter
{
    public static KeyGestureDisplayConverter Instance { get; } = new();

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is KeyGesture gesture ? Format(gesture) : null;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException("A shortcut's display text is one-way.");

    /// <summary>The gesture in this host's notation, or empty for no key.</summary>
    public static string Format(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        return gesture.Key == Key.None
            ? string.Empty
            : KeySequenceDisplay.Format(ToStroke(gesture));
    }

    internal static KeyStroke ToStroke(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var modifiers = CoreKeyModifiers.None;
        if ((gesture.KeyModifiers & AvaloniaKeyModifiers.Control) != AvaloniaKeyModifiers.None)
        {
            modifiers |= CoreKeyModifiers.Control;
        }

        if ((gesture.KeyModifiers & AvaloniaKeyModifiers.Alt) != AvaloniaKeyModifiers.None)
        {
            modifiers |= CoreKeyModifiers.Alt;
        }

        if ((gesture.KeyModifiers & AvaloniaKeyModifiers.Shift) != AvaloniaKeyModifiers.None)
        {
            modifiers |= CoreKeyModifiers.Shift;
        }

        if ((gesture.KeyModifiers & AvaloniaKeyModifiers.Meta) != AvaloniaKeyModifiers.None)
        {
            modifiers |= CoreKeyModifiers.Meta;
        }

        return new KeyStroke(KeyName(gesture.Key), modifiers);
    }

    /// <summary>
    /// The key as the shortcut notation names it. Digits drop Avalonia's "D"
    /// prefix; everything else goes through the canonical name, which turns
    /// "Back" into Backspace and "OemComma" into a comma.
    /// </summary>
    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
        Key.Enter => "ENTER",
        _ => ShortcutText.CanonicalKey(key.ToString()),
    };
}
