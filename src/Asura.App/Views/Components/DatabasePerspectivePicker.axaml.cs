using System.ComponentModel;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// The database perspectives as segmented controls. The panel header and the
/// embedded preview both carry them, so they are one control rather than two
/// copies of a row of toggle buttons kept in step by hand.
///
/// The view model owns the perspective. A choice the panel refuses — leaving an
/// object with unsaved rows, say — is answered by putting the control back on
/// the perspective the panel is actually showing.
/// </summary>
public sealed partial class DatabasePerspectivePicker : UserControl
{
    private DatabaseRuntimePanelViewModel? _observedPanel;
    private bool _synchronizing;

    public DatabasePerspectivePicker()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObservePanel();
        ObservePanel();
    }

    /// <summary>The ER diagram is now showing; the host moves focus onto it.</summary>
    public event EventHandler<RoutedEventArgs>? DiagramShown;

    private void ObservePanel()
    {
        _observedPanel?.PropertyChanged -= OnPanelPropertyChanged;
        _observedPanel = DataContext as DatabaseRuntimePanelViewModel;
        _observedPanel?.PropertyChanged += OnPanelPropertyChanged;
        Synchronize();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is null
            or nameof(DatabaseRuntimePanelViewModel.SelectedMode)
            or nameof(DatabaseRuntimePanelViewModel.SelectedObject)
            or nameof(DatabaseRuntimePanelViewModel.SelectedDatabaseOverviewMode)
            or nameof(DatabaseRuntimePanelViewModel.IsDatabaseOverview))
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        _synchronizing = true;
        try
        {
            var panel = _observedPanel;
            ModePicker.SelectedIndex = (int)(panel?.SelectedMode ?? DatabaseWorkspaceMode.Data);
            OverviewPicker.SelectedIndex = panel?.IsDatabaseDiagramOverview == true ? 1 : 0;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void OnModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_synchronizing || _observedPanel is not { } panel)
        {
            return;
        }

        panel.SetMode((DatabaseWorkspaceMode)Math.Max(0, ModePicker.SelectedIndex));
        Synchronize();
    }

    private async void OnOverviewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        if (_synchronizing || _observedPanel is not { } panel)
        {
            return;
        }

        if (OverviewPicker.SelectedIndex == 1)
        {
            await panel.ShowDatabaseDiagramAsync();
        }
        else
        {
            panel.ShowDatabaseOverview();
        }

        if (!ReferenceEquals(_observedPanel, panel))
        {
            return;
        }

        Synchronize();
        if (panel.IsDatabaseDiagramOverview)
        {
            DiagramShown?.Invoke(this, e);
        }
    }
}
