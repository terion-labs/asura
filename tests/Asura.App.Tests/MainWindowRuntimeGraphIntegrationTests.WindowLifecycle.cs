using System.Reflection;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// The window lifecycle as the view model and close coordinator carry it:
/// asking what is running before a quit closes anything, closing for a quit
/// without asking twice, per-window recovery slots, the narrow-window layout,
/// "Ask before closing", and an isolated workspace's start confined to its
/// canvas, cancellable, and measured.
/// </summary>
public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    [Fact]
    public async Task Quit_inspection_reports_running_sessions_without_closing_the_window()
    {
        var (client, recorder) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var running = new ActiveSessionSummary(
            new SessionId("running-build"),
            new PanelInstanceId("build-panel"),
            "build",
            "cargo build",
            3);
        recorder.RunningSessions = [running];

        var inspected = await viewModel.InspectRunningSessionsAsync(CancellationToken.None);

        Assert.Equal([running], inspected);
        Assert.Equal([viewModel.WindowId.Value], recorder.InspectedCloseTargets);
        Assert.DoesNotContain(
            recorder.SessionCloses,
            close => close.Request.Scope == CloseScopeKind.Window);
        Assert.NotNull(viewModel.RuntimeWorkspace);
    }

    [Fact]
    public async Task A_window_that_cannot_answer_is_reported_as_unknown_rather_than_idle()
    {
        var (client, recorder) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        recorder.RunningSessions = null;

        Assert.Null(await viewModel.InspectRunningSessionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Closing_for_a_quit_confirms_the_window_close_without_asking_again()
    {
        var (client, recorder) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var prompts = 0;
        var closes = 0;
        var coordinator = new ShellCloseCoordinator(
            viewModel,
            new ShellClosePresentation(
                () => { prompts++; return Task.FromResult(true); },
                (_, _) => { prompts++; return Task.FromResult(true); },
                _ => { prompts++; return Task.FromResult(true); },
                _ => Task.CompletedTask,
                () => { },
                () => { },
                () => closes++),
            CancellationToken.None);

        Assert.True(await coordinator.ConfirmDiscardPendingEditsAsync("Asura quits"));
        Assert.True(await coordinator.CloseForQuitAsync());
        Assert.True(await coordinator.CloseForQuitAsync());

        Assert.Equal(0, prompts);
        Assert.Equal(1, closes);
        Assert.True(coordinator.IsWindowCloseApproved);
        var windowClose = Assert.Single(
            recorder.SessionCloses,
            close => close.Request.Scope == CloseScopeKind.Window);
        Assert.Equal(CloseDecision.Confirm, windowClose.Request.Decision);
    }

    [Fact]
    public async Task Each_window_restores_only_the_snapshot_in_its_own_recovery_slot()
    {
        var snapshot = CreateCatalogSnapshot();
        string payload;
        var (sourceClient, _) = CreateSessionClient();
        using (var source = CreateViewModel(sourceClient, snapshot))
        {
            Assert.True(await source.OpenWorkspaceAsync(SecondWorkspaceId));
            payload = RuntimeWorkspaceRecoveryCodec.Serialize(source.RuntimeWorkspace);
        }

        RuntimeRecoverySnapshot InSlot(int slot) => new(
            "previous-run",
            RuntimeWorkspaceRecoveryCodec.SnapshotKeyFor(slot),
            RuntimeWorkspaceRecoveryCodec.SchemaVersion,
            payload,
            DateTimeOffset.UtcNow);

        var (client, _) = CreateSessionClient();
        using var second = CreateViewModel(client, snapshot);
        second.RuntimeRecovery.AssignWindowSlot(2);
        Assert.Equal("desktop.main-window.3", second.RuntimeRecovery.SnapshotKey);

        Assert.False(await second.RestoreRuntimeSnapshotsAsync([InSlot(0)]));
        Assert.Null(second.RuntimeWorkspace);
        Assert.True(await second.RestoreRuntimeSnapshotsAsync([InSlot(0), InSlot(2)]));
        Assert.NotNull(second.RuntimeWorkspace);

        var (unslottedClient, _) = CreateSessionClient();
        using var unslotted = CreateViewModel(unslottedClient, snapshot);
        unslotted.RuntimeRecovery.AssignWindowSlot(null);
        Assert.Null(unslotted.RuntimeRecovery.SnapshotKey);
        Assert.False(await unslotted.RestoreRuntimeSnapshotsAsync([InSlot(0)]));
    }

    [Fact]
    public void A_narrow_window_floats_the_agent_panel_and_folds_the_rail_into_the_menu()
    {
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot());
        Assert.True(viewModel.ShowWorkspacesPanel);

        viewModel.UpdateWindowWidth(1440);
        Assert.False(viewModel.IsNarrowWindow);
        Assert.False(viewModel.IsCompactWindow);
        Assert.True(viewModel.IsWorkspaceRailVisible);
        Assert.Equal(!viewModel.IsAgentPanelDocked, viewModel.IsAgentPanelFloating);

        viewModel.UpdateWindowWidth(900);
        Assert.True(viewModel.IsNarrowWindow);
        Assert.True(viewModel.IsAgentPanelFloating);
        Assert.False(viewModel.IsAgentPanelDockedVisible);
        Assert.True(viewModel.IsWorkspaceRailVisible);

        viewModel.UpdateWindowWidth(700);
        Assert.True(viewModel.IsCompactWindow);
        Assert.False(viewModel.IsWorkspaceRailVisible);

        viewModel.UpdateWindowWidth(1200);
        Assert.False(viewModel.IsNarrowWindow);
        Assert.True(viewModel.IsWorkspaceRailVisible);
    }

    [Fact]
    public async Task Dont_ask_again_turns_off_the_profiles_ask_before_closing()
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
            BuiltInKeymaps.MacOsTerminalId);
        var catalog = DispatchProxy.Create<IDefinitionCatalog, TerminalSavingCatalogProxy>();
        var proxy = (TerminalSavingCatalogProxy)(object)catalog;
        proxy.Snapshot = CreateCatalogSnapshot() with
        {
            TerminalProfiles =
            [
                new StoredDefinition<TerminalProfile>(
                    profile,
                    7,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch),
            ],
        };
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, catalog);
        Assert.True(viewModel.AskBeforeClosing);

        await viewModel.StopAskingBeforeClosingAsync();

        var (saved, expectedRevision) = Assert.Single(proxy.TerminalSaves);
        Assert.False(saved.AskBeforeClosing);
        Assert.Equal(7, expectedRevision);
        Assert.True(saved.RepresentsSameAs(profile with { AskBeforeClosing = false }));
    }

    [Fact]
    public async Task Starting_an_isolated_workspace_can_be_cancelled_and_shows_measured_progress()
    {
        var snapshot = WithWorkspaceIsolation(CreateCatalogSnapshot(), WorkspaceId);
        var provider = new RecordingWorkspaceIsolationProvider
        {
            PrepareEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            AllowPrepare = new(TaskCreationOptions.RunContinuationsAsynchronously),
            PrepareProgress = new WorkspaceIsolationProgress(
                "Downloading the workspace image… 42%",
                0.42),
        };
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(
            client,
            snapshot,
            workspaceIsolationProvider: provider);

        var opening = viewModel.OpenWorkspaceAsync(WorkspaceId);
        await provider.PrepareEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield();
        Assert.True(viewModel.IsWorkspaceIsolationProgressVisible);
        Assert.True(viewModel.HasWorkspaceIsolationStartingPercent);
        Assert.Equal(42, viewModel.WorkspaceIsolationStartingPercent, 3);
        Assert.True(viewModel.CanCancelWorkspaceIsolationStartup);
        Assert.True(viewModel.CancelWorkspaceIsolationStartupCommand.CanExecute(null));

        viewModel.CancelWorkspaceIsolationStartup();

        Assert.False(await opening.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(viewModel.IsWorkspaceIsolationStarting);
        Assert.False(viewModel.CanCancelWorkspaceIsolationStartup);
        Assert.True(viewModel.IsWorkspaceCanvasVisible);
        Assert.Null(viewModel.RuntimeWorkspace);
        Assert.Null(viewModel.OperationError);
    }

    [Fact]
    public async Task Another_workspace_can_be_brought_forward_while_one_starts_and_back_again()
    {
        var snapshot = WithWorkspaceIsolation(CreateCatalogSnapshot(), WorkspaceId);
        var provider = new RecordingWorkspaceIsolationProvider
        {
            PrepareEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            AllowPrepare = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(
            client,
            snapshot,
            workspaceIsolationProvider: provider);
        Assert.True(await viewModel.OpenWorkspaceAsync(SecondWorkspaceId));
        var other = viewModel.RuntimeWorkspace;

        var opening = viewModel.OpenWorkspaceAsync(WorkspaceId);
        await provider.PrepareEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.IsWorkspaceIsolationProgressVisible);
        Assert.False(viewModel.IsWorkspaceCanvasVisible);

        Assert.True(await viewModel.OpenWorkspaceAsync(SecondWorkspaceId));
        Assert.True(viewModel.IsWorkspaceIsolationStarting);
        Assert.False(viewModel.IsWorkspaceIsolationProgressVisible);
        Assert.True(viewModel.IsWorkspaceCanvasVisible);
        Assert.Same(other, viewModel.RuntimeWorkspace);
        Assert.True(viewModel.Workspaces.Single(item => item.Id == SecondWorkspaceId).IsInFront);
        Assert.False(viewModel.Workspaces.Single(item => item.Id == WorkspaceId).IsInFront);

        // Choosing it again brings its progress back without a second start.
        Assert.False(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        Assert.True(viewModel.IsWorkspaceIsolationProgressVisible);
        Assert.False(viewModel.IsWorkspaceCanvasVisible);
        Assert.Single(provider.PrepareRequests);

        provider.AllowPrepare.TrySetResult(true);
        Assert.True(await opening.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(viewModel.IsWorkspaceIsolationStarting);
        Assert.True(viewModel.IsWorkspaceCanvasVisible);
    }

    [Fact]
    public async Task A_window_closed_on_its_own_empties_its_recovery_slot()
    {
        var recoveryStore = new PanelMoveRecoveryStore();
        var startup = new ApplicationStartupState();
        startup.Initialize(new ApplicationRunStart("slot-release-test", false,
            new ApplicationRunState(null, true, null, null)));
        var recoveryWriter = new RuntimeRecoveryWriter(recoveryStore, startup, TimeProvider.System);
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot(),
            runtimeRecoveryWriter: recoveryWriter);
        viewModel.RuntimeRecovery.AssignWindowSlot(1);
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        await recoveryWriter.FlushAsync(CancellationToken.None);
        var written = recoveryStore.Snapshots.Last(item => item.Key == "desktop.main-window.2");
        Assert.True(RuntimeWorkspaceRecoveryCodec.TryDeserialize(written, out var open, out _));
        Assert.NotNull(open!.Workspace);

        viewModel.RuntimeRecovery.ReleaseWindowSlot();
        await recoveryWriter.SealAndFlushAsync(CancellationToken.None);

        // The slot now holds an empty window, and this window writes no more.
        var released = recoveryStore.Snapshots.Last(item => item.Key == "desktop.main-window.2");
        Assert.True(RuntimeWorkspaceRecoveryCodec.TryDeserialize(released, out var empty, out _));
        Assert.Null(empty!.Workspace);
        Assert.Null(viewModel.RuntimeRecovery.SnapshotKey);
    }

    public class TerminalSavingCatalogProxy : DispatchProxy
    {
        public DefinitionCatalogSnapshot Snapshot { get; set; } = DefinitionCatalogSnapshot.Empty;

        public List<(TerminalProfile Profile, long? ExpectedRevision)> TerminalSaves { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_Snapshot" => Snapshot,
                "add_Changed" or "remove_Changed" => null,
                nameof(IDefinitionCatalog.SaveTerminalProfileAsync)
                    when args is [TerminalProfile profile, long expectedRevision, ..] =>
                    SaveTerminal(profile, expectedRevision),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private ValueTask<DefinitionStoreResult<StoredDefinition<TerminalProfile>>> SaveTerminal(
            TerminalProfile profile,
            long expectedRevision)
        {
            TerminalSaves.Add((profile, expectedRevision));
            return ValueTask.FromResult(
                DefinitionStoreResult<StoredDefinition<TerminalProfile>>.Success(
                    new StoredDefinition<TerminalProfile>(
                        profile,
                        expectedRevision + 1,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch)));
        }
    }
}
