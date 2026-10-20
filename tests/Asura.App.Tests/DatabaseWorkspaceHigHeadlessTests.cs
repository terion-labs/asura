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
using Avalonia.Layout;
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
    public Task TheResultMenuGroupsRefreshAloneWithDeleteLast() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();

            var grid = Named<DataGrid>(view, "Database rows");
            var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
            var items = menu.Items.Cast<Control>().ToArray();
            Assert.Equal(3, items.Count(item => item is Separator));
            var delete = Assert.IsType<MenuItem>(items[^1]);
            Assert.Equal("Delete Row", delete.Header);
            Assert.IsType<Separator>(items[^2]);
            // Delete is an ordinary item: the danger is in the confirmation,
            // not a red row in the menu.
            Assert.False(delete.IsSet(MenuItem.ForegroundProperty));

            // Refresh has a group of its own, between reading and changing.
            var refresh = Array.FindIndex(items, item => item is MenuItem { Header: "Refresh" });
            Assert.IsType<Separator>(items[refresh - 1]);
            Assert.IsType<Separator>(items[refresh + 1]);

            // Icons on every item or on none: none.
            Assert.All(Flatten(menu), item => Assert.Null(item.Icon));
            Assert.Contains(Flatten(menu), item => Equals(item.Header, "Copy Row"));
            Assert.DoesNotContain(Flatten(menu), item => Equals(item.Header, "Copy"));
            Assert.True(menu.PlacementConstraintAdjustment.HasFlag(
                Avalonia.Controls.Primitives.PopupPositioning.PopupPositionerConstraintAdjustment.FlipY));

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

    /// <summary>
    /// Panel ▸ Database repeats the result menu for the current cell: every
    /// item is a menu-bar command, available exactly when the context menu
    /// shows it enabled, and the menu bar's copy runs the same handler.
    /// </summary>
    [Fact]
    public Task TheMenuBarRunsTheResultMenuOnTheCurrentCell() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            var target = (IPanelCommandTarget)view;
            var grid = Named<DataGrid>(view, "Database rows");
            grid.SelectedItem = panel.ResultRows[0];
            grid.CurrentColumn = grid.Columns[0];
            grid.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent });
            window.UpdateLayout();
            var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
            Assert.True(menu.IsOpen);

            var quickFilter = Flatten(menu)
                .Where(item => item.Parent is MenuItem { Name: "QuickFilterMenuItem" })
                .ToArray();
            var commands = Flatten(menu).Where(item => item.Tag is CommandId).Except(quickFilter).ToArray();
            PanelContextMenuAssert.ItemsAreInTheMenuBar(
                commands,
                "JSON", "CSV", "SQL INSERT", "Empty", "NULL", "DEFAULT", "Choose File…");
            foreach (var item in commands.Concat(quickFilter))
            {
                var shown = item.IsVisible && item.IsEnabled
                    && (item.Parent is not MenuItem parent || (parent.IsVisible && parent.IsEnabled));
                Assert.True(
                    shown == target.CanExecute((CommandId)item.Tag!),
                    $"{item.Header}: the context menu says {shown}.");
            }

            // Quick Filter names this cell's value; its menu-bar copies name
            // the comparison, and exactly the comparisons it offers are
            // available.
            Assert.NotEmpty(quickFilter);
            var inMenus = ApplicationMenuLayout.AllCommands().Select(item => item.Command).ToHashSet();
            var offered = quickFilter.Select(item => Assert.IsType<CommandId>(item.Tag)).ToHashSet();
            Assert.Subset(inMenus, offered);
            var operators = panel.GetQuickFilterOperators(0)
                .Select(option => DatabaseWorkspaceView.QuickFilterCommand(option.Operator))
                .ToHashSet();
            Assert.Equal(operators, offered);
            HashSet<CommandId> available =
            [
                .. Enum.GetValues<Asura.Application.DatabaseFilterOperator>()
                    .Select(DatabaseWorkspaceView.QuickFilterCommand)
                    .Where(target.CanExecute),
            ];
            Assert.Equal(offered, available);

            // Every item but the three submenu headings has a menu-bar copy.
            Assert.All(
                Flatten(menu).Where(item => item.Tag is null),
                item => Assert.Contains(item.Name, ["QuickFilterMenuItem", "CopyRowAsMenuItem", "SetValueMenuItem"], StringComparer.Ordinal));
            menu.Close();
            window.UpdateLayout();

            var before = panel.ResultRows.Count;
            Assert.True(target.CanExecute(BuiltInCommands.DatabaseAddRow));
            await target.ExecuteAsync(BuiltInCommands.DatabaseAddRow);
            Assert.Equal(before + 1, panel.ResultRows.Count);
            Assert.Contains(target.States, fact => fact.Key == CommandStates.HasMultipleSelection && !fact.Value);
        });

    /// <summary>
    /// Panel ▸ Database's Filter items run Quick Filter's comparison on the
    /// current cell, as the submenu does.
    /// </summary>
    [Fact]
    public Task TheMenuBarQuickFiltersOnTheCurrentCell() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            var target = (IPanelCommandTarget)view;
            var grid = Named<DataGrid>(view, "Database rows");
            grid.SelectedItem = panel.ResultRows[0];
            grid.CurrentColumn = grid.Columns[0];
            window.UpdateLayout();

            Assert.True(target.CanExecute(BuiltInCommands.DatabaseFilterEqual));
            await target.ExecuteAsync(BuiltInCommands.DatabaseFilterEqual);
            var filter = Assert.Single(panel.FilterRows);
            Assert.Equal(Asura.Application.DatabaseFilterOperator.Equal, filter.Operator?.Operator);
        });

    /// <summary>
    /// Panel ▸ Database repeats the object list's menu for the object the
    /// workspace shows, with the same titles, and runs the same actions.
    /// </summary>
    [Fact]
    public Task TheMenuBarRunsTheObjectMenuOnTheShownObject() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            var target = (IPanelCommandTarget)view;
            var shown = Assert.IsType<DatabaseTableItemViewModel>(panel.SelectedObject);
            var row = view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.DataContext, shown));
            var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
            menu.Open(row);
            window.UpdateLayout();
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(4, items.Length);
            PanelContextMenuAssert.ItemsAreInTheMenuBar(items);
            Assert.All(items, item => Assert.True(target.CanExecute((CommandId)item.Tag!), (string?)item.Header));
            menu.Close();

            Asura.Application.DatabaseTableDescriptor? inTab = null;
            Asura.Application.DatabaseTableDescriptor? inPanel = null;
            view.OpenObjectInTabRequested += (_, descriptor) => inTab = descriptor;
            view.OpenObjectInPanelRequested += (_, descriptor) => inPanel = descriptor;
            await target.ExecuteAsync(BuiltInCommands.DatabaseOpenObjectInTab);
            await target.ExecuteAsync(BuiltInCommands.DatabaseOpenObjectInPanel);
            Assert.Same(shown.Descriptor, inTab);
            Assert.Same(shown.Descriptor, inPanel);
            await target.ExecuteAsync(BuiltInCommands.DatabaseOpenStructure);
            Assert.Equal(DatabaseWorkspaceMode.Structure, panel.SelectedMode);

            // The overview shows no object, so there is nothing to act on.
            panel.ShowDatabaseOverview();
            Assert.False(target.CanExecute(BuiltInCommands.DatabaseOpenObjectInTab));
            Assert.False(target.CanExecute(BuiltInCommands.DatabaseCopyObjectName));
        });

    /// <summary>
    /// The connection button's menu, Reconnect and Disconnect, is in the
    /// menu bar too, titled as the button is.
    /// </summary>
    [Fact]
    public Task TheMenuBarRunsTheConnectionMenu() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseRuntimePanelView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            var target = (IPanelCommandTarget)view;
            var button = view.FindControl<Button>("ConnectionButton")!;
            var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            menu.Open(button);
            window.UpdateLayout();
            var items = menu.Items.OfType<MenuItem>().ToArray();
            PanelContextMenuAssert.ItemsAreInTheMenuBar(items);
            Assert.All(items, item => Assert.Equal(item.IsEnabled, target.CanExecute((CommandId)item.Tag!)));
            menu.Close();

            Assert.Contains(target.States, fact => fact.Key == CommandStates.IsDatabaseConnected && fact.Value);
            Assert.True(target.CanExecute(BuiltInCommands.DatabaseReconnect));
            await target.ExecuteAsync(BuiltInCommands.DatabaseToggleConnection);
            Assert.False(panel.IsConnected);
            Assert.Contains(target.States, fact => fact.Key == CommandStates.IsDatabaseConnected && !fact.Value);
            Assert.False(target.CanExecute(BuiltInCommands.DatabaseReconnect));
            Assert.True(target.CanExecute(BuiltInCommands.DatabaseToggleConnection));
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

    [Fact]
    public Task TheFilterRowFitsANarrowDataColumnAndClearSharesApplysLine() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            Assert.True(panel.CanFilterTable);
            var data = view.GetVisualDescendants().OfType<Grid>()
                .First(grid => string.Equals(Avalonia.Styling.Container.GetName(grid), "databaseData", StringComparison.Ordinal));
            string[] filterControls =
            [
                "Include this filter condition",
                "Filter column",
                "Filter operator",
                "Filter value",
                "Apply the table filter",
                "Remove this filter condition",
                "Add a filter condition",
                "Clear the table filter",
            ];

            // On a roomy panel Clear sits on the same line as Apply.
            var apply = Named<Button>(view, "Apply the table filter");
            var clear = Named<Button>(view, "Clear the table filter");
            // Clear's band takes its height from the condition rows' first
            // line, which settles on the layout pass after the first.
            window.UpdateLayout();
            Assert.Equal(CenterY(apply, view), CenterY(clear, view), 0.5);

            // A table open in a narrow data column — two lines at 520 pt, three
            // at 340 — keeps every filter control inside the column, none
            // overlapping another, and the switch still captioned.
            var host = new Border { HorizontalAlignment = HorizontalAlignment.Left, Width = view.Bounds.Width };
            window.Content = null;
            host.Child = view;
            window.Content = host;
            foreach (var width in new[] { 520.0, 340.0 })
            {
                for (var attempt = 0; attempt < 6 && Math.Abs(data.Bounds.Width - width) > 0.5; attempt++)
                {
                    host.Width -= data.Bounds.Width - width;
                    window.UpdateLayout();
                    window.UpdateLayout();
                }

                Assert.Equal(width, data.Bounds.Width, 0.5);
                // The switch keeps saying what it switches when the row folds:
                // a bare box at the start of a row reads as a row selector.
                var include = Named<CheckBox>(view, "Include this filter condition");
                Assert.Equal("Filter", include.Content);
                var caption = Assert.Single(
                    include.GetVisualDescendants().OfType<TextBlock>(),
                    text => string.Equals(text.Text, "Filter", StringComparison.Ordinal));
                Assert.True(caption.IsEffectivelyVisible);
                Assert.True(
                    caption.DesiredSize.Width <= caption.Bounds.Width + 0.5,
                    $"The Filter caption is clipped in a {width} pt data column.");
                var rects = filterControls
                    .Select(name => Named<Control>(view, name))
                    .Select(control => new Rect(control.TranslatePoint(default, data)!.Value, control.Bounds.Size))
                    .ToArray();
                foreach (var rect in rects)
                {
                    Assert.InRange(rect.Left, 0, data.Bounds.Width);
                    Assert.InRange(rect.Right, 0, data.Bounds.Width + 0.5);
                }

                for (var first = 0; first < rects.Length; first++)
                {
                    for (var second = first + 1; second < rects.Length; second++)
                    {
                        Assert.False(
                            rects[first].Intersects(rects[second]),
                            $"At {width} pt {filterControls[first]} overlaps {filterControls[second]}.");
                    }
                }

                // Clear stays on the switch's line.
                Assert.Equal(CenterY(include, view), CenterY(clear, view), 0.5);
            }

            await Task.CompletedTask;
        });

    [Fact]
    public Task AnOpenInspectorDraftWearsTheSharedFocusRing() =>
        RunAsync(async (panel, window) =>
        {
            panel.SelectRow(panel.ResultRows[0]);
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            var field = panel.SelectedRowFields.First(candidate => candidate.ShowsEditAction && candidate.Cell.UsesTextEditor);

            // Started the way a person starts it, from the field's Edit button.
            var edit = Named<Button>(view, $"Edit {field.Name}");
            edit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var focusManager = TopLevel.GetTopLevel(view)!.FocusManager!;
            Panel? draft = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(10);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                draft = view.GetVisualDescendants().OfType<Panel>()
                    .FirstOrDefault(candidate => candidate.Classes.Contains("InspectorDraft") && candidate.IsEffectivelyVisible);
                if (draft is not null
                    && focusManager.GetFocusedElement() is Visual focused
                    && focused.GetVisualAncestors().Contains(draft))
                {
                    break;
                }
            }

            Assert.True(field.IsEditing);
            Assert.NotNull(draft);
            Assert.Contains(
                draft,
                Assert.IsAssignableFrom<Visual>(focusManager.GetFocusedElement()).GetVisualAncestors());

            // The text field's ring: the accent edge and the shared halo.
            var edge = Assert.Single(draft.Children.OfType<Border>(), border => border.Classes.Contains("InspectorDraftEdge"));
            var ring = Assert.Single(draft.Children.OfType<Border>(), border => border.Classes.Contains("InspectorDraftRing"));
            Assert.True(view.TryFindResource("ShellAccentBrush", view.ActualThemeVariant, out var accent));
            Assert.True(view.TryFindResource("ShellFocusRingShadow", view.ActualThemeVariant, out var halo));
            Assert.Equal(accent, edge.BorderBrush);
            Assert.Equal(halo, ring.BoxShadow);
            // The halo is never on the editor's own border: there it stops the
            // code editor's text layer drawing.
            Assert.Equal(default, edge.BoxShadow);
            Assert.Equal(edge.Bounds, ring.Bounds);

            // Leaving the draft takes the ring away again.
            Named<DataGrid>(view, "Database rows").Focus();
            window.UpdateLayout();
            Assert.NotEqual(accent, edge.BorderBrush);
            Assert.Equal(default, ring.BoxShadow);
        });

    [Fact]
    public Task TheExpandedCellEditorMeetsItsCellAndStaysOverTheGrid() =>
        RunAsync(async (panel, window) =>
        {
            var view = new DatabaseWorkspaceView { DataContext = panel };
            window.Content = view;
            window.UpdateLayout();
            var grid = view.FindControl<DataGrid>("ResultDataGrid")!;
            var text = grid.Columns.LastOrDefault(column =>
                column.Tag is DatabaseResultColumnViewModel descriptor
                && panel.ResultRows[0].Cells[grid.Columns.IndexOf(column)].UsesLargeTextEditor);
            Assert.NotNull(text);
            grid.SelectedItem = panel.ResultRows[0];
            grid.CurrentColumn = text;
            window.UpdateLayout();
            grid.BeginEdit();
            window.UpdateLayout();
            await Task.Delay(20);
            window.UpdateLayout();

            var popup = view.FindControl<Avalonia.Controls.Primitives.Popup>("CellExpandPopup")!;
            Assert.True(popup.IsOpen);
            var surface = view.FindControl<Border>("CellExpandSurface")!;
            var cell = Assert.IsType<DataGridCell>(popup.PlacementTarget);
            Assert.Same(panel.ResultRows[0], cell.DataContext);
            var cellOrigin = cell.TranslatePoint(default, window)!.Value;
            var gridOrigin = grid.TranslatePoint(default, window)!.Value;
            var surfaceOrigin = surface.TranslatePoint(default, window)!.Value;

            // The visible edge, not the shadow margin, meets the cell...
            Assert.Equal(cellOrigin.Y + cell.Bounds.Height, surfaceOrigin.Y, 1.0);
            // ...and the editor stays within the grid's width.
            Assert.InRange(surfaceOrigin.X, gridOrigin.X - 0.5, gridOrigin.X + grid.Bounds.Width);
            Assert.InRange(
                surfaceOrigin.X + surface.Bounds.Width,
                gridOrigin.X,
                gridOrigin.X + grid.Bounds.Width + 0.5);
        });

    private static double CenterY(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height / 2), relativeTo)!.Value.Y;

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
        var headless = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
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
