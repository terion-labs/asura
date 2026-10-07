using Asura.Core;
using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Asura.App.Views.Components;

/// <summary>
/// The database result grid's own key equivalents, one table per platform.
/// The context menu shows them and the grid's key handler answers them, so
/// the two cannot drift apart.
///
/// On a Mac the grid lives under a menu bar and an application keymap that
/// answer first: ⌘D is Panel ▸ Split Right there, so Duplicate Row has no key
/// equivalent at all rather than one the panel never receives. Windows and
/// Linux keep Ctrl+D, which nothing else claims on those hosts.
/// </summary>
internal static class DatabaseGridShortcuts
{
    /// <summary>The grid commands that carry a key.</summary>
    internal enum GridCommand
    {
        OpenCellEditor,
        CopyRow,
        CopyCell,
        RefreshPage,
        PasteCell,
        AddRow,
        DuplicateRow,
        DeleteRow,
    }

    /// <summary>
    /// One grid command and its key. <paramref name="MenuCommand"/> names the
    /// Edit menu command the grid carries out itself when the key is that
    /// command's own standard shortcut (⌘C copies, ⌘V pastes): the only way
    /// a grid key may equal a menu key.
    /// </summary>
    internal sealed record Entry(GridCommand Command, KeyGesture? Gesture, CommandId? MenuCommand = null);

    private const AvaloniaKeyModifiers Command = AvaloniaKeyModifiers.Meta;
    private const AvaloniaKeyModifiers Option = AvaloniaKeyModifiers.Alt;
    private const AvaloniaKeyModifiers Control = AvaloniaKeyModifiers.Control;
    private const AvaloniaKeyModifiers Shift = AvaloniaKeyModifiers.Shift;

    /// <summary>
    /// Add Row is ⌥⌘N: ⌘I is Get Info, a new row is a new thing, which is
    /// what N means, and Option keeps it clear of New Window. Refresh is ⌥⌘R
    /// for the same reason. Delete Row is the Delete key the Mac keyboard has.
    /// </summary>
    public static IReadOnlyList<Entry> MacOS { get; } =
    [
        new(GridCommand.OpenCellEditor, new KeyGesture(Key.Enter, Command)),
        new(GridCommand.CopyRow, new KeyGesture(Key.C, Command), BuiltInCommands.EditCopy),
        new(GridCommand.CopyCell, new KeyGesture(Key.C, Command | Shift)),
        new(GridCommand.RefreshPage, new KeyGesture(Key.R, Command | Option)),
        new(GridCommand.PasteCell, new KeyGesture(Key.V, Command), BuiltInCommands.EditPaste),
        new(GridCommand.AddRow, new KeyGesture(Key.N, Command | Option)),
        new(GridCommand.DuplicateRow, null),
        new(GridCommand.DeleteRow, new KeyGesture(Key.Back)),
    ];

    /// <summary>
    /// Add Row is Ctrl+Shift+N: Ctrl+Alt is AltGr on many layouts and types
    /// a character.
    /// </summary>
    public static IReadOnlyList<Entry> WindowsAndLinux { get; } =
    [
        new(GridCommand.OpenCellEditor, new KeyGesture(Key.Enter, Control)),
        new(GridCommand.CopyRow, new KeyGesture(Key.C, Control), BuiltInCommands.EditCopy),
        new(GridCommand.CopyCell, new KeyGesture(Key.C, Control | Shift)),
        new(GridCommand.RefreshPage, new KeyGesture(Key.R, Control)),
        new(GridCommand.PasteCell, new KeyGesture(Key.V, Control), BuiltInCommands.EditPaste),
        new(GridCommand.AddRow, new KeyGesture(Key.N, Control | Shift)),
        new(GridCommand.DuplicateRow, new KeyGesture(Key.D, Control)),
        new(GridCommand.DeleteRow, new KeyGesture(Key.Delete)),
    ];

    /// <summary>The table this process answers to.</summary>
    public static IReadOnlyList<Entry> Current { get; } = For(HostPlatform.Current);

    public static IReadOnlyList<Entry> For(HostOperatingSystem operatingSystem) =>
        operatingSystem == HostOperatingSystem.MacOS ? MacOS : WindowsAndLinux;

    /// <summary>The key a menu item shows for <paramref name="command"/>, or none.</summary>
    public static KeyGesture? GestureFor(GridCommand command) =>
        Current.First(entry => entry.Command == command).Gesture;

    /// <summary>
    /// Whether a key press is <paramref name="command"/>'s key. A command
    /// without a key matches nothing, so the press goes on to the keymap.
    /// </summary>
    public static bool Matches(GridCommand command, KeyEventArgs e) =>
        GestureFor(command)?.Matches(e) == true;
}
