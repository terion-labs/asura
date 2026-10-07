using Avalonia.Controls;
using Avalonia.Headless;
using AvaloniaEdit;

namespace Asura.App.Tests;

/// <summary>
/// The tmux-like prefix must not arm while a control takes typed text: in a
/// text field, the SQL editor, or a web page, Control-B belongs to that control.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ApplicationKeyFocusHeadlessTests
{
    [Fact]
    public Task Text_fields_and_code_editors_own_the_prefix_key() =>
        RunHeadlessAsync(() =>
        {
            var field = new TextBox();
            var editor = new TextEditor();
            var button = new Button();
            var window = new Window
            {
                Content = new StackPanel { Children = { field, editor, button } },
            };
            window.Show();
            try
            {
                Assert.True(ApplicationKeyFocus.OwnsTextInput(field));
                Assert.True(ApplicationKeyFocus.OwnsTextInput(editor.TextArea));
                Assert.False(ApplicationKeyFocus.OwnsTextInput(button));
                Assert.False(ApplicationKeyFocus.OwnsTextInput(null));
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task RunHeadlessAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    assertion();
                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
