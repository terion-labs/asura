using System.ComponentModel;
using System.Globalization;
using System.Text;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Application;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class ProcessMonitorRuntimePanelView : UserControl, IEditTarget
{
    /// <summary>
    /// Below this width the Started column folds away, so name, CPU and
    /// memory keep their room. The panel's own width, not the window's: two
    /// panels can share one window, and this one answers only for itself.
    /// </summary>
    internal const double StartedColumnMinimumPanelWidth = 560;

    private readonly TypeSelectBuffer _typeSelect = new();
    private ProcessMonitorRuntimePanelViewModel? _boundPanel;

    public ProcessMonitorRuntimePanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObservePanel();
        ProcessGrid.AddHandler(TextInputEvent, OnProcessTextInput, RoutingStrategies.Tunnel);
        SizeChanged += (_, change) => UpdateStartedColumn(change.NewSize.Width);
        UpdateSortHeaders();
    }

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;

    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;

    /// <summary>
    /// Splitting places an empty panel beside this one; what it becomes is chosen
    /// there rather than in a modal over the window.
    /// </summary>
    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(sender, e);
    }

    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e) =>
        ConnectionSelected?.Invoke(this, e);

    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) =>
        NewConnectionRequested?.Invoke(this, e);

    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) =>
        SplitRequested?.Invoke(sender, orientation);

    private void ObservePanel()
    {
        if (_boundPanel is { } previous)
        {
            previous.PropertyChanged -= OnPanelPropertyChanged;
        }

        _boundPanel = DataContext as ProcessMonitorRuntimePanelViewModel;
        if (_boundPanel is { } panel)
        {
            panel.PropertyChanged += OnPanelPropertyChanged;
        }

        UpdateSortHeaders();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, nameof(ProcessMonitorRuntimePanelViewModel.Sort), StringComparison.Ordinal))
        {
            UpdateSortHeaders();
        }
    }

    /// <summary>
    /// A header click asks the panel for the column's order — the host sorts,
    /// because it also bounds the sample by that order — and the grid is told
    /// not to reorder the rows itself.
    /// </summary>
    private void OnProcessSorting(object? sender, DataGridColumnEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (DataContext is ProcessMonitorRuntimePanelViewModel panel
            && SortFor(e.Column.Tag as string) is { } sort)
        {
            panel.ChangeSort(sort);
        }
    }

    /// <summary>
    /// The order a column starts in: names and IDs from the top of the
    /// alphabet, CPU and memory from the heaviest.
    /// </summary>
    internal static ProcessMonitorSort? SortFor(string? column) => column switch
    {
        "ProcessId" => ProcessMonitorSort.ProcessIdAscending,
        "Name" => ProcessMonitorSort.NameAscending,
        "Cpu" => ProcessMonitorSort.CpuDescending,
        "Memory" => ProcessMonitorSort.MemoryDescending,
        _ => null,
    };

    /// <summary>
    /// Which way a column is sorted under the panel's current order, or null
    /// when the order belongs to another column.
    /// </summary>
    internal static ListSortDirection? DirectionFor(string? column, ProcessMonitorSort sort) =>
        (column, sort) switch
        {
            ("ProcessId", ProcessMonitorSort.ProcessIdAscending) => ListSortDirection.Ascending,
            ("ProcessId", ProcessMonitorSort.ProcessIdDescending) => ListSortDirection.Descending,
            ("Name", ProcessMonitorSort.NameAscending) => ListSortDirection.Ascending,
            ("Name", ProcessMonitorSort.NameDescending) => ListSortDirection.Descending,
            ("Cpu", ProcessMonitorSort.CpuAscending) => ListSortDirection.Ascending,
            ("Cpu", ProcessMonitorSort.CpuDescending) => ListSortDirection.Descending,
            ("Memory", ProcessMonitorSort.MemoryAscending) => ListSortDirection.Ascending,
            ("Memory", ProcessMonitorSort.MemoryDescending) => ListSortDirection.Descending,
            _ => null,
        };

    /// <summary>
    /// Each header names its column and, on the sorted one, carries the
    /// chevron a Mac table header shows; its accessible name says the same.
    /// </summary>
    private void UpdateSortHeaders()
    {
        var sort = _boundPanel?.Sort ?? ProcessMonitorSort.CpuDescending;
        foreach (var column in ProcessGrid.Columns)
        {
            var key = column.Tag as string;
            var title = key switch
            {
                "ProcessId" => "PID",
                "Name" => "Name",
                "Cpu" => "CPU",
                "Memory" => "Memory",
                _ => "Started",
            };
            var direction = DirectionFor(key, sort);
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } },
            };
            if (direction is { } sorted)
            {
                var chevron = new SymbolIcon
                {
                    Symbol = sorted == ListSortDirection.Ascending ? Symbol.ChevronUp : Symbol.ChevronDown,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                chevron.Bind(SymbolIcon.FontSizeProperty, chevron.GetResourceObservable("ShellIconSizeXs"));
                header.Children.Add(chevron);
            }

            AutomationProperties.SetName(
                header,
                direction switch
                {
                    ListSortDirection.Ascending => $"{title}, sorted ascending",
                    ListSortDirection.Descending => $"{title}, sorted descending",
                    _ => title,
                });
            column.Header = header;
        }
    }

    private void UpdateStartedColumn(double panelWidth)
    {
        foreach (var column in ProcessGrid.Columns)
        {
            if (string.Equals(column.Tag as string, "Started", StringComparison.Ordinal))
            {
                column.IsVisible = panelWidth > StartedColumnMinimumPanelWidth;
            }
        }
    }

    /// <summary>
    /// Typing a name jumps to the first process that starts with it, as in
    /// any Mac list.
    /// </summary>
    private void OnProcessTextInput(object? sender, TextInputEventArgs e)
    {
        _ = sender;
        if (DataContext is not ProcessMonitorRuntimePanelViewModel panel
            || _typeSelect.Append(e.Text, DateTimeOffset.UtcNow) is not { } prefix
            || TypeSelectBuffer.FindMatch(panel.Processes, process => process.Name, prefix) is not { } match)
        {
            return;
        }

        e.Handled = true;
        ProcessGrid.SelectedItem = match;
        ProcessGrid.ScrollIntoView(match, null);
    }

    private IReadOnlyList<ProcessMonitorEntryViewModel> SelectedProcesses() =>
        ProcessGrid.SelectedItems?
            .OfType<ProcessMonitorEntryViewModel>()
            .ToArray()
        ?? [];

    private void OnProcessContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _ = sender;
        var selected = SelectedProcesses();
        CopyRowsMenuItem.IsEnabled = selected.Count > 0;
        CopyNameMenuItem.IsEnabled = selected.Count == 1;
        CopyProcessIdMenuItem.IsEnabled = selected.Count == 1;
        e.Cancel = DataContext is not ProcessMonitorRuntimePanelViewModel { ShowContent: true };
    }

    private void OnCopyRowsClick(object? sender, RoutedEventArgs e) =>
        CopyText(RowsText(SelectedProcesses()));

    private void OnCopyNameClick(object? sender, RoutedEventArgs e) =>
        CopyText(SelectedProcesses() is [var process] ? process.Name : null);

    private void OnCopyProcessIdClick(object? sender, RoutedEventArgs e) =>
        CopyText(SelectedProcesses() is [var process]
            ? process.ProcessId.ToString(CultureInfo.InvariantCulture)
            : null);

    /// <summary>
    /// The selected rows as text, one per line and tab between columns, so a
    /// paste into a spreadsheet or a terminal keeps them apart.
    /// </summary>
    internal static string? RowsText(IReadOnlyList<ProcessMonitorEntryViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(row.ProcessId.ToString(CultureInfo.InvariantCulture))
                .Append('\t').Append(row.Name)
                .Append('\t').Append(row.Cpu)
                .Append('\t').Append(row.Memory)
                .Append('\t').Append(row.Started)
                .Append('\n');
        }

        return text.ToString();
    }

    private void CopyText(string? text)
    {
        if (!string.IsNullOrEmpty(text) && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    private bool GridHasFocus() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused
        && (ReferenceEquals(focused, ProcessGrid) || ProcessGrid.IsVisualAncestorOf(focused));

    bool IEditTarget.CanExecute(EditAction action) =>
        GridHasFocus()
        && action switch
        {
            EditAction.Copy => SelectedProcesses().Count > 0,
            EditAction.SelectAll => DataContext is ProcessMonitorRuntimePanelViewModel { Processes.Count: > 0 },
            _ => false,
        };

    Task IEditTarget.ExecuteAsync(EditAction action)
    {
        switch (action)
        {
            case EditAction.Copy:
                CopyText(RowsText(SelectedProcesses()));
                break;
            case EditAction.SelectAll:
                ProcessGrid.SelectAll();
                break;
            default:
                break;
        }

        return Task.CompletedTask;
    }
}
