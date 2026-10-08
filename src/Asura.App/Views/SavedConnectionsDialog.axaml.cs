using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class SavedConnectionsDialog : DialogWindow
{
    public SavedConnectionsDialog()
    {
        InitializeComponent();

        // Return edits the selected row. Tunnelled, because the list's own
        // key handling would otherwise take the key first.
        this.FindControl<ListBox>("ConnectionList")!
            .AddHandler(KeyDownEvent, OnConnectionListKeyDown, RoutingStrategies.Tunnel);
    }

    public event EventHandler<LauncherConnectionViewModel>? EditRequested;
    public event EventHandler? AddRequested;

    private LauncherConnectionViewModel? SelectedConnection =>
        this.FindControl<ListBox>("ConnectionList")?.SelectedItem as LauncherConnectionViewModel;

    private void OnConnectionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        this.FindControl<Button>("EditButton")!.IsEnabled = SelectedConnection is not null;
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        EditSelected();
    }

    private void OnConnectionDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        if (e.Source is Control { DataContext: LauncherConnectionViewModel })
        {
            EditSelected();
        }
    }

    private void OnConnectionListKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key is Key.Enter or Key.Return && e.KeyModifiers == KeyModifiers.None && SelectedConnection is not null)
        {
            e.Handled = true;
            EditSelected();
        }
    }

    private void EditSelected()
    {
        if (SelectedConnection is { } connection)
        {
            EditRequested?.Invoke(this, connection);
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);
}
