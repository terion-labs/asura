using Asura.App.ViewModels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentIcons.Avalonia;

namespace Asura.App.Views.Components;

public sealed partial class SavedConnectionShortcutView : UserControl
{
    internal const string EditConnectionHeader = "Edit Connection…";
    internal const string DeleteConnectionHeader = "Delete Connection";

    public SavedConnectionShortcutView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PresentShortcutMenu();
    }

    public event EventHandler<SavedConnectionLaunchViewModel>? LaunchRequested;

    /// <summary>Raised with the row's own view model as the sender's context.</summary>
    public event EventHandler<SavedConnectionShortcutViewModel>? EditRequested;

    public event EventHandler<SavedConnectionShortcutViewModel>? DeleteRequested;

    /// <summary>
    /// The chevron's menu: the other ways to open the connection, then Edit
    /// Connection… and, apart from it, Delete Connection. It used to be a
    /// popover of buttons, which arrow keys did not walk and VoiceOver read
    /// as buttons rather than menu items.
    ///
    /// Built when the row is bound rather than as the menu opens: the row's
    /// view model is immutable, and a presenter already showing a flyout
    /// does not pick up items added from its own Opening event.
    /// </summary>
    private void PresentShortcutMenu()
    {
        if (ShortcutMenuButton.Flyout is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        if (DataContext is not SavedConnectionShortcutViewModel shortcut)
        {
            return;
        }

        foreach (var item in BuildShortcutMenu(shortcut))
        {
            menu.Items.Add(item);
        }
    }

    internal IReadOnlyList<Control> BuildShortcutMenu(SavedConnectionShortcutViewModel shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        var items = new List<Control>();
        foreach (var launch in shortcut.AlternativeLaunches)
        {
            var item = new MenuItem
            {
                Header = GitMenuText.Literal(launch.Label),
                Icon = new SymbolIcon { Symbol = launch.Icon },
                DataContext = launch,
                IsEnabled = shortcut.CanOpen,
            };
            AutomationProperties.SetName(item, launch.Label);
            item.Click += OnAlternativeClick;
            items.Add(item);
        }

        if (items.Count > 0)
        {
            items.Add(new Separator());
        }

        var edit = new MenuItem { Header = EditConnectionHeader };
        edit.Click += OnEditClick;
        items.Add(edit);
        items.Add(new Separator());

        // No ellipsis: deleting a connection happens at once and can be
        // undone, so nothing further is asked.
        var delete = new MenuItem { Header = DeleteConnectionHeader };
        delete.Click += OnDeleteClick;
        items.Add(delete);
        return items;
    }

    private void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (DataContext is SavedConnectionShortcutViewModel shortcut)
        {
            LaunchRequested?.Invoke(this, shortcut.DefaultLaunch);
        }
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (DataContext is SavedConnectionShortcutViewModel shortcut)
        {
            ShortcutMenuButton.Flyout?.Hide();
            EditRequested?.Invoke(this, shortcut);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (DataContext is SavedConnectionShortcutViewModel shortcut)
        {
            ShortcutMenuButton.Flyout?.Hide();
            DeleteRequested?.Invoke(this, shortcut);
        }
    }

    private void OnAlternativeClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: SavedConnectionLaunchViewModel launch })
        {
            ShortcutMenuButton.Flyout?.Hide();
            LaunchRequested?.Invoke(this, launch);
        }
    }
}
