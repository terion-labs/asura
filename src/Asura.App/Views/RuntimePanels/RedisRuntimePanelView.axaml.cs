using System.ComponentModel;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class RedisRuntimePanelView : UserControl
{
    private RedisRuntimePanelViewModel? _observedPanel;
    private bool _synchronizingPerspective;

    public RedisRuntimePanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObservePanel();
    }

    public event EventHandler<RoutedEventArgs>? CloseRequested;
    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;
    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;
    public event EventHandler<RoutedEventArgs>? EditConnectionRequested;
    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    private RedisRuntimePanelViewModel? Panel => DataContext as RedisRuntimePanelViewModel;

    private void ObservePanel()
    {
        if (_observedPanel is not null)
        {
            _observedPanel.PasswordRequested -= OnPasswordRequested;
            _observedPanel.PropertyChanged -= OnPanelPropertyChanged;
        }

        _observedPanel = Panel;
        if (_observedPanel is not null)
        {
            _observedPanel.PasswordRequested += OnPasswordRequested;
            _observedPanel.PropertyChanged += OnPanelPropertyChanged;
        }

        SyncPerspective();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is null or nameof(RedisRuntimePanelViewModel.Perspective))
        {
            SyncPerspective();
        }
    }

    private async void OnPasswordRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Panel is not { } panel || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var result = await new DatabasePasswordPromptDialog(
                panel.SavedConnectionName ?? "Redis",
                panel.CanStorePassword,
                panel.PasswordStoreLabel)
            .ShowDialog<DatabasePasswordPromptResult?>(owner);
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

    private void OnPerspectiveSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_synchronizingPerspective || Panel is not { } panel || PerspectivePicker.SelectedIndex < 0)
        {
            return;
        }

        panel.Perspective = (RedisWorkspacePerspective)PerspectivePicker.SelectedIndex;
        SyncPerspective();
    }

    private void SyncPerspective()
    {
        _synchronizingPerspective = true;
        try
        {
            PerspectivePicker.SelectedIndex = (int)(Panel?.Perspective ?? RedisWorkspacePerspective.Browser);
        }
        finally
        {
            _synchronizingPerspective = false;
        }
    }

    // Return filters the key list by the pattern; the scan reads the server,
    // so it runs when asked rather than on every keystroke.
    private void OnKeyPatternKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Enter
            && e.KeyModifiers == KeyModifiers.None
            && Panel?.ScanCommand is { } scan
            && scan.CanExecute(null))
        {
            e.Handled = true;
            scan.Execute(null);
        }
    }

    // Return runs the query, as it does in every search box.
    private void OnSearchQueryKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Enter
            && e.KeyModifiers == KeyModifiers.None
            && Panel?.SearchCommand is { } search
            && search.CanExecute(null))
        {
            e.Handled = true;
            search.Execute(null);
        }
    }

    // Clearing the field — its clear button or Escape — lists every key again,
    // as clearing any search field shows everything it was hiding.
    private void OnKeyPatternTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (string.IsNullOrEmpty(KeyPatternBox.Text)
            && Panel is { IsFilteringKeys: true, ScanCommand: { } scan }
            && scan.CanExecute(null))
        {
            scan.Execute(null);
        }
    }

    private void OnNewKeyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Panel?.BeginCreateKey();
    }

    // The sheet closes itself once the key exists; this is the way out without
    // creating one.
    private void OnCancelCreateKeyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        Panel?.CancelCreateKey();
    }

    // Rows of a collection form. The row a button belongs to is the row it
    // carries as its data.
    private void OnAddNewKeyEntryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Panel?.AddNewKeyEntry();
    }

    private void OnRemoveNewKeyEntryClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (Panel is { } panel && sender is Control { DataContext: RedisEntryDraft entry })
        {
            panel.RemoveNewKeyEntry(entry);
        }
    }

    private void OnAddMutationEntryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Panel?.AddMutationEntry();
    }

    private void OnRemoveMutationEntryClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (Panel is { } panel && sender is Control { DataContext: RedisEntryDraft entry })
        {
            panel.RemoveMutationEntry(entry);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(sender, e);
    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e) => ConnectionSelected?.Invoke(this, e);
    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) => NewConnectionRequested?.Invoke(sender, e);
    private void OnEditConnectionClick(object? sender, RoutedEventArgs e) => EditConnectionRequested?.Invoke(this, e);
    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) => SplitRequested?.Invoke(sender, orientation);
}
