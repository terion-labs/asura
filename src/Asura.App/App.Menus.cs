using System.Collections.Specialized;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Asura.App;

/// <summary>
/// The menu bar: the application menu, one menu bar per window built from the
/// command registry, and the application-level commands those menus run.
/// </summary>
public sealed partial class App : IApplicationCommandService
{
    internal static readonly Uri HelpAddress = new("https://asura.sh/");
    internal static readonly Uri ReleaseNotesAddress = new("https://github.com/terion-labs/asura/releases");
    internal static readonly Uri ReportIssueAddress = new("https://github.com/terion-labs/asura/issues/new");

    private ApplicationMenuBar? _applicationMenu;
    private AboutWindow? _aboutWindow;
    private bool _menuAttachmentRegistered;

    bool IApplicationCommandService.IsQuickTerminalVisible => IsQuickTerminalVisible;

    internal bool IsQuickTerminalVisible => _quickTerminalController?.IsVisible == true;

    /// <summary>
    /// Builds the application menu before Avalonia exports it, so Avalonia
    /// appends its standard items to this menu instead of a default one.
    /// </summary>
    private void CreateApplicationMenu()
    {
        _applicationMenu = ApplicationMenuBar.CreateApplicationMenu(new ApplicationMenuHost(this));
        var menu = _applicationMenu.Menu;
        ((INotifyCollectionChanged)menu.Items).CollectionChanged += (_, _) => AdoptStandardApplicationItems(menu);
        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>
    /// Gives Avalonia's standard application menu items their macOS names and
    /// key equivalents: "Quit Asura", and Hide Others on Option-Command-H.
    /// </summary>
    internal static void AdoptStandardApplicationItems(NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            if (string.Equals(item.Header, "Quit", StringComparison.Ordinal))
            {
                item.Header = $"Quit {ProductName}";
            }
            else if (string.Equals(item.Header, "Hide Others", StringComparison.Ordinal))
            {
                item.Gesture = new KeyGesture(
                    Key.H,
                    Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Alt);
            }
        }
    }

    private static string ProductName => Asura.Application.ProductIdentity.DisplayName;

