using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

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
        });

    [Fact]
    public Task A_read_only_editor_takes_no_drop() =>
        RunHeadlessAsync(() =>
        {
            var editor = new CodeEditBox { Text = "SELECT 1;", IsReadOnly = true };
            var window = new Window { Width = 600, Height = 200, Content = editor };
            try
            {
                window.Show();
                Assert.True(DragDrop.GetAllowDrop(editor));

                editor.InsertAtCaret("ignored");

                Assert.Equal("SELECT 1;", editor.Text);
            }
            finally
            {
                window.Close();
            }
        });

    private string Write(string name, string contents)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static async Task RunHeadlessAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(
                () =>
                {
                    assertion();
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
