using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The Git inspectors' lists read as the rest of the shell's lists do: rows
/// with room around them, and counts whose digits line up and sit on the
/// line beside them.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class GitDialogLayoutHeadlessTests
{
    [Fact]
    public Task Lfs_lock_rows_are_inset_like_file_history_and_start_a_step_below_the_tabs() => RunAsync(() =>
    {
        var dialog = new GitLfsDialog();
        dialog.LocksList.ItemsSource = new[]
        {
            new GitLfsLockItemViewModel(new GitLfsLock("1", "assets/logo.psd", "Ada", GitLfsLockOwnership.CurrentUser, null)),
            new GitLfsLockItemViewModel(new GitLfsLock("2", "assets/hero.psd", "Grace", GitLfsLockOwnership.OtherUser, null)),
        };
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var small = Space(dialog, "ShellSpaceSm");
            var extraSmall = Space(dialog, "ShellSpaceXs");

            var rows = dialog.LocksList.GetVisualDescendants().OfType<ListItem>().ToArray();
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row => Assert.Equal(new Thickness(small, extraSmall), row.ContentPadding));

            // The first row's path sits inside its row, not against its edge.
            var title = rows[0].GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Title");
            var container = Assert.IsType<ListBoxItem>(dialog.LocksList.ContainerFromIndex(0));
            var titleOrigin = title.TranslatePoint(default, container)!.Value;
            Assert.True(titleOrigin.X >= small && titleOrigin.Y >= extraSmall, $"{titleOrigin}");

            var tabs = dialog.LocksList.FindAncestorOfType<TabControl>()!;
            var tab = tabs.GetVisualDescendants().OfType<TabItem>().First();
            var tabBottom = tab.TranslatePoint(new Point(0, tab.Bounds.Height), tabs)!.Value.Y;
            var listTop = dialog.LocksList.TranslatePoint(default, tabs)!.Value.Y;
            Assert.True(listTop - tabBottom >= small - 0.5, $"The list starts {listTop - tabBottom} below the tabs.");
        }
        finally
        {
            dialog.Close();
        }
    });

    [Fact]
    public Task Contributor_counts_use_tabular_lining_figures_on_the_authors_baseline() => RunAsync(() =>
    {
        var dialog = new GitStatisticsDialog(new GitRepositoryStatistics(
            [new GitDirectorySize("src", 15_000, 3)],
            "  1042\tAda Lovelace <ada@example.com>\n     7\tGrace Hopper <grace@example.com>\n"));
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var counts = dialog.Contributors.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Name == "CommitCount").ToArray();
            Assert.Equal(2, counts.Length);
            foreach (var count in counts)
            {
                var features = Assert.IsType<Avalonia.Media.FontFeatureCollection>(count.FontFeatures);
                Assert.Contains(features, feature => feature.Tag == "tnum" && feature.Value == 1);
                Assert.Contains(features, feature => feature.Tag == "lnum" && feature.Value == 1);

                var author = count.GetVisualParent()!.GetVisualChildren().OfType<TextBlock>().Single(text => text != count);
                Assert.Equal(author.FontFamily, count.FontFamily);
                var countBaseline = count.TranslatePoint(new Point(0, count.TextLayout.TextLines[0].Baseline), dialog)!.Value.Y;
                var authorBaseline = author.TranslatePoint(new Point(0, author.TextLayout.TextLines[0].Baseline), dialog)!.Value.Y;
                Assert.Equal(authorBaseline, countBaseline, precision: 1);
            }

            // Rows are spaced: a step of air between one contributor's line
            // and the next, not two lines set solid.
            var firstTop = counts[0].TranslatePoint(default, dialog)!.Value.Y;
            var secondTop = counts[1].TranslatePoint(default, dialog)!.Value.Y;
            var gap = secondTop - (firstTop + counts[0].Bounds.Height);
            Assert.True(gap >= Space(dialog, "ShellSpaceSm") - 0.5, $"Contributor rows are {gap} apart.");
        }
        finally
        {
            dialog.Close();
        }
    });

    private static double Space(Control owner, string key)
    {
        Assert.True(owner.TryFindResource(key, out var value));
        return Assert.IsType<double>(value);
    }

    private static async Task RunAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            assertion();
            return true;
        }, timeout.Token));
    }
}
