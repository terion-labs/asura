using Asura.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// Quick Terminal is a panel before it is ever shown: a window ordered in
/// without the panel behavior lands on the desktop's Space and pulls the
/// person out of the full-screen app they summoned it over.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class QuickTerminalRevealHeadlessTests
{
    [Fact]
    public Task The_panel_behavior_is_set_before_the_first_show_and_after_every_show() =>
        RunHeadlessAsync(() =>
        {
            var visibleWhenConfigured = new List<bool>();
            var window = new QuickTerminalWindow
            {
                PanelConfigurator = target =>
                {
                    visibleWhenConfigured.Add(target.IsVisible);
                    return true;
                },
            };
            try
            {
                window.ShowForReveal(0);
                Assert.True(window.IsVisible);
                // The first call happens while the window is still hidden;
                // the second re-applies it once the window is on screen.
                Assert.Equal([false, true], visibleWhenConfigured);
                Assert.True(window.IsConfiguredAsPanel);

                // Every later show applies the behavior again, before and
                // after, and calling it directly is safe.
                window.Hide();
                visibleWhenConfigured.Clear();
                window.ShowForReveal(0);
                Assert.Equal([false, true], visibleWhenConfigured);
                window.ConfigureAsPanel();
                Assert.True(window.IsConfiguredAsPanel);
            }
            finally
            {
                window.ClosePermanently();
            }
        });

    [Fact]
    public Task A_platform_without_a_native_window_yet_is_configured_once_it_has_one() =>
        RunHeadlessAsync(() =>
        {
            var visibleWhenConfigured = new List<bool>();
            var window = new QuickTerminalWindow
            {
                // No native window until the first show.
                PanelConfigurator = target =>
                {
                    visibleWhenConfigured.Add(target.IsVisible);
                    return target.IsVisible;
                },
            };
            try
            {
                window.ConfigureAsPanel();
                Assert.False(window.IsConfiguredAsPanel);

                window.ShowForReveal(0);
                Assert.Equal([false, false, true], visibleWhenConfigured);
                Assert.True(window.IsConfiguredAsPanel);
            }
            finally
            {
                window.ClosePermanently();
            }
        });

    private static async Task RunHeadlessAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                assertion();
                return Task.FromResult(true);
            },
            timeout.Token));
    }
}
