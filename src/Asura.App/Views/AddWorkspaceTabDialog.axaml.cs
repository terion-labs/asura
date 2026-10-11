using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentIcons.Common;

namespace Asura.App.Views;

internal sealed record TabSourceRow(
    string Title,
    string Detail,
    Symbol Symbol,
    string? Badge,
    WorkspaceTabSource? Source)
{
    public bool IsNewScreen => Source is null;

    public bool Matches(string term) =>
        IsNewScreen
        || Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Detail.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (Badge?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// A connection's glyph says what it reaches, as its detail line does:
    /// one server glyph for every row made SSH, WSL, Docker, and this Mac look
    /// alike at a glance.
    /// </summary>
    public static Symbol ConnectionSymbol(ConnectionKind? kind) => kind switch
    {
        ConnectionKind.Local => Symbol.Laptop,
        ConnectionKind.Docker => Symbol.Box,
        ConnectionKind.Wsl => Symbol.WindowConsole,
        _ => Symbol.Server,
    };
}

/// <summary>
/// Chooses what a new workspace tab opens: a saved connection, a saved screen —
/// linked or copied — or a screen that exists only in this workspace.
/// </summary>
public sealed partial class AddWorkspaceTabDialog : DialogWindow
{
    private readonly IReadOnlyList<TabSourceRow> _rows = [];

    public AddWorkspaceTabDialog()
    {
        InitializeComponent();
    }

    public AddWorkspaceTabDialog(
        IReadOnlyList<ScreenConnectionOption> connections,
        IReadOnlyList<WorkspaceScreenOption> screens,
        IReadOnlyList<WorkspaceLayoutOption> layouts)
        : this()
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(layouts);

        // Only what can actually be opened. An unavailable definition is a
        // repair, not a choice, and the editor's rows are where repairs happen.
        var available = layouts
            .Where(layout => layout.IsAvailable && !LayoutDefinition.IsAutoSaved(layout.Id))
            .ToArray();
        _rows =
        [
            new TabSourceRow(
                "New screen for this workspace",
                "Pick a layout, then choose a connection for each panel",
                Symbol.Add,
                "Workspace-only",
                null),
            .. connections
                .Where(connection => connection.IsAvailable)
                .Select(connection => new TabSourceRow(
                    connection.Name,
                    connection.Kind,
                    TabSourceRow.ConnectionSymbol(connection.ConnectionKind),
                    null,
                    new WorkspaceTabSource.Connection(connection.Id))),
            .. screens
                .Where(screen => screen.IsAvailable)
                .Select(screen => new TabSourceRow(
                    screen.Name,
                    screen.LayoutName,
                    Symbol.Grid,
                    "Screen",
                    new WorkspaceTabSource.LinkedScreen(screen.Id))),
        ];

        NewScreenLayout.ItemsSource = available;
        NewScreenLayout.SelectedItem = available.FirstOrDefault();
        ShowRows(_rows);
        Opened += (_, _) => SearchInput.Focus();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        var term = SearchInput.Text;
        ShowRows(string.IsNullOrWhiteSpace(term)
            ? _rows
            : [.. _rows.Where(row => row.Matches(term))]);
    }

    private void ShowRows(IReadOnlyList<TabSourceRow> rows)
    {
        SourceList.ItemsSource = rows;
        SourceList.SelectedItem = null;
    }

    private void OnSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        var row = SourceList.SelectedItem as TabSourceRow;
        NewScreenOptions.IsVisible = row is { IsNewScreen: true };
        SavedScreenOptions.IsVisible = row?.Source is WorkspaceTabSource.LinkedScreen;
        SelectionOptions.IsVisible = NewScreenOptions.IsVisible || SavedScreenOptions.IsVisible;
        ConfirmButton.IsEnabled = row is not null;
    }

    private void OnSourceDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        _ = e;
        Confirm();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Confirm();
    }

    private void Confirm()
    {
        if (SourceList.SelectedItem is not TabSourceRow row)
        {
            return;
        }

        if (row.IsNewScreen)
        {
            if (NewScreenLayout.SelectedItem is not WorkspaceLayoutOption layout)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(NewScreenName.Text)
                ? "New screen"
                : NewScreenName.Text.Trim();
            Close(new WorkspaceTabSource.NewScreen(layout.Id, name));
            return;
        }

        // Copying is a decision about the saved screen, so it is applied here
        // rather than being a fourth kind of row.
        Close(row.Source is WorkspaceTabSource.LinkedScreen linked && CopyScreen.IsChecked == true
            ? new WorkspaceTabSource.CopiedScreen(linked.Id)
            : row.Source);
    }

}
