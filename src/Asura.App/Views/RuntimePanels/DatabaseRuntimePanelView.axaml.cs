using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class DatabaseRuntimePanelView : UserControl
{
    private DatabaseRuntimePanelViewModel? _observedPanel;

    public DatabaseRuntimePanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObservePanel();
    }

    private void ObservePanel()
    {
        _observedPanel?.PasswordRequested -= OnPasswordRequested;
        _observedPanel = Panel;
        _observedPanel?.PasswordRequested += OnPasswordRequested;
    }

    private void OnDiagramShown(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DatabaseWorkspace.FocusDiagram();
    }

    private async void OnPasswordRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Panel is not { } panel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new DatabasePasswordPromptDialog(
            panel.SavedConnectionName ?? "Database",
            panel.CanStorePassword,
            panel.PasswordStoreLabel);
        var result = await dialog.ShowDialog<DatabasePasswordPromptResult?>(owner);
        if (result is not null)
        {
            panel.SetSessionPassword(result.Password);
            if (result.SaveToCredentialStore)
            {
                _ = await panel.StoreSessionPasswordAsync(result.Password);
            }

            await panel.ConnectAsync();
        }
    }

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;

    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;

    /// <summary>The panel asks the shell to open its connection in the editor.</summary>
    public event EventHandler<RoutedEventArgs>? EditConnectionRequested;

    /// <summary>An object wants its own tab on the same connection.</summary>
    public event EventHandler<Asura.Application.DatabaseTableDescriptor>?
        OpenObjectInTabRequested;

    /// <summary>An object wants a panel split beside this one.</summary>
    public event EventHandler<Asura.Application.DatabaseTableDescriptor>?
        OpenObjectInPanelRequested;

    /// <summary>
    /// Splitting places an empty panel beside this one; what it becomes is chosen
    /// there rather than in a modal over the window.
    /// </summary>
    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    private DatabaseRuntimePanelViewModel? Panel => DataContext as DatabaseRuntimePanelViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(sender, e);
    }

    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e) =>
        ConnectionSelected?.Invoke(this, e);

    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) =>
        NewConnectionRequested?.Invoke(sender, e);

    private void OnEditConnectionClick(object? sender, RoutedEventArgs e) =>
        EditConnectionRequested?.Invoke(this, e);

    private void OnOpenObjectInTab(
        object? sender,
        Asura.Application.DatabaseTableDescriptor e) =>
        OpenObjectInTabRequested?.Invoke(this, e);

    private void OnOpenObjectInPanel(
        object? sender,
        Asura.Application.DatabaseTableDescriptor e) =>
        OpenObjectInPanelRequested?.Invoke(this, e);

    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) =>
        SplitRequested?.Invoke(sender, orientation);
}
