using System.Collections.Immutable;
using System.ComponentModel;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Tooltips for toolbar buttons whose command also has a menu-bar shortcut.
/// The tooltip names the shortcut the menu shows, found the way the menu
/// finds it, so a rebound command's button names the new key. Hosts without
/// those menu shortcuts show the label.
/// </summary>
public sealed class ShortcutTips : INotifyPropertyChanged
{
    private Func<CommandId, KeyStroke?> _resolve = DefaultResolver();

    private ShortcutTips()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // One set of tips per UI thread. Bindings update only on the thread that
    // owns their controls, so a change is raised to the toolbars of the
    // thread that made it. The app has one UI thread and so one set; each
    // headless test session has its own.
    [ThreadStatic]
    private static ShortcutTips? t_current;

    /// <summary>The tips for the active keymaps; bound by every toolbar.</summary>
    public static ShortcutTips Current => t_current ??= new();

    public string NewTab => Tip("New tab", BuiltInCommands.NewTab);

    public string CloseTab => Tip("Close tab", BuiltInCommands.CloseTab);

    public string ToggleAgentPanel => Tip("Show or hide the agent panel", BuiltInCommands.ToggleAgentPanel);

    public string AddPanel => Tip("Add panel", BuiltInCommands.AddPanel);

    public static string RunStatement { get; } = KeySequenceDisplay.WithShortcut(
        "Run the statement",
        new KeyStroke(
            "ENTER",
            HostPlatform.Current == HostOperatingSystem.MacOS ? KeyModifiers.Meta : KeyModifiers.Control));

    /// <summary>
    /// Reads shortcuts from the keymaps now in use. A window calls this when it
    /// opens and when the application keymap changes, so the tooltips name the
    /// saved keymap's keys and follow a rebinding. Only tips whose text changed
    /// are announced.
    /// </summary>
    internal void Update(MenuShortcutResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (HostPlatform.Current != HostOperatingSystem.MacOS)
        {
            return;
        }

        var before = Tips();
        _resolve = command => resolver.Resolve(command, ImmutableDictionary<string, string>.Empty);
        var after = Tips();
        for (var index = 0; index < after.Length; index++)
        {
            if (!string.Equals(before[index].Text, after[index].Text, StringComparison.Ordinal))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(after[index].Name));
            }
        }
    }

    private (string Name, string Text)[] Tips() =>
    [
        (nameof(NewTab), NewTab),
        (nameof(CloseTab), CloseTab),
        (nameof(ToggleAgentPanel), ToggleAgentPanel),
        (nameof(AddPanel), AddPanel),
    ];

    private string Tip(string label, CommandId command) =>
        KeySequenceDisplay.WithShortcut(label, _resolve(command));

    private static Func<CommandId, KeyStroke?> DefaultResolver()
    {
        if (HostPlatform.Current != HostOperatingSystem.MacOS)
        {
            return static _ => null;
        }

        var resolver = new MenuShortcutResolver(
            BuiltInKeymaps.ApplicationFor(HostOperatingSystem.MacOS).Bindings,
            BuiltInKeymaps.TerminalFor(HostOperatingSystem.MacOS).Bindings);
        return command => resolver.Resolve(command, ImmutableDictionary<string, string>.Empty);
    }
}
