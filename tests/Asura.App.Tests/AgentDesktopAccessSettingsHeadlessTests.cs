using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Settings › AI shows the desktop automation row (ADR 0061): its state as
/// the session host reports it, read when the page is shown, and the way to
/// System Settings only while access is what is missing on a Mac.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class AgentDesktopAccessSettingsHeadlessTests
{
    [Fact]
    public async Task The_AI_page_shows_the_state_and_reads_it_again_each_time_it_is_shown()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync(host);
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                // Opening Asura reads nothing: the row is the only place the
                // system's accessibility setting is consulted.
                Assert.Equal(0, host.ReadCount);

                window.NavigateToSettings(SettingsPage.Agent);
                Dispatcher.UIThread.RunJobs();
                var view = SettingsViewOnShow();
                var label = view.FindControl<TextBlock>("AgentDesktopAccessStateLabel")!;
                var openButton = view.FindControl<Button>("OpenAccessibilitySystemSettingsButton")!;

                Assert.True(view.FindControl<Control>("AgentDesktopAccessGroup")!.IsEffectivelyVisible);
                Assert.Equal("Access not granted", label.Text);
                Assert.Equal(OperatingSystem.IsMacOS(), openButton.IsEffectivelyVisible);
                Assert.True(host.ReadCount > 0);

                // Granted in System Settings meanwhile: showing the page again
                // reads the new answer.
                host.State = AgentDesktopAccessState.Granted;
                var readsOnTheAiPage = host.ReadCount;
                Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Notifications));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(readsOnTheAiPage, host.ReadCount);
                Assert.True(await window.RequestSettingsPageAsync(SettingsPage.Agent));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Ready", label.Text);
                Assert.False(openButton.IsVisible);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
            }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_desktop_host_the_AI_page_shows_the_row_as_unavailable()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings(SettingsPage.Agent);
                Dispatcher.UIThread.RunJobs();
                var view = SettingsViewOnShow();

                Assert.Equal(AgentDesktopAccessState.Unavailable, viewModel.AgentDesktopAccess.State);
                Assert.Equal("Unavailable", view.FindControl<TextBlock>("AgentDesktopAccessStateLabel")!.Text);
                Assert.False(view.FindControl<Button>("OpenAccessibilitySystemSettingsButton")!.IsVisible);
                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
            }
        }, CancellationToken.None));
    }

    /// <summary>
    /// ADR 0061: Auto authorizes desktop app data only, so the default
    /// policy's picker keeps Auto for that row and disables it for desktop
    /// app interaction and control.
    /// </summary>
    [Fact]
    public async Task The_AI_page_disables_Auto_for_desktop_app_interaction_and_control()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            ForgetSettingsWindowState();
            using var viewModel = await MainWindowRuntimeGraphIntegrationTests.CreateViewModelWithOpenWorkspaceAsync();
            var window = new MainWindow { DataContext = viewModel, Width = 1400, Height = 900 };
            window.Show();
            try
            {
                window.NavigateToSettings(SettingsPage.Agent);
                Dispatcher.UIThread.RunJobs();
                var view = SettingsViewOnShow();
                var permissions = view.GetVisualDescendants()
                    .OfType<Expander>()
                    .Single(expander => AutomationProperties.GetName(expander) == "Default agent tool permissions");
                permissions.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();
                view.UpdateLayout();

                foreach (var capability in AgentPolicy.DesktopCapabilities)
                {
                    var name = $"{AgentPolicyPresentation.CapabilityName(capability)} permission";
                    var picker = permissions.GetVisualDescendants()
                        .OfType<SegmentedControl>()
                        .Single(control => AutomationProperties.GetName(control) == name);
                    var segments = picker.Items.OfType<RadioButton>().ToArray();

                    Assert.Equal(["Off", "Ask", "Auto"], segments.Select(segment => segment.Content));
                    Assert.True(segments[0].IsEffectivelyEnabled);
                    Assert.True(segments[1].IsEffectivelyEnabled);
                    Assert.Equal(
                        AgentPolicyPresentation.OffersAuto(capability),
                        segments[2].IsEffectivelyEnabled);
                    Assert.Equal(0, picker.SelectedIndex);
                }

                return true;
            }
            finally
            {
                SettingsWindow.Current?.CloseForHost();
                window.Close();
            }
        }, CancellationToken.None));
    }

    private static SettingsView SettingsViewOnShow()
    {
        var settings = Assert.IsType<SettingsWindow>(SettingsWindow.Current);
        settings.UpdateLayout();
        return settings.GetVisualDescendants().OfType<SettingsView>().Single();
    }

    private static void ForgetSettingsWindowState()
    {
        SettingsWindow.StateStore = null;
        SettingsWindow.ResetState();
    }
}
