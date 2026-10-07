using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The database panel against the Mac's conventions: views switched by a
/// segmented control, one button style per toolbar, a pager that reads as
/// rows per page, a context menu of three groups, and a result table that
/// selects like a Mac table.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DatabaseWorkspaceHigHeadlessTests
{
    [Fact]
    public Task ThePerspectivesAreOneSegmentedControlThatFollowsThePanel() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseRuntimePanelView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();

            var picker = Segmented(view, "Object view");
            var segments = picker.GetVisualDescendants().OfType<RadioButton>().ToArray();
            Assert.Equal(["Data", "Structure", "Indexes"], segments.Select(segment => segment.Content));
            Assert.All(segments, segment => Assert.True(segment.IsEnabled));
            Assert.True(segments[0].IsChecked);

            // Choosing a segment changes what the panel shows, and the panel
            // changing its mode moves the control with it.
            segments[1].IsChecked = true;
            Assert.Equal(DatabaseWorkspaceMode.Structure, panel.SelectedMode);
            panel.SetMode(DatabaseWorkspaceMode.Indexes);
            Assert.Equal(2, picker.SelectedIndex);
            Assert.True(segments[2].IsChecked);

            // The database overview has views of its own, in the same slot.
            panel.ShowDatabaseOverview();
            window.UpdateLayout();
            Assert.False(picker.IsEffectivelyVisible);
            var overview = Segmented(view, "Database view");
            Assert.True(overview.IsEffectivelyVisible);
            Assert.Equal(0, overview.SelectedIndex);

            // With nothing selected, Structure and Indexes dim rather than go.
            panel.Disconnect();
            window.UpdateLayout();
            Assert.True(picker.IsEffectivelyVisible);
            Assert.Equal(3, picker.GetVisualDescendants().OfType<RadioButton>().Count());
            Assert.False(segments[1].IsEnabled);
            Assert.False(segments[2].IsEnabled);
            await Task.CompletedTask;
        });

    [Fact]
    public Task ClickingTheDiagramSegmentShowsTheDiagram() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseRuntimePanelView { DataContext = panel };
            window.Content = view;
            panel.ShowDatabaseOverview();
            window.UpdateLayout();

            var diagram = Named<RadioButton>(view, "Show database Mermaid ER diagram");
            var centre = diagram.TranslatePoint(
                new Point(diagram.Bounds.Width / 2, diagram.Bounds.Height / 2),
                window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            for (var attempt = 0; attempt < 200 && !panel.IsDatabaseDiagramOverview; attempt++)
            {
                await Task.Delay(10);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            Assert.True(diagram.IsChecked);
            Assert.True(panel.IsDatabaseDiagramOverview);
        });

    [Fact]
    public Task TheRowToolbarUsesOneButtonStyleAndThePagerSaysRowsPerPage() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();

            var add = Named<Button>(view, "Add a database row");
            var delete = Named<Button>(view, "Delete the selected database rows");
            var revert = Named<Button>(view, "Revert unsaved database changes");
            var save = Named<Button>(view, "Save database changes");
            var apply = Named<Button>(view, "Apply the table filter");
            var clear = Named<Button>(view, "Clear the table filter");
            foreach (var button in new[] { add, revert, save, apply, clear })
            {
                Assert.Contains("SecondaryButton", button.Classes, StringComparer.Ordinal);
                Assert.DoesNotContain("PrimaryButton", button.Classes, StringComparer.Ordinal);
                Assert.DoesNotContain("TextButton", button.Classes, StringComparer.Ordinal);
            }

            Assert.Contains("DestructiveButton", delete.Classes, StringComparer.Ordinal);
            Assert.Contains(
                OperatingSystem.IsMacOS() ? "(⌥⌘N)" : "N",
                DatabaseRuntimePanelViewModel.AddRowTip,
                StringComparison.Ordinal);

            // The open object's row tells a screen reader it is the selected one.
            var objectRows = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.DataContext is DatabaseTableItemViewModel && button.IsEffectivelyVisible)
                .ToArray();
            Assert.NotEmpty(objectRows);
            foreach (var row in objectRows)
            {
                var selected = ((DatabaseTableItemViewModel)row.DataContext!).IsSelected;
                Assert.Equal(selected ? "Selected" : string.Empty, AutomationProperties.GetItemStatus(row) ?? string.Empty);
            }

            Assert.Contains(objectRows, row => ((DatabaseTableItemViewModel)row.DataContext!).IsSelected);

            // The filter switch says what it switches.
            var include = Named<CheckBox>(view, "Include this filter condition");
            Assert.Equal("Filter", include.Content);

            // "Rows per page: 200 · 2 rows", never "200 / 2".
            var texts = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .Select(text => text.Text)
                .ToArray();
            Assert.Contains("Rows per page:", texts, StringComparer.Ordinal);
            Assert.DoesNotContain("/", texts, StringComparer.Ordinal);
            var total = Named<TextBlock>(view, "Total matching database rows");
            Assert.EndsWith(" rows", total.Text, StringComparison.Ordinal);
            Assert.Equal(panel.TotalRowsText, total.Text);

            // The status bar counts objects with a real plural.
            Assert.Matches(@"^Connected · [\d.,\u00a0\u202f]+ objects$|^Connected · 1 object$", panel.StatusText);
            await Task.CompletedTask;
        });

    [Fact]
    public Task TheResultMenuHasThreeGroupsWithDeleteLast() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();

            var grid = Named<DataGrid>(view, "Database rows");
            var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
            var items = menu.Items.Cast<Control>().ToArray();
            Assert.Equal(2, items.Count(item => item is Separator));
            var delete = Assert.IsType<MenuItem>(items[^1]);
            Assert.Equal("Delete Row", delete.Header);
            Assert.IsType<Separator>(items[^2]);

            var headers = Flatten(menu).Select(item => item.Header as string).ToArray();
            Assert.Contains("Open Cell Editor…", headers, StringComparer.Ordinal);
            Assert.DoesNotContain(headers, header => header?.Contains("Quick Look", StringComparison.Ordinal) == true);
            Assert.Equal("Open Cell Editor…", Assert.IsType<MenuItem>(items[0]).Header);

            // Add Row is ⌥⌘N on a Mac: ⌘I is Get Info.
            var add = Assert.Single(Flatten(menu), item => Equals(item.Header, "Add Row"));
            var addRowGesture = DatabaseGridShortcuts.GestureFor(DatabaseGridShortcuts.GridCommand.AddRow);
            Assert.NotNull(addRowGesture);
            Assert.Equal(addRowGesture, add.InputGesture);
            Assert.Equal(Key.N, addRowGesture.Key);
            Assert.Equal(
                OperatingSystem.IsMacOS()
                    ? Avalonia.Input.KeyModifiers.Meta | Avalonia.Input.KeyModifiers.Alt
                    : Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift,
                addRowGesture.KeyModifiers);

            // ⌘D is Panel ▸ Split Right on a Mac, so Duplicate Row shows no key there.
            var duplicate = Assert.Single(Flatten(menu), item => Equals(item.Header, "Duplicate Row"));
            Assert.Equal(DatabaseGridShortcuts.GestureFor(DatabaseGridShortcuts.GridCommand.DuplicateRow), duplicate.InputGesture);
            if (OperatingSystem.IsMacOS())
            {
                Assert.Null(duplicate.InputGesture);
            }
            await Task.CompletedTask;
        });

    [Fact]
    public Task TheResultTableSelectsSeveralRowsAndShowsTheSelection() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();

            var grid = Named<DataGrid>(view, "Database rows");
            Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);
            Assert.True(panel.ResultRows.Count >= 2);

            grid.SelectedItem = panel.ResultRows[0];
            grid.SelectedItems.Add(panel.ResultRows[1]);
            window.UpdateLayout();

            // The inspector shows one row and says how many are selected; the
            // grid keeps both rows selected and draws them so.
            Assert.Same(panel.ResultRows[0], panel.SelectedRow);
            Assert.Equal(2, panel.SelectedRows.Count);
            Assert.Equal("Row 1 · 2 selected", panel.SelectedRowTitle);
            Assert.Equal(2, grid.SelectedItems.Count);
            foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>()
                         .Where(candidate => candidate.DataContext is DatabaseResultRowViewModel shown
                             && panel.SelectedRows.Contains(shown)))
            {
                var background = Assert.Single(
                    row.GetVisualDescendants().OfType<Rectangle>(),
                    rectangle => rectangle.Name == "BackgroundRectangle");
                var fill = Assert.IsAssignableFrom<ISolidColorBrush>(background.Fill);
                Assert.NotEqual(0, fill.Color.A);
            }

            var (text, _) = await panel.BuildRowsTsvAsync(panel.SelectedRows);
            Assert.Equal(2, text.Split(Environment.NewLine).Length);

            // Copy Row As and Duplicate Row act on one row, so with two
            // selected they are unavailable instead of acting on one silently.
            Assert.True(panel.HasMultipleSelectedRows);
            Assert.False(panel.CanDuplicateSelectedRow);
            grid.CurrentColumn = grid.Columns[0];
            grid.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent });
            window.UpdateLayout();
            var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
            Assert.True(menu.IsOpen);
            Assert.Equal(2, panel.SelectedRows.Count);
            var menuItems = Flatten(menu).ToArray();
            Assert.False(Assert.Single(menuItems, item => Equals(item.Header, "Copy Row As")).IsEnabled);
            Assert.False(Assert.Single(menuItems, item => Equals(item.Header, "Duplicate Row")).IsEnabled);
            Assert.Equal("Delete 2 Rows", Assert.Single(menuItems, item => item.Name == "DeleteRowMenuItem").Header);
            menu.Close();
            window.UpdateLayout();

            // Delete takes every selected row.
            Assert.True(panel.CanDeleteSelectedRow);
            var before = panel.ResultRows.Count;
            panel.DeleteSelectedRow();
            Assert.Equal(before - 2, panel.ResultRows.Count);
            Assert.True(panel.HasPendingChanges);
            Assert.Empty(panel.SelectedRows);
        });

    private static SegmentedControl Segmented(Control root, string name) =>
        root.GetVisualDescendants().OfType<SegmentedControl>().First(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));

    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));

    private static IEnumerable<MenuItem> Flatten(ItemsControl owner)
    {
        foreach (var item in owner.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in Flatten(item))
            {
                yield return child;
            }
        }
    }

    private static async Task RunAsync(Func<DatabaseRuntimePanelViewModel, Window, Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var headless = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await headless.Dispatch(
                async () =>
                {
                    using var panel = new DatabaseRuntimePanelViewModel(
                        PanelInstanceId.New(),
                        "Database",
                        new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient(),
                        "sqlite",
                        "Data Source=demo.db");
                    await panel.Initialization;
                    await panel.PreviewTableAsync(panel.Tables[0]);
                    var window = new Window { Width = 1200, Height = 760 };
                    window.Show();
                    try
                    {
                        await assertion(panel, window);
                    }
                    finally
                    {
                        window.Content = null;
                        window.Close();
                    }

                    return true;
                },
                timeout.Token));
        }
        finally
        {
            await headless.DisposeAsync();
        }
    }
}
