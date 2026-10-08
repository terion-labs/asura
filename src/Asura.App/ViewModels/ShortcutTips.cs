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

    /// <summary>The tips for the active keymaps; bound by every toolbar.</summary>
    public static ShortcutTips Current { get; } = new();

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
    /// Reads shortcuts from the keymaps now in use. The window calls this when
    /// the application keymap changes, so the tooltips follow a rebinding.
    /// </summary>
    internal void Update(MenuShortcutResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (HostPlatform.Current != HostOperatingSystem.MacOS)
        {
            return;
        }

        _resolve = command => resolver.Resolve(command, ImmutableDictionary<string, string>.Empty);
        foreach (var name in new[] { nameof(NewTab), nameof(CloseTab), nameof(ToggleAgentPanel), nameof(AddPanel) })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

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
