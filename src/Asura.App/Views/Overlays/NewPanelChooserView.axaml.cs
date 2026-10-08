using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Overlays;

public sealed partial class NewPanelChooserView : UserControl
{
    /// <summary>How many rows a saved-item list shows before it scrolls.</summary>
    internal const int VisibleRows = 5;

    public NewPanelChooserView()
    {
        InitializeComponent();
        ConnectionRows.SizeChanged += (_, _) => FitRows(ConnectionRows, ConnectionRowsScroll);
        ScreenRows.SizeChanged += (_, _) => FitRows(ScreenRows, ScreenRowsScroll);
    }

    /// <summary>
    /// Ends a list exactly at its fifth row, measured from a row as drawn,
    /// so the height follows the text size and never stops mid-row.
    /// </summary>
    private static void FitRows(ItemsControl rows, ScrollViewer scroll)
    {
        if (rows.ContainerFromIndex(0) is { Bounds.Height: > 0 } first)
        {
            var height = first.Bounds.Height * VisibleRows;
            if (!scroll.MaxHeight.Equals(height))
            {
                scroll.MaxHeight = height;
            }
        }
    }

    public event EventHandler<RoutedEventArgs>? AddBrowserPanelRequested;

    public event EventHandler<RoutedEventArgs>? AddFilePanelRequested;

    public event EventHandler<RoutedEventArgs>? AddProcessMonitorPanelRequested;

    public event EventHandler<RoutedEventArgs>? AddStatisticsPanelRequested;

    public event EventHandler<RoutedEventArgs>? AddDatabasePanelRequested;

    public event EventHandler<RoutedEventArgs>? AddDockerPanelRequested;
    public event EventHandler<RoutedEventArgs>? AddKubernetesPanelRequested;

    public event EventHandler<RoutedEventArgs>? AddGitPanelRequested;

    public event EventHandler<RoutedEventArgs>? AddTerminalPanelRequested;

    /// <summary>Raised with the chosen connection row as the sender.</summary>
    public event EventHandler<RoutedEventArgs>? AddConnectionPanelRequested;


    /// <summary>Raised with the chosen saved-screen row as the sender.</summary>
    public event EventHandler<RoutedEventArgs>? OpenScreenRequested;

    public event EventHandler<RoutedEventArgs>? ShowLayoutDesignerRequested;

    /// <summary>The first tile: Terminal.</summary>
    internal Control InitialAction => NewPanelTerminalButton;

    internal void FocusInitialAction() =>
        NewPanelTerminalButton.Focus(NavigationMethod.Tab);

    /// <summary>
    /// Fits the chooser inside the window it opens over: its preferred width
    /// when there is room, otherwise the window's width less its margins, and
    /// never taller than the window, scrolling instead.
    /// </summary>
    internal void FitTo(Size window, double margin)
    {
        var width = Math.Max(0, window.Width - (2 * margin));
        var height = Math.Max(0, window.Height - (2 * margin));
        MaxWidth = width;
        MaxHeight = height;
        Width = Math.Min(PreferredWidth, width);
    }

    private const double PreferredWidth = 760;

    private void OnAddBrowserPanelClick(object? sender, RoutedEventArgs e) =>
        AddBrowserPanelRequested?.Invoke(sender, e);

    private void OnAddFilePanelClick(object? sender, RoutedEventArgs e) =>
        AddFilePanelRequested?.Invoke(sender, e);

    private void OnAddProcessMonitorPanelClick(object? sender, RoutedEventArgs e) =>
        AddProcessMonitorPanelRequested?.Invoke(sender, e);

    private void OnAddStatisticsPanelClick(object? sender, RoutedEventArgs e) =>
        AddStatisticsPanelRequested?.Invoke(sender, e);

    private void OnAddDatabasePanelClick(object? sender, RoutedEventArgs e) =>
        AddDatabasePanelRequested?.Invoke(sender, e);

    private void OnAddDockerPanelClick(object? sender, RoutedEventArgs e) =>
        AddDockerPanelRequested?.Invoke(sender, e);

    private void OnAddKubernetesPanelClick(object? sender, RoutedEventArgs e) =>
        AddKubernetesPanelRequested?.Invoke(sender, e);

    private void OnAddGitPanelClick(object? sender, RoutedEventArgs e) =>
        AddGitPanelRequested?.Invoke(sender, e);

    private void OnAddTerminalPanelClick(object? sender, RoutedEventArgs e) =>
        AddTerminalPanelRequested?.Invoke(sender, e);

    private void OnAddConnectionPanelClick(object? sender, RoutedEventArgs e) =>
        AddConnectionPanelRequested?.Invoke(sender, e);


    private void OnOpenScreenClick(object? sender, RoutedEventArgs e) =>
        OpenScreenRequested?.Invoke(sender, e);

    private void OnShowLayoutDesignerClick(object? sender, RoutedEventArgs e) =>
        ShowLayoutDesignerRequested?.Invoke(sender, e);
}
