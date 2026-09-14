using Asura.App.Views;
using Asura.App.Views.SettingsPages;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class MainWindowAppearanceTests
{
    [Theory]
    [InlineData("AppearanceModeSystem", "AppearanceModeDark")]
    [InlineData("TabPlacementTop", "TabPlacementLeft")]
    [InlineData("WorkspacePanelLeft", "WorkspacePanelRight")]
    public async Task Detached_appearance_pages_keep_radio_selections_independent(
        string firstOptionName,
        string secondOptionName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessUnitTestSession.StartNew(
            typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                var firstPage = new AppearanceSettingsPageView();
                var secondPage = new AppearanceSettingsPageView();
                var firstSelection = firstPage.FindControl<RadioButton>(firstOptionName)!;
                var secondSelection = secondPage.FindControl<RadioButton>(firstOptionName)!;
                var alternative = secondPage.FindControl<RadioButton>(secondOptionName)!;

                firstSelection.IsChecked = true;
                secondSelection.IsChecked = true;
                Assert.True(firstSelection.IsChecked);
                Assert.True(secondSelection.IsChecked);

                alternative.IsChecked = true;
                Assert.True(firstSelection.IsChecked);
                Assert.False(secondSelection.IsChecked);
                Assert.True(alternative.IsChecked);
                return Task.FromResult(true);
            },
            timeout.Token));
    }

    [Fact]
    public void Platform_profile_picker_includes_every_durable_profile()
    {
        Assert.Equal(
            Enum.GetValues<PlatformProfile>(),
            MainWindow.AppearancePlatformProfiles);
        Assert.Contains(
            PlatformProfile.Custom,
            MainWindow.AppearancePlatformProfiles);
    }

    [Fact]
    public async Task Appearance_refresh_reapplies_the_quick_terminal_backdrop()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(
            typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var window = new QuickTerminalWindow
                    {
                        TransparencyLevelHint =
                        [
                            WindowTransparencyLevel.AcrylicBlur,
                            WindowTransparencyLevel.Blur,
                        ],
                    };

                    App.RefreshWindowBackdrop(window);

                    Assert.Equal(
                        [WindowTransparencyLevel.Transparent],
                        window.TransparencyLevelHint);
                    return Task.FromResult(true);
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
