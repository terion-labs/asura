using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Diff lines draw their own selected state: an added line keeps its tint
/// when it is selected for staging. The shell's accent selection must not
/// reach them, or the code text turns to the accent's foreground on a
/// background that is not the accent.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class GitDiffSelectionHeadlessTests
{
    [Fact]
    public async Task A_selected_diff_line_in_a_focused_diff_keeps_its_text_colour()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        // Returning a value picks the overload that awaits the test body; a
        // bare async lambda would be dispatched as a Task<Task> and its
        // failures dropped.
        var completed = await session.Dispatch(async () =>
        {
            using var panel = new GitRuntimePanelViewModel(PanelInstanceId.New(), "Git", new FakeGitRepositoryClient(), BuiltInConnections.Local);
            await panel.OpenRepositoryAsync("/repo");
            await panel.DiffLoading;
            var view = new GitDiffView { DataContext = panel };
            var window = new Window { Width = 900, Height = 600, Content = view };
            // A foreground nothing in the diff draws otherwise, so text that took
            // the accent's foreground cannot pass for text that kept its own.
            window.Resources["ShellAccentForegroundBrush"] = new SolidColorBrush(Colors.Magenta);
            try
            {
                window.Show();
                window.UpdateLayout();

                var lines = view.FindControl<ListBox>("UnifiedLines")!;
                var added = panel.DiffLines.First(line => line.IsAdded);
                var row = Assert.IsType<ListBoxItem>(lines.ContainerFromItem(added));
                var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.NotEmpty(texts);
                Color?[] resting = [.. texts.Select(text => Color(text.Foreground))];

                lines.SelectedItem = added;
                Assert.True(row.Focus(NavigationMethod.Tab));
                window.UpdateLayout();

                Color?[] selected = [.. texts.Select(text => Color(text.Foreground))];
                Assert.Equal(resting, selected);
                Assert.DoesNotContain(Colors.Magenta, selected.OfType<Color>());
                var accent = window.TryFindResource("ShellAccentBrush", window.ActualThemeVariant, out var value)
                    ? Color(value as IBrush)
                    : null;
                Assert.NotEqual(
                    accent,
                    Color(row.GetVisualDescendants().OfType<ContentPresenter>().First(part => part.Name == "PART_ContentPresenter").Background));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, timeout.Token);
        Assert.True(completed);
    }

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;
}
