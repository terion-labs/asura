using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;

namespace Asura.App.Tests;

/// <summary>
/// A Finder drop on the SQL editor: one small script opens into the statement,
/// anything else types its path, and the whole drop is one Undo.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DatabaseSqlDropTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("asura-sql-drop-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("query.sql")]
    [InlineData("notes.txt")]
    [InlineData("REPORT.SQL")]
    public async Task A_single_small_script_drops_its_text(string name)
    {
        var path = Write(name, "SELECT 1;\nSELECT 2;\n");

        var text = await CodeEditBoxFileDrop.TextForAsync([path], CancellationToken.None);

        Assert.Equal("SELECT 1;\nSELECT 2;\n", text);
    }

    [Fact]
    public async Task A_script_over_one_mebibyte_drops_its_path()
    {
        var path = Write("dump.sql", new string('x', (int)CodeEditBoxFileDrop.MaximumScriptBytes + 1));
        var limit = Write("limit.sql", new string('y', (int)CodeEditBoxFileDrop.MaximumScriptBytes));

        Assert.Equal(path, await CodeEditBoxFileDrop.TextForAsync([path], CancellationToken.None));
        Assert.Equal(
            (int)CodeEditBoxFileDrop.MaximumScriptBytes,
            (await CodeEditBoxFileDrop.TextForAsync([limit], CancellationToken.None)).Length);
    }

    [Fact]
    public async Task Any_other_file_drops_its_path()
    {
        var csv = Write("people.csv", "id,name\n");

        Assert.Equal(csv, await CodeEditBoxFileDrop.TextForAsync([csv], CancellationToken.None));
        Assert.Equal(_folder, await CodeEditBoxFileDrop.TextForAsync([_folder], CancellationToken.None));
    }

    [Fact]
    public async Task Several_files_drop_their_paths_separated_by_spaces()
    {
        var first = Write("a.sql", "SELECT 1;");
        var second = Write("b.sql", "SELECT 2;");

        var text = await CodeEditBoxFileDrop.TextForAsync([first, second], CancellationToken.None);

        Assert.Equal($"{first} {second}", text);
    }

    [Fact]
    public async Task A_missing_script_drops_its_path()
    {
        var missing = Path.Combine(_folder, "gone.sql");

        Assert.Equal(missing, await CodeEditBoxFileDrop.TextForAsync([missing], CancellationToken.None));
    }

    [Fact]
    public Task The_drop_goes_in_at_the_caret_as_one_undoable_edit() =>
        RunHeadlessAsync(() =>
        {
            var editor = new CodeEditBox { Text = "SELECT * FROM ;" };
            var window = new Window { Width = 600, Height = 200, Content = editor };
            try
            {
                window.Show();
                var inner = editor.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
                inner.CaretOffset = "SELECT * FROM ".Length;

                editor.InsertAtCaret("people");

                Assert.Equal("SELECT * FROM people;", editor.Text);
                Assert.Equal("SELECT * FROM people".Length, inner.CaretOffset);
                Assert.True(inner.Document.UndoStack.CanUndo);
                inner.Document.UndoStack.Undo();
                Assert.Equal("SELECT * FROM ;", editor.Text);
                Assert.False(inner.Document.UndoStack.CanUndo);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task A_read_only_editor_takes_no_drop() =>
        RunHeadlessAsync(async () =>
        {
            var editor = new CodeEditBox { Text = "SELECT 1;", IsReadOnly = true, AcceptsFileDrop = true };
            var window = new Window { Width = 600, Height = 200, Content = editor };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.True(DragDrop.GetAllowDrop(editor));
                var data = await FileDragAsync(window, Write("query.sql", "SELECT 2;"));
                var point = CenterOf(editor, window);

                var effects = RecordEffects(window);

                window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.Equal(DragDropEffects.None, effects.Last);
                window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.Equal(DragDropEffects.None, effects.Last);
                window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
                editor.InsertAtCaret("ignored");
                await Task.Delay(50);

                Assert.Equal("SELECT 1;", editor.Text);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Only_an_editor_that_opts_in_takes_files() =>
        RunHeadlessAsync(() =>
        {
            var plain = new CodeEditBox();
            var sql = new CodeEditBox { AcceptsFileDrop = true };

            Assert.False(plain.AcceptsFileDrop);
            Assert.False(DragDrop.GetAllowDrop(plain));
            Assert.True(DragDrop.GetAllowDrop(sql));
            sql.AcceptsFileDrop = false;
            Assert.False(DragDrop.GetAllowDrop(sql));
            return Task.CompletedTask;
        });

    [Fact]
    public Task The_SQL_statement_editor_is_the_one_that_takes_files() =>
        RunHeadlessAsync(() =>
        {
            var view = new DatabaseWorkspaceView();
            var editor = view.FindControl<CodeEditBox>("QueryEditorBox");

            Assert.NotNull(editor);
            Assert.True(editor.AcceptsFileDrop);
            Assert.All(
                view.GetLogicalDescendants().OfType<CodeEditBox>().Where(box => !ReferenceEquals(box, editor)),
                box => Assert.False(box.AcceptsFileDrop));
            return Task.CompletedTask;
        });

    [Fact]
    public Task A_Finder_drag_that_also_carries_text_lands_once() =>
        RunHeadlessAsync(async () =>
        {
            var editor = new CodeEditBox { Text = "", AcceptsFileDrop = true };
            var window = new Window { Width = 600, Height = 200, Content = editor };
            try
            {
                window.Show();
                window.UpdateLayout();
                var path = Write("query.sql", "SELECT 42;");
                // Finder and Linux file managers put the file's URI or name on
                // the pasteboard as text alongside the file itself.
                var data = await FileDragAsync(window, path, alsoAsText: new Uri(path).AbsoluteUri);
                var point = CenterOf(editor, window);

                var effects = RecordEffects(window);

                window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy | DragDropEffects.Move, RawInputModifiers.None);
                Assert.Equal(DragDropEffects.Copy, effects.Last);
                window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy | DragDropEffects.Move, RawInputModifiers.None);
                Assert.Equal(DragDropEffects.Copy, effects.Last);
                Assert.True(editor.FindControl<Border>("FileDropHighlight")!.IsVisible);
                window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy | DragDropEffects.Move, RawInputModifiers.None);
                await WaitUntilAsync(() => editor.Text == "SELECT 42;");
                await Task.Delay(50);

                Assert.Equal("SELECT 42;", editor.Text);
                Assert.False(editor.FindControl<Border>("FileDropHighlight")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task A_drag_without_files_is_left_to_the_text_area() =>
        RunHeadlessAsync(async () =>
        {
            var editor = new CodeEditBox { Text = "", AcceptsFileDrop = true };
            var window = new Window { Width = 600, Height = 200, Content = editor };
            try
            {
                window.Show();
                window.UpdateLayout();
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText("people"));
                var point = CenterOf(editor, window);

                window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.False(editor.FindControl<Border>("FileDropHighlight")!.IsVisible);
                window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
                await WaitUntilAsync(() => editor.Text == "people");
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task<DataTransfer> FileDragAsync(Window window, string path, string? alsoAsText = null)
    {
        var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
        Assert.NotNull(file);
        var item = DataTransferItem.CreateFile(file);
        if (alsoAsText is not null)
        {
            item.SetText(alsoAsText);
        }

        var data = new DataTransfer();
        data.Add(item);
        return data;
    }

    /// <summary>What the drag finally answers, as the window sees it last.</summary>
    private static EffectsRecord RecordEffects(Window window)
    {
        var record = new EffectsRecord();
        void Capture(object? sender, DragEventArgs e) => record.Last = e.DragEffects;
        window.AddHandler(DragDrop.DragEnterEvent, Capture, RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(DragDrop.DragOverEvent, Capture, RoutingStrategies.Bubble, handledEventsToo: true);
        return record;
    }

    private sealed class EffectsRecord
    {
        public DragDropEffects? Last { get; set; }
    }

    private static Point CenterOf(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The editor did not reach the expected text.");
            await Task.Delay(10);
        }
    }

    private string Write(string name, string contents)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(
                async () =>
                {
                    await assertion();
                    return true;
                },
                timeout.Token));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
