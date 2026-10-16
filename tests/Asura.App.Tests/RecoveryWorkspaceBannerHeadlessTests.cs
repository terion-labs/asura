using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A recovery workspace looked exactly like every workspace having been
/// deleted: it said what it was only in the window title, which the title bar
/// hides, and only the first time it was made. Every window now carries a
/// banner saying the workspaces are safe, why they didn't open, and how to
/// get back to them.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class RecoveryWorkspaceBannerHeadlessTests
{
    [Fact]
    public async Task A_recovery_run_says_so_in_every_window_and_offers_the_way_back()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var app = Assert.IsType<App>(Avalonia.Application.Current);
            var reopened = 0;
            app.RecoveryWorkspace = new RecoveryWorkspaceNotice(
                "Another Asura process still has your profile open and isn’t responding.",
                () => reopened++);
            try
            {
                foreach (var _ in new[] { "first window", "second window" })
                {
                    var workspace = new WorkspaceView();
                    var window = new Window { Width = 1280, Height = 800, Content = workspace };
                    window.Show();
                    try
                    {
                        var banner = workspace.FindControl<Callout>("RecoveryWorkspaceBanner")!;
                        Assert.True(banner.IsEffectivelyVisible);
                        Assert.True(banner.Bounds.Height > 0);
                        Assert.Contains("safe", banner.Text, StringComparison.Ordinal);
                        Assert.Equal(
                            "Another Asura process still has your profile open and isn’t responding.",
                            banner.Footnote);
                        var open = Assert.Single(
                            banner.GetVisualDescendants().OfType<Button>(),
                            button => Equals(button.Content, "Open My Workspaces"));
                        open.Command!.Execute(null);
                    }
                    finally
                    {
                        window.Close();
                    }
                }

                Assert.Equal(2, reopened);
            }
            finally
            {
                app.RecoveryWorkspace = null;
            }

            return true;
        }, timeout.Token));
    }

    [Fact]
    public async Task The_persons_own_profile_shows_no_recovery_banner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var workspace = new WorkspaceView();
            var window = new Window { Width = 1280, Height = 800, Content = workspace };
            window.Show();
            try
            {
                Assert.False(workspace.FindControl<Callout>("RecoveryWorkspaceBanner")!.IsVisible);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, timeout.Token));
    }
}
