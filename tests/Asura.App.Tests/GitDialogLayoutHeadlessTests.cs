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
    public Task Lfs_lock_rows_are_inset_like_file_history_and_start_a_step_below_the_segments() => RunAsync(() =>
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

            var segments = dialog.FindControl<SegmentedControl>("LfsViewSegments")!;
            var segmentsBottom = segments.TranslatePoint(new Point(0, segments.Bounds.Height), dialog)!.Value.Y;
            var listTop = dialog.LocksList.TranslatePoint(default, dialog)!.Value.Y;
            Assert.True(listTop - segmentsBottom >= small - 0.5, $"The list starts {listTop - segmentsBottom} below the segments.");
        }
        finally
        {
            dialog.Close();
        }
    });

    /// <summary>
    /// The conflict, file history and LFS windows switch among a few fixed
    /// views with the shared segmented control, as Kubernetes does, not with
    /// a tab strip of their own: choosing a segment shows its view, and the
    /// tab view underneath draws no strip.
    /// </summary>
    [Fact]
    public Task Git_inspectors_switch_views_with_the_shared_segmented_control() => RunAsync(() =>
    {
        var conflict = new GitConflictDialog();
        var history = new GitFileHistoryDialog();
        var lfs = new GitLfsDialog();
        (Window Dialog, string Segments, string Views, int Count)[] cases =
        [
            (conflict, "VersionSegments", "VersionViews", 4),
            (history, "RevisionViewSegments", "RevisionViews", 3),
            (lfs, "LfsViewSegments", "LfsViews", 3),
        ];
        foreach (var (dialog, segmentsName, viewsName, count) in cases)
        {
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var segments = dialog.FindControl<SegmentedControl>(segmentsName)!;
                var views = dialog.FindControl<TabControl>(viewsName)!;
                Assert.Equal(count, segments.ItemCount);
                Assert.Equal(count, views.ItemCount);
                Assert.Contains("Headless", views.Classes, StringComparer.Ordinal);
                Assert.DoesNotContain(views.GetVisualDescendants().OfType<TabItem>(), tab => tab.IsEffectivelyVisible);
                Assert.Equal(0, views.SelectedIndex);

                var last = segments.GetVisualDescendants().OfType<RadioButton>().Last();
                last.IsChecked = true;
                dialog.UpdateLayout();

                Assert.Equal(count - 1, segments.SelectedIndex);
                Assert.Equal(count - 1, views.SelectedIndex);
            }
            finally
            {
                dialog.Close();
            }
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
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            assertion();
            return true;
        }, timeout.Token));
    }
}
