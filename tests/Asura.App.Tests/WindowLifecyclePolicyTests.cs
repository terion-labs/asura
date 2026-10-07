using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia;
using AvaloniaDock = Avalonia.Controls.Dock;

namespace Asura.App.Tests;

public sealed class WindowLifecyclePolicyTests
{
    private static readonly PixelRect Primary = new(0, 0, 2880, 1800);
    private static readonly PixelRect Secondary = new(2880, 0, 1920, 1080);

    [Fact]
    public void Quit_alert_counts_sessions_across_windows_and_quick_terminal()
    {
        var census = RunningSessionCensus.Empty
            .WithWindow([Session("build"), Session("tail")])
            .WithWindow([])
            .WithWindow([Session("ssh")])
            .WithQuickTerminal([Session("top")]);

        Assert.True(census.RequiresConfirmation);
        Assert.Equal(4, census.Sessions.Count);
        Assert.Equal(
            "4 sessions in 2 windows and Quick Terminal are still running. Quitting ends them.",
            census.Describe());
    }

    [Fact]
    public void Quit_alert_reads_naturally_for_one_session()
    {
        Assert.Equal(
            "1 session in 1 window is still running. Quitting ends it.",
            RunningSessionCensus.Empty.WithWindow([Session("build")]).Describe());
        Assert.Equal(
            "1 session in Quick Terminal is still running. Quitting ends it.",
            RunningSessionCensus.Empty.WithQuickTerminal([Session("top")]).Describe());
    }

