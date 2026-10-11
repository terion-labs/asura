using Asura.App.Controls;
using Asura.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// The agent panel shares the workspace's right edge with the file transfer
/// card, and both have to stay usable at every text size.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class WorkspaceAgentOverlayHeadlessTests
{
    [Fact]
    public async Task File_transfers_sit_beside_the_agent_panel_instead_of_over_its_composer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var workspace = new WorkspaceView();
            var window = new Window { Width = 1440, Height = 900, Content = workspace };
            try
            {
                window.Show();
                var transfers = workspace.FindControl<SurfaceCard>("FileTransferManager")!;
                var overlay = workspace.FindControl<Panel>("AgentOverlay")!;
                var agent = workspace.FindControl<AgentWorkspaceView>("AgentWorkspaceSurface")!;
                transfers.IsVisible = true;
                overlay.IsVisible = true;
                window.UpdateLayout();

                var agentLeft = agent.TranslatePoint(default, workspace)!.Value.X;
                var transfersRight = transfers.TranslatePoint(
                    new Point(transfers.Bounds.Width, 0),
                    workspace)!.Value.X;
                Assert.True(
                    transfersRight < agentLeft,
                    $"Transfers end at {transfersRight}; the agent panel starts at {agentLeft}.");

                overlay.IsVisible = false;
                window.UpdateLayout();
                var edgeInset = workspace.Bounds.Width - transfers.TranslatePoint(
                    new Point(transfers.Bounds.Width, 0),
                    workspace)!.Value.X;
                Assert.True(edgeInset < 40, $"Transfers stay {edgeInset} from the window edge.");
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    [Fact]
    public async Task Error_notices_make_room_for_the_agent_panel_in_the_bottom_corner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var workspace = new WorkspaceView();
            var window = new Window { Width = 1440, Height = 900, Content = workspace };
            try
            {
                window.Show();
                var overlay = workspace.FindControl<Panel>("AgentOverlay")!;
                var agent = workspace.FindControl<AgentWorkspaceView>("AgentWorkspaceSurface")!;
                overlay.IsVisible = true;
                window.UpdateLayout();

                // The notices stand off by the reserved width, which ends at
                // the agent panel's left edge.
                var agentLeft = agent.TranslatePoint(default, workspace)!.Value.X;
                Assert.True(workspace.AgentPanelReservedWidth > 0);
                Assert.True(
                    workspace.Bounds.Width - workspace.AgentPanelReservedWidth <= agentLeft + 0.5,
                    $"Reserved {workspace.AgentPanelReservedWidth}; the agent panel starts at {agentLeft}.");

                overlay.IsVisible = false;
                window.UpdateLayout();
                Assert.Equal(0, workspace.AgentPanelReservedWidth);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    [Theory]
    [InlineData("Queued", "Default")]
    [InlineData("Running", "Default")]
    [InlineData("Completed", "Success")]
    [InlineData("Failed", "Danger")]
    [InlineData("Canceled", "Default")]
    public void Transfer_chips_take_the_tone_of_their_state(string state, string tone)
    {
        Assert.Equal(
            Enum.Parse<SurfaceTone>(tone),
            WorkspaceView.TransferStateTone.Convert(state, typeof(SurfaceTone), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task An_unbound_error_notice_list_takes_no_space()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var notices = new Asura.App.Views.Components.ErrorNoticesView();
            var window = new Window { Width = 400, Height = 300, Content = notices };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.False(notices.IsVisible);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    [Fact]
    public async Task File_transfers_stack_above_the_composer_when_there_is_no_room_beside_the_agent_panel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var workspace = new WorkspaceView();
            var window = new Window { Width = 560, Height = 900, Content = workspace };
            try
            {
                window.Show();
                var transfers = workspace.FindControl<SurfaceCard>("FileTransferManager")!;
                var overlay = workspace.FindControl<Panel>("AgentOverlay")!;
                var agent = workspace.FindControl<AgentWorkspaceView>("AgentWorkspaceSurface")!;
                var composer = agent.FindControl<Border>("AgentComposer")!;
                // Without an agent conversation bound the composer is hidden;
                // show it as a bound conversation would.
                composer.IsVisible = true;
                transfers.IsVisible = true;
                overlay.IsVisible = true;
                window.UpdateLayout();
                Assert.True(
                    transfers.TranslatePoint(new Point(transfers.Bounds.Width, 0), workspace)!.Value.X
                        > agent.TranslatePoint(default, workspace)!.Value.X,
                    "The window is too narrow for the card to sit beside the agent panel.");

                // The card follows the composer after each agent layout pass,
                // so it settles on the pass after the one that placed the
                // composer.
                window.UpdateLayout();

                var composerTop = composer.TranslatePoint(default, workspace)!.Value.Y;
                var transfersBottom = transfers.TranslatePoint(
                    new Point(0, transfers.Bounds.Height),
                    workspace)!.Value.Y;
                Assert.True(
                    transfersBottom < composerTop,
                    $"Transfers end at {transfersBottom}; the composer starts at {composerTop}.");
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    [Fact]
    public async Task Agent_panel_grows_with_the_text_size_setting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var workspace = new WorkspaceView();
            var window = new Window { Width = 1440, Height = 1400, Content = workspace };
            window.Resources["ShellFontSize13"] = 13d;
            try
            {
                window.Show();
                var overlay = workspace.FindControl<Panel>("AgentOverlay")!;
                var agent = workspace.FindControl<AgentWorkspaceView>("AgentWorkspaceSurface")!;
                overlay.IsVisible = true;
                agent.Classes.Add("floating");
                window.UpdateLayout();
                Assert.Equal(352, agent.Bounds.Width);
                Assert.Equal(620, agent.Bounds.Height);

                window.Resources["ShellFontSize13"] = 19.5d;
                window.UpdateLayout();

                Assert.Equal(528, agent.Bounds.Width);
                Assert.Equal(930, agent.Bounds.Height);
                Assert.Equal(528, TextScaledExtension.Scale(352, 19.5d));
                Assert.Equal(352, TextScaledExtension.Scale(352, null));
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }
}
