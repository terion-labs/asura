using Asura.App.Views.SettingsPages;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// System is the visible default for the colour mode; Dark and Light are an
/// override, kept under a disclosure that opens when one is already in force.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class AppearanceOverrideHeadlessTests
{
    [Theory]
    [InlineData(AppearanceMode.System, false)]
    [InlineData(AppearanceMode.Dark, true)]
    [InlineData(AppearanceMode.Light, true)]
    public Task The_override_disclosure_opens_only_for_a_stored_override(
        AppearanceMode mode,
        bool expanded) => RunAsync(() =>
    {
        var (page, window) = Show();
        try
        {
            page.ApplyAppearance(Theme(mode), TextScale);

            Assert.Equal(expanded, page.AppearanceOverrideExpander.IsExpanded);
            Assert.Equal(mode, page.CaptureAppearance().Appearance);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task The_three_modes_stay_exclusive_across_the_disclosure() => RunAsync(() =>
    {
        var (page, window) = Show();
        try
        {
            page.ApplyAppearance(Theme(AppearanceMode.System), TextScale);
            var changes = 0;
            page.ApplicationAppearanceChanged += (_, _) => changes++;

            page.AppearanceModeDark.IsChecked = true;
            Assert.False(page.AppearanceModeSystem.IsChecked);
            Assert.Equal(AppearanceMode.Dark, page.CaptureAppearance().Appearance);

            page.AppearanceModeLight.IsChecked = true;
            Assert.False(page.AppearanceModeDark.IsChecked);
            Assert.Equal(AppearanceMode.Light, page.CaptureAppearance().Appearance);

            page.AppearanceModeSystem.IsChecked = true;
            Assert.False(page.AppearanceModeLight.IsChecked);
            Assert.Equal(AppearanceMode.System, page.CaptureAppearance().Appearance);
            Assert.True(changes > 0);
        }
        finally
        {
            window.Close();
        }
    });

    private static readonly AppearanceTextScaleOption TextScale = new("Follow host", null);

    private static (AppearanceSettingsPageView Page, Window Window) Show()
    {
        var page = new AppearanceSettingsPageView();
        page.ConfigureAppearanceControls(Enum.GetValues<PlatformProfile>(), [TextScale]);
        var window = new Window { Content = page };
        window.Show();
        return (page, window);
    }

    private static ThemePreference Theme(AppearanceMode mode) =>
        new(
            ThemePreference.Default.Id,
            ThemePreference.Default.Name,
            mode,
            PlatformProfile.Automatic,
            AccentPreference.FollowHost);

    private static Task RunAsync(Action action) => RunAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });

    private static async Task RunAsync(Func<Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(async () => { await action(); return true; }, timeout.Token));
        }
        finally { await session.DisposeAsync(); }
    }
}