    [Fact]
    public void Idle_windows_need_no_quit_alert_but_an_unanswered_one_does()
    {
        var idle = RunningSessionCensus.Empty.WithWindow([]).WithQuickTerminal([]);
        var unknown = RunningSessionCensus.Empty.WithWindow([]).WithWindow(null);

        Assert.False(idle.RequiresConfirmation);
        Assert.True(unknown.RequiresConfirmation);
        Assert.Contains("couldn't confirm", unknown.Describe(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, false, null, "Asura")]
    [InlineData("Main", null, false, null, "Main")]
    [InlineData("Main", "zsh", false, null, "Main · zsh")]
    [InlineData("Main", "zsh", true, null, "Settings")]
    [InlineData("Main", "zsh", false, "Recovery Workspace", "Main · zsh — Recovery Workspace")]
    public void Window_titles_name_the_workspace_and_tab(
        string? workspace,
        string? tab,
        bool settings,
        string? qualifier,
        string expected)
    {
        Assert.Equal(expected, ShellWindowTitle.Compose(workspace, tab, settings, qualifier));
    }

    [Fact]
    public void A_frame_still_on_screen_keeps_its_place()
    {
        var saved = new PixelRect(3000, 100, 1200, 800);

        Assert.Equal(saved, WindowPlacementPolicy.Clamp(saved, [Primary, Secondary], Primary));
    }

    [Fact]
    public void A_frame_hanging_off_a_screen_is_pulled_back_on_and_fitted()
    {
        var saved = new PixelRect(2400, -50, 3000, 900);

        var clamped = WindowPlacementPolicy.Clamp(saved, [Primary], Primary);

        Assert.Equal(new PixelRect(0, 0, 2880, 900), clamped);
    }

    [Fact]
    public void A_frame_left_on_a_disconnected_display_is_centred_on_the_primary_screen()
    {
        var saved = new PixelRect(6000, 200, 1000, 600);

        var clamped = WindowPlacementPolicy.Clamp(saved, [Primary], Primary);

        Assert.Equal(new PixelRect(940, 600, 1000, 600), clamped);
    }

    [Fact]
    public void New_windows_cascade_by_one_title_bar_and_wrap_at_the_screen_edge()
    {
        var front = new PixelRect(100, 100, 1200, 800);

        Assert.Equal(
            new PixelPoint(156, 156),
            WindowPlacementPolicy.Cascade(front, 56, Primary));
        Assert.Equal(
            new PixelPoint(56, 56),
            WindowPlacementPolicy.Cascade(new PixelRect(1650, 950, 1200, 800), 56, Primary));
    }

    [Fact]
    public void Recovery_slots_reuse_the_lowest_free_one_and_run_out()
    {
        Assert.Equal(0, WindowPlacementPolicy.FirstFreeSlot([]));
        Assert.Equal(1, WindowPlacementPolicy.FirstFreeSlot([0, 2]));
        Assert.Equal(-1, WindowPlacementPolicy.FirstFreeSlot(
            Enumerable.Range(0, WindowPlacement.MaximumSlots)));
    }

    [Fact]
    public void Each_window_slot_has_its_own_recovery_key_and_the_first_keeps_the_old_one()
    {
        Assert.Equal(RuntimeWorkspaceRecoveryCodec.SnapshotKey, RuntimeWorkspaceRecoveryCodec.SnapshotKeyFor(0));
        Assert.Equal("desktop.main-window.2", RuntimeWorkspaceRecoveryCodec.SnapshotKeyFor(1));
        Assert.Equal("desktop.main-window.3", RuntimeWorkspaceRecoveryCodec.SnapshotKeyFor(2));
    }

    [Fact]
    public async Task Close_prompts_are_skipped_when_asking_is_off()
    {
        var asked = 0;
        var preference = new CloseConfirmationPreference(() => false, () => Task.CompletedTask);

        var confirmed = await preference.ConfirmAsync(() =>
        {
            asked++;
            return Task.FromResult((false, false));
        });

        Assert.True(confirmed);
        Assert.Equal(0, asked);
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public async Task Dont_ask_again_turns_asking_off_only_when_the_close_goes_ahead(
        bool confirmed,
        bool dontAskAgain,
        int expectedStops)
    {
        var stops = 0;
        var preference = new CloseConfirmationPreference(
            () => true,
            () =>
            {
                stops++;
                return Task.CompletedTask;
            });

        var result = await preference.ConfirmAsync(() => Task.FromResult((confirmed, dontAskAgain)));

        Assert.Equal(confirmed, result);
        Assert.Equal(expectedStops, stops);
    }

    [Fact]
    public void The_canvas_is_what_the_docked_rail_and_agent_panel_leave()
    {
        var area = DockPanelFillArea.Compute(
            new Size(1000, 700),
            [
                (AvaloniaDock.Left, new Rect(0, 0, 52, 700), new Thickness(0, 0, 6, 0)),
                (AvaloniaDock.Right, new Rect(640, 0, 360, 700), new Thickness(8, 0, 0, 0)),
            ]);

        Assert.Equal(new Rect(58, 0, 574, 700), area);
    }

    [Fact]
    public void A_rail_on_the_right_leaves_the_canvas_on_the_left()
    {
        var area = DockPanelFillArea.Compute(
            new Size(800, 600),
            [(AvaloniaDock.Right, new Rect(748, 0, 52, 600), default)]);

        Assert.Equal(new Rect(0, 0, 748, 600), area);
    }

    [Fact]
    public void Saving_terminal_settings_keeps_the_ask_before_closing_choice()
    {
        var profile = new TerminalProfile(
            new TerminalProfileId("default-terminal"),
            "Default terminal",
            "JetBrains Mono",
            14,
            1.4,
            TerminalCursorStyle.Block,
            cursorBlink: true,
            10_000,
            TerminalPalette.AsuraDark,
            BuiltInKeymaps.MacOsTerminalId,
            askBeforeClosing: false);
        var editor = new TerminalProfileEditorViewModel(profile, 3, BuiltInKeymaps.All)
        {
            FontSize = 15,
        };

        Assert.False(editor.CreateSaveRequest().Profile.AskBeforeClosing);
    }

    [Fact]
    public void Turning_ask_before_closing_back_on_is_saved()
    {
        var profile = new TerminalProfile(
            new TerminalProfileId("default-terminal"),
            "Default terminal",
            "JetBrains Mono",
            14,
            1.4,
            TerminalCursorStyle.Block,
            cursorBlink: true,
            10_000,
            TerminalPalette.AsuraDark,
            BuiltInKeymaps.MacOsTerminalId,
            askBeforeClosing: false);
        var editor = new TerminalProfileEditorViewModel(profile, 3, BuiltInKeymaps.All);
        Assert.False(editor.AskBeforeClosing);

        editor.AskBeforeClosing = true;

        Assert.True(editor.CreateSaveRequest().Profile.AskBeforeClosing);
    }

    [Fact]
    public void Closing_one_window_asks_about_the_window_not_the_app()
    {
        var options = Confirmations.CloseScopeOptions(
            new CloseScopeResult.ConfirmationRequired(
                CloseScopeKind.Window,
                "window-1",
                [Session("build")]));

        Assert.Equal("Close This Window?", options.Heading);
        Assert.Equal("Close Window", options.ConfirmLabel);
        Assert.Equal("Cancel", options.CancelLabel);
        Assert.Equal("Don't ask again", options.SuppressionLabel);
        Assert.Contains("• build", options.Notice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CloseScopeKind.Panel, "Close This Panel?")]
    [InlineData(CloseScopeKind.Tab, "Close This Tab?")]
    [InlineData(CloseScopeKind.Workspace, "Terminate This Workspace?")]
    [InlineData(CloseScopeKind.Session, "Close This Session?")]
    public void Close_alert_headings_share_title_case(CloseScopeKind scope, string heading)
    {
        var options = Confirmations.CloseScopeOptions(
            new CloseScopeResult.ConfirmationRequired(scope, "target", [Session("build")]));

        Assert.Equal(heading, options.Heading);
        Assert.Equal("Close Anyway", options.ConfirmLabel);
    }

    [Fact]
    public void The_quit_alert_counts_everything_and_offers_quit_or_cancel()
    {
        var census = RunningSessionCensus.Empty
            .WithWindow([Session("build")])
            .WithQuickTerminal([Session("top")]);

        var options = Confirmations.QuitApplicationOptions(census);

        Assert.Equal("Quit Asura?", options.Heading);
        Assert.Equal(census.Describe(), options.Detail);
        Assert.Equal("Quit", options.ConfirmLabel);
        Assert.Equal("Cancel", options.CancelLabel);
        Assert.True(options.ShowsCancel);
        Assert.Equal("Don't ask again", options.SuppressionLabel);
    }

    [Fact]
    public async Task Quitting_with_nothing_running_closes_every_window_without_an_alert()
    {
        var windows = new[] { new FakeQuitWindow(), new FakeQuitWindow() };
        var alerts = 0;
        var approvals = 0;

        var outcome = await ApplicationQuit.RunAsync(
            windows,
            () => Task.FromResult<IReadOnlyList<ActiveSessionSummary>?>([]),
            _ =>
            {
                alerts++;
                return Task.FromResult(true);
            },
            () => approvals++,
            () => throw new InvalidOperationException("Nothing failed to close."));

        Assert.Equal(QuitOutcome.ReadyToExit, outcome);
        Assert.Equal(0, alerts);
        Assert.Equal(1, approvals);
        Assert.All(windows, window => Assert.True(window.Closed));
    }

    [Fact]
    public async Task One_alert_covers_every_window_and_quick_terminal()
    {
        var windows = new[]
        {
            new FakeQuitWindow { Running = [Session("build")] },
            new FakeQuitWindow(),
            new FakeQuitWindow { Running = [Session("ssh")] },
        };
        var censuses = new List<RunningSessionCensus>();

        var outcome = await ApplicationQuit.RunAsync(
            windows,
            () => Task.FromResult<IReadOnlyList<ActiveSessionSummary>?>([Session("top")]),
            census =>
            {
                censuses.Add(census);
                return Task.FromResult(true);
            },
            () => { },
            () => { });

        Assert.Equal(QuitOutcome.ReadyToExit, outcome);
        var census = Assert.Single(censuses);
        Assert.Equal(3, census.Sessions.Count);
        Assert.Equal(2, census.WindowCount);
        Assert.True(census.IncludesQuickTerminal);
        // Nothing was closed until the one alert was answered.
        Assert.All(windows, window => Assert.True(window.Closed));
    }

    [Fact]
    public async Task Cancelling_the_quit_alert_leaves_every_window_open()
    {
        var windows = new[]
        {
            new FakeQuitWindow(),
            new FakeQuitWindow { Running = [Session("build")] },
        };
        var approvals = 0;

        var outcome = await ApplicationQuit.RunAsync(
            windows,
            () => Task.FromResult<IReadOnlyList<ActiveSessionSummary>?>([]),
            _ => Task.FromResult(false),
            () => approvals++,
            () => { });

        Assert.Equal(QuitOutcome.Cancelled, outcome);
        Assert.Equal(0, approvals);
        Assert.All(windows, window => Assert.False(window.Closed));
    }

    [Fact]
    public async Task Keeping_unsaved_edits_stops_the_quit_before_anything_is_counted()
    {
        var windows = new[]
        {
            new FakeQuitWindow(),
            new FakeQuitWindow { DiscardsEdits = false },
            new FakeQuitWindow(),
        };
        var alerts = 0;

        var outcome = await ApplicationQuit.RunAsync(
            windows,
            () => Task.FromResult<IReadOnlyList<ActiveSessionSummary>?>([]),
            _ =>
            {
                alerts++;
                return Task.FromResult(true);
            },
            () => { },
            () => { });

        Assert.Equal(QuitOutcome.Cancelled, outcome);
        Assert.Equal(0, alerts);
        Assert.False(windows[2].WasAskedAboutEdits);
        Assert.All(windows, window => Assert.False(window.WasInspected));
        Assert.All(windows, window => Assert.False(window.Closed));
    }

    [Fact]
    public async Task An_unanswered_window_still_gets_the_alert()
    {
        var alerts = 0;

        var outcome = await ApplicationQuit.RunAsync(
            [new FakeQuitWindow { Running = null }],
            () => Task.FromResult<IReadOnlyList<ActiveSessionSummary>?>([]),
            census =>
            {
                alerts++;
                Assert.False(census.IsComplete);
                return Task.FromResult(false);
            },
            () => { },
            () => { });

        Assert.Equal(QuitOutcome.Cancelled, outcome);
        Assert.Equal(1, alerts);
    }

    [Fact]
    public async Task A_window_that_cannot_close_stops_the_quit_and_keeps_the_rest_open()
    {
        var windows = new[]
        {
            new FakeQuitWindow(),
            new FakeQuitWindow { Closes = false },
            new FakeQuitWindow(),
        };
        var approved = false;

        var outcome = await ApplicationQuit.RunAsync(
            windows,
            () => Task.FromResult<IReadOnlyList<ActiveSessionSummary>?>([]),
            _ => Task.FromResult(true),
            () => approved = true,
            () => approved = false);

        Assert.Equal(QuitOutcome.Interrupted, outcome);
        Assert.False(approved);
        Assert.True(windows[0].Closed);
        Assert.False(windows[2].Closed);
    }

    private sealed class FakeQuitWindow : IQuitParticipant
    {
        public bool DiscardsEdits { get; init; } = true;

        public IReadOnlyList<ActiveSessionSummary>? Running { get; init; } = [];

        public bool Closes { get; init; } = true;

        public bool WasAskedAboutEdits { get; private set; }

        public bool WasInspected { get; private set; }

        public bool Closed { get; private set; }

        public Task<bool> ConfirmDiscardPendingEditsAsync()
        {
            WasAskedAboutEdits = true;
            return Task.FromResult(DiscardsEdits);
        }

        public Task<IReadOnlyList<ActiveSessionSummary>?> InspectRunningSessionsAsync()
        {
            WasInspected = true;
            return Task.FromResult(Running);
        }

        public Task<bool> CloseForQuitAsync()
        {
            Closed = Closes;
            return Task.FromResult(Closes);
        }
    }

    private static ActiveSessionSummary Session(string title) =>
        new(SessionId.New(), new PanelInstanceId($"panel-{title}"), title, "running", 1);
}
