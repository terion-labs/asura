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
    public Task The_panel_behavior_is_set_before_the_first_show_and_only_once() =>
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
                Assert.Equal([false], visibleWhenConfigured);
                Assert.True(window.IsConfiguredAsPanel);

                // Showing again, or a later reveal, does not configure again.
                window.ShowForReveal(0.5);
                window.Hide();
                window.ShowForReveal(0);
                window.ConfigureAsPanel();
                Assert.Equal([false], visibleWhenConfigured);
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
