using Asura.Core;
using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using CoreKeyModifiers = Asura.Core.KeyModifiers;

namespace Asura.App;

/// <summary>
/// Chooses the key equivalent each menu item shows. The active keymap wins:
/// a command it binds to one stroke shows that stroke. Commands the keymap
/// leaves alone, or binds only to a prefix sequence, show Apple's standard
/// shortcut from <see cref="MacOsMenuShortcuts"/>, unless the keymap already
/// gives that stroke to another command.
/// </summary>
/// <param name="applicationBindings">The active application keymap's bindings.</param>
/// <param name="terminalBindings">
/// The terminal keymap's bindings. They name terminal commands only, so they
/// supply strokes for those commands and never block another item's stroke.
/// </param>
/// <param name="extraShortcuts">
/// Strokes a host knows outside any keymap, such as the Quick Terminal hotkey.
/// </param>
internal sealed class MenuShortcutResolver(
    IReadOnlyList<CommandBinding> applicationBindings,
    IReadOnlyList<CommandBinding> terminalBindings,
    IReadOnlyDictionary<CommandId, KeyStroke>? extraShortcuts = null)
{
    public KeyStroke? Resolve(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (SingleStroke(applicationBindings, command, arguments) is { } applicationStroke)
        {
            return applicationStroke;
        }

        if (SingleStroke(terminalBindings, command, arguments) is { } terminalStroke)
        {
            return terminalStroke;
        }

        if (extraShortcuts?.TryGetValue(command, out var extra) == true)
        {
            return extra;
        }

        if (MacOsMenuShortcuts.For(command, arguments) is not { } standard)
        {
            return null;
        }

        var claimed = applicationBindings.Any(binding =>
            binding.Sequence.Count == 1
            && IsSameKey(binding.Sequence[0], standard)
            && !(binding.CommandId == command && SameArguments(binding.Arguments, arguments)));
        return claimed ? null : standard;
    }

    /// <summary>
    /// Keymaps name some keys by character and others by key cap, "[" and
    /// "OEMOPENBRACKETS" for one key; both press the same menu gesture.
    /// </summary>
    private static bool IsSameKey(KeyStroke left, KeyStroke right) =>
        left == right
        || (MenuKeyGestures.From(left) is { } leftGesture
            && leftGesture.Equals(MenuKeyGestures.From(right)));

    private static KeyStroke? SingleStroke(
        IReadOnlyList<CommandBinding> bindings,
        CommandId command,
        IReadOnlyDictionary<string, string> arguments)
    {
        foreach (var binding in bindings)
        {
            if (binding.CommandId == command
                && binding.Sequence.Count == 1
                && SameArguments(binding.Arguments, arguments))
            {
                return binding.Sequence[0];
            }
        }

        return null;
    }

    private static bool SameArguments(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(argument =>
            right.TryGetValue(argument.Key, out var value)
            && string.Equals(value, argument.Value, StringComparison.Ordinal));
}

/// <summary>
/// Converts keymap strokes into the gestures native menus display.
/// </summary>
internal static class MenuKeyGestures
{
    private static readonly Dictionary<string, Key> NamedKeys = new(StringComparer.Ordinal)
    {
        ["+"] = Key.Add,
        ["="] = Key.OemPlus,
        ["-"] = Key.OemMinus,
        [","] = Key.OemComma,
        ["."] = Key.OemPeriod,
        ["/"] = Key.OemQuestion,
        [";"] = Key.OemSemicolon,
        ["'"] = Key.OemQuotes,
        ["`"] = Key.OemTilde,
        ["["] = Key.OemOpenBrackets,
        ["]"] = Key.OemCloseBrackets,
        ["\\"] = Key.OemPipe,
        ["ARROWLEFT"] = Key.Left,
        ["ARROWRIGHT"] = Key.Right,
        ["ARROWUP"] = Key.Up,
        ["ARROWDOWN"] = Key.Down,
        ["ENTER"] = Key.Enter,
        ["RETURN"] = Key.Enter,
        ["BACKSPACE"] = Key.Back,
        ["ESCAPE"] = Key.Escape,
        ["ESC"] = Key.Escape,
        ["SPACE"] = Key.Space,
    };

    /// <summary>
    /// The menu gesture for a stroke, or null when a native menu cannot show
    /// it, for example a shifted symbol that only exists inside a prefix.
    /// </summary>
    public static KeyGesture? From(KeyStroke stroke)
    {
        if (!TryMapKey(stroke.Key, out var key))
        {
            return null;
        }

        var modifiers = AvaloniaKeyModifiers.None;
        if ((stroke.Modifiers & CoreKeyModifiers.Control) != CoreKeyModifiers.None)
        {
            modifiers |= AvaloniaKeyModifiers.Control;
        }

        if ((stroke.Modifiers & CoreKeyModifiers.Alt) != CoreKeyModifiers.None)
        {
            modifiers |= AvaloniaKeyModifiers.Alt;
        }

        if ((stroke.Modifiers & CoreKeyModifiers.Shift) != CoreKeyModifiers.None)
        {
            modifiers |= AvaloniaKeyModifiers.Shift;
        }

        if ((stroke.Modifiers & CoreKeyModifiers.Meta) != CoreKeyModifiers.None)
        {
            modifiers |= AvaloniaKeyModifiers.Meta;
        }

        return new KeyGesture(key, modifiers);
    }

    private static bool TryMapKey(string name, out Key key)
    {
        if (NamedKeys.TryGetValue(name, out key))
        {
            return true;
        }

        if (name.Length == 1 && char.IsAsciiLetterUpper(name[0]))
        {
            return Enum.TryParse(name, out key);
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return Enum.TryParse($"D{name}", out key);
        }

        // Avalonia key names such as F6, HOME or OEMOPENBRACKETS, which is how
        // the keymap recorder writes keys it has no symbol for.
        return name.Length > 1
            && char.IsAsciiLetter(name[0])
            && Enum.TryParse(name, ignoreCase: true, out key)
            && Enum.IsDefined(key)
            && key != Key.None;
    }
}
