using Asura.App.Controls;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Asura.App;

/// <summary>
/// A panel view that carries out its own context-menu commands for the menu
/// bar, the command palette and keymaps: the Docker, Kubernetes, Files,
/// Database, Processes, Redis and Git commands in <see cref="BuiltInCommands.PanelContextCommands"/>.
/// The menu bar dims each one unless the focused panel can run it now, as the
/// context menu dims its own copy.
/// </summary>
internal interface IPanelCommandTarget
{
    /// <summary>
    /// Whether the panel can run <paramref name="command"/> now. A panel
    /// answers false for every command that is not its own.
    /// </summary>
    bool CanExecute(CommandId command);

    Task ExecuteAsync(CommandId command);

    /// <summary>
    /// The facts behind the panel's state titles, such as
    /// <see cref="CommandStates.HasMultipleSelection"/> for Copy Names.
    /// </summary>
    IEnumerable<KeyValuePair<string, bool>> States => [];
}

/// <summary>
/// Finds the panel the menu bar's panel commands act on, and reports what it
/// can run as <see cref="CommandStates"/> facts.
/// </summary>
internal static class PanelCommandTargets
{
    private static readonly IReadOnlyDictionary<string, string> NoArguments =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The panels around the focused element, innermost first, so a Files
    /// view inside a Docker panel answers Files commands and Docker answers
    /// its own. With nothing focused, or only the window itself,
    /// <paramref name="unfocused"/> supplies them: the active panel in a main
    /// window, or a floated window's panel.
    /// Focus elsewhere, such as in the sidebar, names no panel, so a key
    /// equivalent like Command-Delete never reaches a panel behind it.
    /// </summary>
    public static IReadOnlyList<IPanelCommandTarget> For(
        IInputElement? focused,
        Func<IEnumerable<IPanelCommandTarget>> unfocused)
    {
        ArgumentNullException.ThrowIfNull(unfocused);
        if (focused is Visual visual and not TopLevel)
        {
            return [.. visual.GetSelfAndVisualAncestors().OfType<IPanelCommandTarget>()];
        }

        return [.. unfocused()];
    }

    /// <summary>
    /// The targets inside <paramref name="root"/> whose panel is
    /// <paramref name="panel"/>, or every target inside it when
    /// <paramref name="panel"/> is null. A hidden view is no target: a Docker
    /// panel keeps its Files view while it shows another detail, and Move to
    /// Trash must not act on a file list the person cannot see.
    /// </summary>
    public static IEnumerable<IPanelCommandTarget> Within(Visual root, object? panel) =>
        root.GetVisualDescendants()
            .OfType<IPanelCommandTarget>()
            .Where(target => target is Visual { IsEffectivelyVisible: true }
                && (panel is null
                    || (target is StyledElement element && ReferenceEquals(element.DataContext, panel))));

    /// <summary>
    /// Adds whether each panel command can run, and the targets' own facts.
    /// A command with a menu key equivalent is unavailable while a text field
    /// has focus: AppKit offers a key to the menu before the focused control,
    /// so an enabled item would take Command-Delete or Shift-Command-N from
    /// the field the person is typing in. When two targets report the same
    /// fact, the innermost one's holds, as it is the one that runs commands.
    /// Reload Page is the exception: no field takes Command-R, and a browser
    /// reloads with it from the page or the address field.
    /// </summary>
    public static void AddState(
        ICollection<KeyValuePair<string, bool>> state,
        IReadOnlyList<IPanelCommandTarget> targets,
        IInputElement? focused)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(targets);
        var typing = IsTextInput(focused);
        foreach (var command in BuiltInCommands.PanelContextCommands)
        {
            state.Add(new(
                CommandStates.CanExecute(command),
                !(typing && TakesKeyFromTyping(command)) && Runner(targets, command) is not null));
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            foreach (var fact in target.States)
            {
                if (reported.Add(fact.Key))
                {
                    state.Add(fact);
                }
            }
        }
    }

    /// <summary>Runs the command on the innermost panel that can run it now.</summary>
    public static Task ExecuteAsync(IReadOnlyList<IPanelCommandTarget> targets, CommandId command) =>
        Runner(targets, command)?.ExecuteAsync(command) ?? Task.CompletedTask;

    /// <summary>
    /// A context-menu item for a panel command, titled as the registry and
    /// the menu bar title it. <paramref name="title"/> is the registry's state
    /// title when the item names a plural, such as Copy Names.
    /// </summary>
    public static MenuItem MenuItem(CommandId command, bool isEnabled, Func<Task> run, string? title = null)
    {
        var item = new MenuItem
        {
            Header = title ?? Title(command),
            IsEnabled = isEnabled,
            Tag = command,
        };
        item.Click += async (_, _) => await run();
        return item;
    }

    /// <summary>
    /// Names a context-menu item written in markup as the command it runs, so
    /// it shows the registry's title, as its menu-bar copy does.
    /// </summary>
    public static void Bind(MenuItem item, CommandId command)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Header = Title(command);
        item.Tag = command;
    }

    public static string Title(CommandId command) =>
        BuiltInCommands.Registry.TryGet(command, out var definition) && definition is not null
            ? definition.Title
            : throw new ArgumentOutOfRangeException(nameof(command), command, null);

    /// <summary>The registry's plural title for a command that has one.</summary>
    public static string StateTitle(CommandId command) =>
        BuiltInCommands.Registry.TryGet(command, out var definition) && definition?.StateTitle is { } stateTitle
            ? stateTitle.Title
            : Title(command);

    /// <summary>
    /// Whether the focused element takes typing: a text field, a code
    /// editor, a terminal or a web page.
    /// </summary>
    internal static bool IsTextInput(IInputElement? focused) =>
        focused is Visual visual
        && visual.GetSelfAndVisualAncestors().Any(element => element
            is TextBox
            or TextEditor
            or ManagedTerminalSurface
            or IBrowserEditCommandTarget);

    private static bool TakesKeyFromTyping(CommandId command) =>
        command != BuiltInCommands.BrowserReload
        && MacOsMenuShortcuts.For(command, NoArguments) is not null;

    private static IPanelCommandTarget? Runner(IReadOnlyList<IPanelCommandTarget> targets, CommandId command) =>
        targets.FirstOrDefault(target => target.CanExecute(command));
}
