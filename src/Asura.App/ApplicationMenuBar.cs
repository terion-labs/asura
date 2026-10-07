using System.Collections.Immutable;
using System.Windows.Input;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Asura.App;

/// <summary>
/// What one window's menu bar runs its commands against. Every top-level
/// window has a host, so the menu bar keeps the same menus whichever window
/// is key; items a window cannot run are dimmed rather than removed.
/// </summary>
internal interface IApplicationMenuHost
{
    /// <summary>
    /// The window's command contexts and <see cref="CommandStates"/> facts.
    /// Captured each time a menu opens or a key equivalent is matched.
    /// </summary>
    CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments);

    /// <summary>
    /// Limits the registry cannot express, such as a tab position past the
    /// last tab or a window that cannot be minimized.
    /// </summary>
    bool CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments);

    Task ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments);

    MenuShortcutResolver CreateShortcutResolver();
}

/// <summary>
/// One window's native menus, built from <see cref="ApplicationMenuLayout"/>.
/// Titles, check marks, key equivalents and enabled state are refreshed when
/// macOS asks a menu to update, which it does before showing the menu and
/// before matching a key equivalent against it.
/// </summary>
internal sealed class ApplicationMenuBar
{
    private static readonly ImmutableDictionary<string, string> NoArguments = [];

    private readonly IApplicationMenuHost _host;
    private readonly List<BoundItem> _items = [];
    private CommandInvocation? _state;

    private ApplicationMenuBar(IApplicationMenuHost host)
    {
        _host = host;
    }

    public NativeMenu Menu { get; private set; } = [];

    /// <summary>The menus a window exports: File through Help.</summary>
    public static ApplicationMenuBar CreateMenuBar(IApplicationMenuHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var bar = new ApplicationMenuBar(host);
        bar.Menu = bar.Build(ApplicationMenuLayout.MenuBar);
        bar.Refresh();
        return bar;
    }

    /// <summary>
    /// The application menu's own items. Avalonia appends Services, Hide and
    /// Quit to the same menu after these.
    /// </summary>
    public static ApplicationMenuBar CreateApplicationMenu(IApplicationMenuHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var bar = new ApplicationMenuBar(host);
        bar.Menu = bar.Build(ApplicationMenuLayout.ApplicationMenu);
        bar.Refresh();
        return bar;
    }

    /// <summary>Re-reads every item's title, state, shortcut and availability.</summary>
    public void Refresh() => Refresh(_items);

    /// <summary>
    /// Runs the item a key alias stands for. A menu item shows one key
    /// equivalent, so Bigger shows Command-+ and, as in Apple's apps, also
    /// answers to Command-=, the same key without Shift.
    /// </summary>
    /// <returns>True when an available item ran.</returns>
    public bool TryInvokeKeyAlias(Key key, AvaloniaKeyModifiers modifiers)
    {
        if (key != Key.OemPlus || (modifiers & AvaloniaKeyModifiers.Shift) != AvaloniaKeyModifiers.None)
        {
            return false;
        }

        _state = _host.CaptureInvocation(NoArguments);
        foreach (var bound in _items)
        {
            if (bound.Item.Gesture is { Key: Key.Add } gesture
                && gesture.KeyModifiers == modifiers
                && bound.Command.CanExecute(null))
            {
                bound.Command.Execute(null);
                return true;
            }
        }

        return false;
    }

    private NativeMenu Build(IEnumerable<MenuNode> nodes)
    {
        var menu = new NativeMenu();
        var items = new List<BoundItem>();
        foreach (var node in nodes)
        {
            menu.Items.Add(node switch
            {
                MenuSeparatorNode => new NativeMenuItemSeparator(),
                MenuSubmenuNode submenu => new NativeMenuItem(submenu.Title)
                {
                    Menu = Build(submenu.Items),
                },
                MenuCommandNode command => Bind(command, items),
                _ => throw new ArgumentOutOfRangeException(nameof(nodes), node, null),
            });
        }

        // AppKit asks each submenu to update separately, so each refreshes only
        // its own items against freshly captured state.
        menu.NeedsUpdate += (_, _) => Refresh(items);
        return menu;
    }

    private NativeMenuItem Bind(MenuCommandNode node, List<BoundItem> items)
    {
        var item = new NativeMenuItem(node.Title);
        var bound = new BoundItem(node, item, new MenuItemCommand(this, node));
        item.Command = bound.Command;
        if (node.CheckedWhen is not null)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
        }

        items.Add(bound);
        _items.Add(bound);
        return item;
    }

    private void Refresh(IReadOnlyList<BoundItem> items)
    {
        _state = _host.CaptureInvocation(NoArguments);
        var shortcuts = _host.CreateShortcutResolver();
        foreach (var bound in items)
        {
            var node = bound.Node;
            bound.Item.Header = node.StateTitle is { } stateTitle && _state.HasState(stateTitle.State)
                ? stateTitle.Title
                : node.Title;
            if (node.CheckedWhen is { } checkedWhen)
            {
                bound.Item.IsChecked = _state.HasState(checkedWhen);
            }

            var stroke = shortcuts.Resolve(node.Command, node.Arguments);
            bound.Item.Gesture = stroke is { } value ? MenuKeyGestures.From(value) : null;
            bound.Command.RaiseCanExecuteChanged();
        }
    }

    private CommandId? Runnable(MenuCommandNode node)
    {
        if (IsAvailable(node.Command, node.Arguments))
        {
            return node.Command;
        }

        return node.Fallback is { } fallback && IsAvailable(fallback, NoArguments)
            ? fallback
            : null;
    }

    private bool IsAvailable(CommandId command, IReadOnlyDictionary<string, string> arguments)
    {
        var state = _state ??= _host.CaptureInvocation(NoArguments);
        return BuiltInCommands.Registry.TryGet(command, out var definition)
            && definition is not null
            && definition.IsAvailable(new CommandInvocation(state.ActiveContexts, arguments, state.State))
            && _host.CanExecute(command, arguments);
    }

    private Task ExecuteAsync(MenuCommandNode node)
    {
        // State may have changed since the menu last updated, for example
        // after a key equivalent that the menu did not see.
        _state = _host.CaptureInvocation(NoArguments);
        return Runnable(node) is { } command
            ? _host.ExecuteAsync(command, command == node.Command ? node.Arguments : NoArguments)
            : Task.CompletedTask;
    }

    private sealed record BoundItem(MenuCommandNode Node, NativeMenuItem Item, MenuItemCommand Command);

    private sealed class MenuItemCommand(ApplicationMenuBar bar, MenuCommandNode node) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => bar.Runnable(node) is not null;

        public async void Execute(object? parameter) => await bar.ExecuteAsync(node);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