    /// <summary>
    /// Every top-level window gets the same menus when it opens: dialogs,
    /// floated panels, Quick Terminal and the About window included.
    /// </summary>
    private void RegisterMenuAttachment()
    {
        if (_menuAttachmentRegistered)
        {
            return;
        }

        _menuAttachmentRegistered = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => AttachMenuBar(window));
        if (_applicationMenu is { } applicationMenu)
        {
            AdoptStandardApplicationItems(applicationMenu.Menu);
        }
    }

    private void AttachMenuBar(Window window)
    {
        if (NativeMenu.GetMenu(window) is not null)
        {
            return;
        }

        var lifetime = new CancellationTokenSource();
        IApplicationMenuHost host = window switch
        {
            MainWindow main => main,
            SettingsWindow or EditorWindow => new SettingsWindowMenuHost(window, this, lifetime.Token),
            QuickTerminalWindow quick when _quickTerminalController is { } controller =>
                new QuickTerminalMenuHost(quick, controller, this, lifetime.Token),
            _ => new AuxiliaryWindowMenuHost(window, this, lifetime.Token),
        };
        var menuBar = ApplicationMenuBar.CreateMenuBar(host);
        NativeMenu.SetMenu(window, menuBar.Menu);
        if (window is QuickTerminalWindow)
        {
            MacOsMenuBar.ExcludeFromWindowsMenu(window);
        }

        // Tunnelled, so the alias reaches the menu before a focused terminal
        // reads the key as input.
        window.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (!e.Handled && menuBar.TryInvokeKeyAlias(e.Key, e.KeyModifiers))
                {
                    e.Handled = true;
                }
            },
            RoutingStrategies.Tunnel);

        window.Activated += (_, _) =>
        {
            menuBar.Refresh();
            Dispatcher.UIThread.Post(
                static () => MacOsMenuBar.AdoptStandardMenus(
                    ApplicationMenuLayout.WindowMenuTitle,
                    ApplicationMenuLayout.HelpMenuTitle),
                DispatcherPriority.Background);
        };
        window.Closed += (_, _) =>
        {
            lifetime.Cancel();
            lifetime.Dispose();
        };
    }

    internal MenuShortcutResolver CreateShortcutResolver(MainWindowViewModel? viewModel)
    {
        viewModel ??= ActiveMainWindow()?.DataContext as MainWindowViewModel ?? _mainWindowViewModel;
        var terminalKeymap = viewModel?.ActivePanel is TerminalRuntimePanelViewModel { Keymap: { } snapshot }
            ? snapshot.Bindings
            : BuiltInKeymaps.TerminalFor(CurrentOperatingSystem()).Bindings;
        var extra = _quickTerminalController is { } controller
            ? new Dictionary<CommandId, KeyStroke> { [BuiltInCommands.ToggleQuickTerminal] = controller.Hotkey }
            : null;
        return new MenuShortcutResolver(
            viewModel?.ActiveApplicationKeymap.Bindings ?? [],
            terminalKeymap,
            extra);
    }

    MenuShortcutResolver IApplicationCommandService.CreateShortcutResolver() => CreateShortcutResolver(null);

    Task IApplicationCommandService.ExecuteAsync(CommandId command, TopLevel origin) =>
        ExecuteAsync(command, origin);

    /// <summary>
    /// Runs an application command for the window that asked, or for no window
    /// when the application menu runs it with every window closed. Commands
    /// that need a workspace go to the frontmost main window.
    /// </summary>
    internal async Task ExecuteAsync(CommandId command, TopLevel? origin)
    {
        if (command == BuiltInCommands.About)
        {
            ShowAboutWindow();
        }
        else if (command == BuiltInCommands.NewWindow)
        {
            OpenNewWindow();
        }
        else if (command == BuiltInCommands.ToggleQuickTerminal)
        {
            ToggleQuickTerminal();
        }
        else if (command == BuiltInCommands.ShowSettings)
        {
            ShowSettingsWindow(page: null);
        }
        else if (command == BuiltInCommands.ShowKeyboardShortcuts)
        {
            ShowSettingsWindow(SettingsPage.Keybindings);
        }
        else if (command == BuiltInCommands.CheckForUpdates)
        {
            if (SettingsWindowOwner() is { } window)
            {
                await window.CheckForUpdatesAsync();
            }
        }
        else if (command == BuiltInCommands.NewTerminalTab)
        {
            if (ShowFrontMainWindow() is { } window)
            {
                await window.ExecuteCommandAsync(BuiltInCommands.NewTerminalTab);
            }
        }
        else if (command == BuiltInCommands.ShowHelp)
        {
            await OpenAddressAsync(origin, HelpAddress);
        }
        else if (command == BuiltInCommands.ShowReleaseNotes)
        {
            await OpenAddressAsync(origin, ReleaseNotesAddress);
        }
        else if (command == BuiltInCommands.ReportIssue)
        {
            await OpenAddressAsync(origin, ReportIssueAddress);
        }
    }

    /// <summary>
    /// The frontmost main window, brought forward; a new one when every window
    /// has been closed and Asura kept running. None while Asura is quitting.
    /// </summary>
    private MainWindow? ShowFrontMainWindow()
    {
        if (WindowForRequest() is not { } window)
        {
            return null;
        }

        ShowWindow(window);
        return window;
    }

    /// <summary>
    /// Settings… brings the one Settings window forward, or opens it for the
    /// main window in front. The main window itself stays where it is: the
    /// point of a Settings window is that the workspace stays in view.
    /// </summary>
    private void ShowSettingsWindow(SettingsPage? page) =>
        SettingsWindowOwner()?.NavigateToSettings(page);

    /// <summary>
    /// The main window whose Settings to show: the one the open Settings
    /// window belongs to, else the main window in front.
    /// </summary>
    private MainWindow? SettingsWindowOwner() =>
        SettingsWindow.Current?.Host ?? WindowForRequest();

    private async Task OpenAddressAsync(TopLevel? origin, Uri address)
    {
        if ((origin ?? ActiveMainWindow() ?? (TopLevel?)_aboutWindow) is { } launcherOwner)
        {
            _ = await launcherOwner.Launcher.LaunchUriAsync(address);
        }
    }

    /// <summary>
    /// About Asura opens one small window; choosing it again brings that
    /// window forward.
    /// </summary>
    private void ShowAboutWindow()
    {
        if (_aboutWindow is { } open)
        {
            open.Activate();
            return;
        }

        var about = new AboutWindow
        {
            DataContext = ActiveMainWindow()?.DataContext ?? _mainWindowViewModel,
        };
        about.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow = about;
        about.Show();
    }

    private static HostOperatingSystem CurrentOperatingSystem() =>
        OperatingSystem.IsMacOS()
            ? HostOperatingSystem.MacOS
            : OperatingSystem.IsWindows()
                ? HostOperatingSystem.Windows
                : HostOperatingSystem.Linux;

    /// <summary>
    /// The application menu's items run regardless of which window is key.
    /// </summary>
    private sealed class ApplicationMenuHost(App application) : IApplicationMenuHost
    {
        public CommandInvocation CaptureInvocation(IReadOnlyDictionary<string, string> arguments) =>
            new(CommandContext.Global, arguments);

        public bool CanExecute(CommandId command, IReadOnlyDictionary<string, string> arguments) => true;

        public Task ExecuteAsync(CommandId command, IReadOnlyDictionary<string, string> arguments) =>
            application.ExecuteAsync(command, application.ActiveMainWindow());

        public MenuShortcutResolver CreateShortcutResolver() => application.CreateShortcutResolver(null);
    }
}
