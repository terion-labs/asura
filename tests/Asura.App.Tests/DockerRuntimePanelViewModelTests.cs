using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Asura.Docker;
using Asura.Testing;
using FluentIcons.Common;

namespace Asura.App.Tests;

public sealed class DockerRuntimePanelViewModelTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    [Fact]
    public async Task InitializationSelectsTheFirstContainerAndLoadsInspection()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.Equal(PanelKind.Docker, panel.Kind);
        Assert.Equal("api", panel.SelectedResource?.Title);
        Assert.Equal(3, panel.ContainerCount);
        Assert.Equal(1, panel.RunningContainerCount);
        Assert.True(panel.CanOpenShell);
        Assert.NotNull(panel.Inspection);
        Assert.Equal("1 running · Docker 28.3.0", panel.StatusText);
    }

    [Fact]
    public async Task RunningContainerActionsAreReevaluatedAfterInitialRefreshCompletes()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.False(panel.StartCommand.CanExecute(null));
        Assert.True(panel.StopCommand.CanExecute(null));
        Assert.True(panel.RestartCommand.CanExecute(null));
        Assert.True(panel.PauseCommand.CanExecute(null));
        Assert.False(panel.ResumeCommand.CanExecute(null));
        Assert.False(panel.CanStartSelectedContainer);
        Assert.True(panel.CanStopSelectedContainer);
        Assert.True(panel.CanRestartSelectedContainer);
        Assert.True(panel.CanPauseSelectedContainer);
        Assert.False(panel.CanResumeSelectedContainer);

        panel.SelectResource(Assert.Single(panel.Resources, item => string.Equals(item.Title, "worker", StringComparison.Ordinal)));

        Assert.True(panel.StartCommand.CanExecute(null));
        Assert.False(panel.StopCommand.CanExecute(null));
        Assert.True(panel.SelectedContainerIsStopped);
        Assert.False(panel.SelectedContainerIsActive);
        Assert.True(panel.CanStartSelectedContainer);
        Assert.False(panel.CanStopSelectedContainer);
    }

    [Fact]
    public async Task PausedContainerUsesStopAndResumeLifecycleStates()
    {
        var snapshot = Snapshot() with
        {
            Containers = [Container("paused", "paused-api", "paused")],
        };
        var client = new FakeDockerEngineClient(snapshot);
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.False(panel.StartCommand.CanExecute(null));
        Assert.True(panel.StopCommand.CanExecute(null));
        Assert.True(panel.RestartCommand.CanExecute(null));
        Assert.False(panel.PauseCommand.CanExecute(null));
        Assert.True(panel.ResumeCommand.CanExecute(null));
        Assert.False(panel.SelectedContainerIsStopped);
        Assert.True(panel.SelectedContainerIsActive);
        Assert.False(panel.CanStartSelectedContainer);
        Assert.True(panel.CanStopSelectedContainer);
    }

    [Fact]
    public async Task LoadingAndEmptyStatesAreMutuallyExclusive()
    {
        var snapshotCompletion = new TaskCompletionSource<DockerEngineSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDockerEngineClient(Snapshot())
        {
            SnapshotCompletion = snapshotCompletion,
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        Assert.True(panel.ShowLoading);
        Assert.False(panel.ShowEmptyState);
        Assert.True(panel.ShowResourceProgress);

        snapshotCompletion.SetResult(Snapshot() with { Containers = [] });
        await panel.Initialization;

        Assert.False(panel.ShowLoading);
        Assert.True(panel.ShowEmptyState);
        Assert.False(panel.ShowResourceProgress);
    }

    [Fact]
    public async Task ContainersAreGroupedByComposeStackWithRunningStacksAndContainersFirst()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.Equal(["zeta", "alpha"], panel.ContainerStacks.Select(stack => stack.Name), StringComparer.Ordinal);
        Assert.Equal(["api", "web"], panel.ContainerStacks[0].Containers.Select(item => item.Title), StringComparer.Ordinal);
        Assert.Equal("1/2 running", panel.ContainerStacks[0].Summary);
        Assert.Equal("1 stopped", panel.ContainerStacks[1].Summary);
        Assert.Equal("api", panel.Resources[0].Title);
    }

    [Fact]
    public async Task StandaloneContainersAreVisibleAfterComposeStacks()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            Containers =
            [
                .. snapshot.Containers,
                Container("standalone-running", "toolbox", "running"),
                Container("standalone-stopped", "old-toolbox", "exited"),
            ],
        };
        var client = new FakeDockerEngineClient(snapshot);
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.Equal(
            ["zeta", "alpha", "Standalone containers"],
            panel.ContainerStacks.Select(stack => stack.Name), StringComparer.Ordinal);
        var standalone = panel.ContainerStacks[^1];
        Assert.True(standalone.IsStandalone);
        Assert.Equal("Standalone containers", standalone.Name);
        Assert.Equal(["toolbox", "old-toolbox"], standalone.Containers.Select(item => item.Title), StringComparer.Ordinal);
        Assert.Equal("1/2 running", standalone.Summary);
        Assert.Equal(5, panel.Resources.Count);
    }

    [Theory]
    [InlineData(DockerContainerAction.Start, "web")]
    [InlineData(DockerContainerAction.Stop, "api,cache")]
    [InlineData(DockerContainerAction.Restart, "api,cache")]
    [InlineData(DockerContainerAction.Pause, "api")]
    [InlineData(DockerContainerAction.Resume, "cache")]
    public async Task StackActionsApplyToEveryEligibleContainerAndRefreshOnce(
        DockerContainerAction action,
        string expectedContainerNames)
    {
        var snapshot = Snapshot() with
        {
            Containers =
            [
                .. Snapshot().Containers,
                new DockerContainerSummary(
                    "paused",
                    "cache",
                    "redis:8-alpine",
                    "paused",
                    "Up 2 hours (Paused)",
                    string.Empty,
                    "2 hours ago",
                    "—",
                    "—",
                    "—",
                    "—",
                    "zeta",
                    "cache"),
            ],
        };
        var client = new FakeDockerEngineClient(snapshot);
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;

        var stack = Assert.Single(panel.ContainerStacks, item => string.Equals(item.Name, "zeta", StringComparison.Ordinal));
        await panel.RunStackActionAsync(stack, action);

        var namesById = snapshot.Containers.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        Assert.Equal(
            expectedContainerNames.Split(','),
            client.ContainerActions.Select(call => namesById[call.ContainerId]), StringComparer.Ordinal);
        Assert.All(client.ContainerActions, call => Assert.Equal(action, call.Action));
        Assert.Equal(2, client.SnapshotReadCount);
    }

    [Fact]
    public async Task ShellDetailIsAvailableOnlyForARunningContainer()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;

        panel.SelectDetail(DockerPanelDetail.Shell);
        Assert.True(panel.IsShellDetail);

        panel.SelectResource(Assert.Single(panel.Resources, item => string.Equals(item.Title, "worker", StringComparison.Ordinal)));

        Assert.True(panel.IsInfoDetail);
        Assert.False(panel.CanOpenShell);
    }

    [Fact]
    public async Task ShellViewShowsProgressWhileResolvingAndOffersRetryOnlyAfterAFailure()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        Assert.Equal("api", panel.SelectedResource?.Title);

        panel.BeginShellResolution("abc");

        Assert.True(panel.IsResolvingShell);
        Assert.True(panel.ShowShellProgress);
        Assert.False(panel.ShowShellFailure);
        Assert.False(panel.ShowShellIdle);
        Assert.False(panel.CanRetryShell);
        Assert.Equal("Finding a container shell…", panel.ShellStateTitle);

        panel.PresentShellResolutionFailure(
            "abc",
            new DockerError(DockerErrorCode.ShellUnavailable, "No shell was found in this image.", false));

        Assert.True(panel.IsShellDetail);
        Assert.False(panel.IsResolvingShell);
        Assert.True(panel.HasShellFailure);
        Assert.True(panel.ShowShellFailure);
        Assert.False(panel.ShowShellProgress);
        Assert.False(panel.ShowShellIdle);
        Assert.True(panel.CanRetryShell);
        Assert.Equal("No interactive shell found", panel.ShellStateTitle);
        Assert.Equal("No shell was found in this image.", panel.ShellStateMessage);

        panel.BeginShellResolution("abc");

        Assert.False(panel.HasShellFailure);
        Assert.False(panel.CanRetryShell);
        Assert.True(panel.ShowShellProgress);
    }

    [Fact]
    public async Task ShellViewIsIdleWithoutRetryForAStoppedContainer()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        panel.PresentShellResolutionFailure(
            "abc",
            new DockerError(DockerErrorCode.CommandFailed, "exec failed", true));
        Assert.True(panel.CanRetryShell);

        panel.SelectResource(Assert.Single(
            panel.Resources,
            item => string.Equals(item.Title, "worker", StringComparison.Ordinal)));

        Assert.True(panel.ShowShellIdle);
        Assert.False(panel.ShowShellProgress);
        Assert.False(panel.ShowShellFailure);
        Assert.False(panel.CanRetryShell);
        Assert.Equal("Container isn’t running", panel.ShellStateTitle);
    }

    [Fact]
    public async Task LogSearchMarksLinesWithoutAMatchAsContext()
    {
        var client = new FakeDockerEngineClient(Snapshot())
        {
            LogPageFactory = _ => new DockerContainerLogPage(
                [
                    new DockerContainerLogLine("2026-08-10T12:00:00Z", "starting worker"),
                    new DockerContainerLogLine("2026-08-10T12:00:01Z", "database unavailable"),
                    new DockerContainerLogLine("2026-08-10T12:00:02Z", "retrying in 5s"),
                ],
                false,
                "2026-08-10T12:00:00Z",
                "2026-08-10T12:00:02Z"),
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        panel.SelectDetail(DockerPanelDetail.Logs);
        await WaitUntilAsync(() => panel.LogRows.Count == 3);
        Assert.All(panel.LogRows, row => Assert.False(row.IsContextLine));

        panel.LogSearchText = "unavailable";
        await panel.SearchLogsAsync();

        Assert.Equal(
            [true, false, true],
            panel.LogRows.Select(row => row.IsContextLine));
    }

    [Fact]
    public async Task SwitchingSectionReprojectsResourcesAndDisablesContainerDetails()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;

        panel.SelectSection(DockerPanelSection.Images);
        panel.SelectDetail(DockerPanelDetail.Logs);

        Assert.True(panel.IsImagesSection);
        Assert.Single(panel.Resources);
        Assert.Equal("demo/api:latest", panel.SelectedResource?.Title);
        Assert.Equal(Symbol.Archive, panel.SelectedResource?.Icon);
        Assert.False(panel.CanOpenShell);
        Assert.True(panel.IsInfoDetail);
    }

    [Fact]
    public async Task DetailTabsFollowTheSelectedResourceCapabilities()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;

        Assert.True(panel.CanShowLogs);
        Assert.True(panel.CanShowShell);
        Assert.True(panel.CanShowFiles);
        Assert.True(panel.CanShowJson);

        panel.SelectSection(DockerPanelSection.Images);

        Assert.False(panel.CanShowLogs);
        Assert.False(panel.CanShowShell);
        Assert.True(panel.CanShowFiles);
        Assert.True(panel.CanShowJson);
        panel.SelectDetail(DockerPanelDetail.Files);
        Assert.True(panel.IsFilesDetail);

        panel.SelectSection(DockerPanelSection.Volumes);
        Assert.True(panel.CanShowFiles);
        panel.SelectDetail(DockerPanelDetail.Files);
        Assert.True(panel.IsFilesDetail);

        panel.SelectSection(DockerPanelSection.Networks);
        Assert.False(panel.CanShowFiles);
        panel.SelectDetail(DockerPanelDetail.Files);
        Assert.True(panel.IsInfoDetail);
        Assert.True(panel.CanShowJson);
    }

    /// <summary>
    /// The views are five, the most a segmented control holds, in the order
    /// the control shows them, and the narrow pop-up names the current one.
    /// </summary>
    [Fact]
    public async Task DetailViewsAreFiveStableSegmentsNamedByThePopUp()
    {
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            new FakeDockerEngineClient(Snapshot()),
            BuiltInConnections.Local);
        await panel.Initialization;

        Assert.Equal(
            [DockerPanelDetail.Info, DockerPanelDetail.Logs, DockerPanelDetail.Shell, DockerPanelDetail.Files, DockerPanelDetail.Json],
            Enum.GetValues<DockerPanelDetail>());
        Assert.Equal(0, panel.DetailIndex);
        Assert.Equal("Info", panel.DetailTitle);

        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        panel.SelectDetail(DockerPanelDetail.Json);

        Assert.Equal(4, panel.DetailIndex);
        Assert.Equal("JSON", panel.DetailTitle);
        Assert.Contains(nameof(DockerRuntimePanelViewModel.DetailIndex), changed, StringComparer.Ordinal);
        Assert.Contains(nameof(DockerRuntimePanelViewModel.DetailTitle), changed, StringComparer.Ordinal);
    }

    /// <summary>
    /// The section list and the narrow segmented control both bind the
    /// section by position, and a position that is not a section is ignored.
    /// </summary>
    [Fact]
    public async Task SectionIndexSelectsTheSectionAtThatPosition()
    {
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            new FakeDockerEngineClient(Snapshot()),
            BuiltInConnections.Local);
        await panel.Initialization;

        var changed = new List<string?>();
        panel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        panel.SectionIndex = (int)DockerPanelSection.Volumes;

        Assert.True(panel.IsVolumesSection);
        Assert.Equal(2, panel.SectionIndex);
        Assert.Contains(nameof(DockerRuntimePanelViewModel.SectionIndex), changed, StringComparer.Ordinal);

        panel.SectionIndex = -1;
        Assert.True(panel.IsVolumesSection);
        panel.SectionIndex = 9;
        Assert.True(panel.IsVolumesSection);
    }

    /// <summary>
    /// The header's status mark follows the engine — it was a green dot
    /// whatever happened — and says which state it shows, with a glyph for
    /// each so the state does not rest on colour.
    /// </summary>
    [Fact]
    public async Task EngineStatusFollowsTheEngineAndNamesItsState()
    {
        var pending = new TaskCompletionSource<DockerEngineSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            new FakeDockerEngineClient(Snapshot()) { SnapshotCompletion = pending },
            BuiltInConnections.Local);

        Assert.Equal(StatusTone.Warning, panel.EngineStatusTone);
        Assert.StartsWith("Connecting to the Docker engine", panel.EngineStatusLabel, StringComparison.Ordinal);
        Assert.Equal(Symbol.Warning, panel.EngineStatusGlyph);

        pending.SetResult(Snapshot());
        await panel.Initialization;

        Assert.Equal(StatusTone.Success, panel.EngineStatusTone);
        Assert.StartsWith("Docker engine connected", panel.EngineStatusLabel, StringComparison.Ordinal);
        Assert.Equal(Symbol.CheckmarkCircle, panel.EngineStatusGlyph);

        using var unavailable = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            new FakeDockerEngineClient(new DockerError(
                DockerErrorCode.RuntimeUnavailable,
                "The daemon is not running.",
                true)),
            BuiltInConnections.Local);
        await unavailable.Initialization;

        Assert.Equal(StatusTone.Danger, unavailable.EngineStatusTone);
        Assert.StartsWith("Docker engine unavailable", unavailable.EngineStatusLabel, StringComparison.Ordinal);
        Assert.Equal(Symbol.ErrorCircle, unavailable.EngineStatusGlyph);
    }

    /// <summary>
    /// Emptying the search field — its clear button, Escape, or deleting the
    /// last character — ends the search on the engine, as clearing a search
    /// field does; it used to leave the old results showing.
    /// </summary>
    [Fact]
    public async Task EmptyingTheSearchFieldEndsTheSearch()
    {
        var client = new FakeDockerEngineClient(Snapshot());
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        panel.LogSearchText = "ready";
        await panel.SearchLogsAsync();
        Assert.True(panel.IsLogSearchActive);
        var requests = client.LogRequests.Count;

        panel.LogSearchText = string.Empty;

        Assert.False(panel.IsLogSearchActive);
        Assert.True(panel.CanFollowLogs);
        await WaitUntilAsync(() => client.LogRequests.Count > requests);
        Assert.Null(client.LogRequests[^1].SearchText);
    }

    [Fact]
    public async Task LogSearchSendsQueryAndContextToEngineInsteadOfFilteringVisibleRows()
    {
        var client = new FakeDockerEngineClient(Snapshot())
        {
            LogPageFactory = request => new DockerContainerLogPage(
                [new DockerContainerLogLine("2026-08-10T12:00:00Z", request.SearchText ?? "latest")],
                false,
                "2026-08-10T12:00:00Z",
                "2026-08-10T12:00:00Z"),
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        panel.LogSearchText = "database unavailable";
        panel.LogSearchContext = 4;

        await panel.SearchLogsAsync();

        var request = Assert.Single(client.LogRequests);
        Assert.Equal("database unavailable", request.SearchText);
        Assert.Equal(4, request.ContextLines);
        Assert.True(panel.IsLogSearchActive);
        Assert.False(panel.CanFollowLogs);
        Assert.Equal("database unavailable", Assert.Single(panel.LogRows).Message);
    }

    [Fact]
    public async Task LogSearchHighlightsEveryCaseInsensitiveMatchWithoutChangingTheMessage()
    {
        var client = new FakeDockerEngineClient(Snapshot())
        {
            LogPageFactory = _ => new DockerContainerLogPage(
                [new DockerContainerLogLine(
                    "2026-08-10T12:00:00Z",
                    "Completed cleanup; completed archive")],
                false,
                "2026-08-10T12:00:00Z",
                "2026-08-10T12:00:00Z"),
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        panel.LogSearchText = "completed";

        await panel.SearchLogsAsync();

        var row = Assert.Single(panel.LogRows);
        Assert.Equal("Completed cleanup; completed archive", row.Message);
        Assert.Equal(
            ["Completed", " cleanup; ", "completed", " archive"],
            row.MessageSegments.Select(segment => segment.Text), StringComparer.Ordinal);
        Assert.Equal(
            [true, false, true, false],
            row.MessageSegments.Select(segment => segment.IsMatch));
    }

    [Fact]
    public void LogSearchContextUsesABoundedNumericEditor()
    {
        var input = ApplicationViews
            .FindUniqueNamedElement("LogSearchContextInput")
            .Element;

        Assert.Equal("NumericUpDown", input.Name.LocalName);
        Assert.Equal("0", input.Attribute("Minimum")?.Value);
        Assert.Equal("100", input.Attribute("Maximum")?.Value);
        Assert.Equal("1", input.Attribute("Increment")?.Value);
        Assert.Equal("False", input.Attribute("ShowButtonSpinner")?.Value);
        Assert.Equal(
            "{Binding LogSearchContextInput, Mode=TwoWay}",
            input.Attribute("Value")?.Value);
    }

    [Fact]
    public void LogSearchIsTheSharedSearchFieldWithItsOwnClearButton()
    {
        var search = ApplicationViews
            .FindUniqueNamedElement("LogSearchBox")
            .Element;

        // The shared field draws the magnifier and the clear button and
        // clears on Escape; a hand-placed clear button over a padded text box
        // is what every search field used to be.
        Assert.Equal("SearchField", search.Name.LocalName);
        Assert.Equal("OnLogSearchKeyDown", search.Attribute("KeyDown")?.Value);
        Assert.Null(search.Attribute("Padding"));
    }

    [Fact]
    public async Task LogSearchContextEditorNormalizesEmptyFractionalAndOutOfRangeValues()
    {
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            new FakeDockerEngineClient(Snapshot()),
            BuiltInConnections.Local);
        await panel.Initialization;

        panel.LogSearchContextInput = 12.9m;
        Assert.Equal(12, panel.LogSearchContext);

        panel.LogSearchContextInput = 101m;
        Assert.Equal(100, panel.LogSearchContext);

        panel.LogSearchContextInput = null;
        Assert.Equal(0, panel.LogSearchContext);
    }

    [Fact]
    public async Task OlderLogPageIsPrependedWithoutRepeatingCursorOverlap()
    {
        var client = new FakeDockerEngineClient(Snapshot())
        {
            LogPageFactory = request => request.BeforeTimestamp is null
                ? new DockerContainerLogPage(
                    [
                        new DockerContainerLogLine("2026-08-10T12:00:01Z", "middle"),
                        new DockerContainerLogLine("2026-08-10T12:00:02Z", "newest"),
                    ],
                    true,
                    "2026-08-10T12:00:01Z",
                    "2026-08-10T12:00:02Z")
                : new DockerContainerLogPage(
                    [
                        new DockerContainerLogLine("2026-08-10T12:00:00Z", "oldest"),
                        new DockerContainerLogLine("2026-08-10T12:00:01Z", "middle"),
                    ],
                    false,
                    "2026-08-10T12:00:00Z",
                    "2026-08-10T12:00:01Z"),
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;
        panel.LogSearchText = "anything";
        await panel.SearchLogsAsync();

        Assert.True(await panel.LoadOlderLogsAsync());

        Assert.Equal(["oldest", "middle", "newest"], panel.LogRows.Select(row => row.Message), StringComparer.Ordinal);
        Assert.False(panel.HasOlderLogs);
        Assert.NotNull(client.LogRequests[1].BeforeTimestamp);
    }

    [Fact]
    public async Task VolumesAreSortedByLoadedSizeDescendingWithUnknownSizesLast()
    {
        var snapshot = Snapshot() with
        {
            Volumes =
            [
                new DockerVolumeSummary("unknown", "local", "local", "/unknown"),
                new DockerVolumeSummary("small", "local", "local", "/small"),
                new DockerVolumeSummary("large", "local", "local", "/large"),
                new DockerVolumeSummary("also-large", "local", "local", "/also-large"),
            ],
        };
        var client = new FakeDockerEngineClient(snapshot)
        {
            VolumeUsage =
            [
                new DockerVolumeUsage("small", "10 MB", 10_000_000),
                new DockerVolumeUsage("large", "2 GB", 2_000_000_000),
                new DockerVolumeUsage("also-large", "2 GB", 2_000_000_000),
                new DockerVolumeUsage("unknown", "—", null),
            ],
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;

        panel.SelectSection(DockerPanelSection.Volumes);
        await panel.VolumeUsageLoading;

        Assert.Equal(
            ["also-large", "large", "small", "unknown"],
            panel.Resources.Select(item => item.Title), StringComparer.Ordinal);
        // Sizes go through the application's one size formatter, whatever
        // unit the engine wrote them in.
        Assert.Equal(
            [ByteSize.Format(2_000_000_000), ByteSize.Format(2_000_000_000), ByteSize.Format(10_000_000), "—"],
            panel.Resources.Select(item => item.Subtitle), StringComparer.Ordinal);
        Assert.Equal(1, client.VolumeUsageReadCount);
        Assert.Equal("4 resources", panel.ResourceSummary);
    }

    [Fact]
    public async Task VolumeSectionExposesLoadingStateUntilSizeUsageFinishes()
    {
        var volumeUsage = new TaskCompletionSource<IReadOnlyList<DockerVolumeUsage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeDockerEngineClient(Snapshot())
        {
            VolumeUsageCompletion = volumeUsage,
        };
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);
        await panel.Initialization;

        panel.SelectSection(DockerPanelSection.Volumes);

        Assert.True(panel.ShowVolumeSizeLoading);
        Assert.True(panel.ShowResourceProgress);
        Assert.Equal("1 resource · Loading sizes…", panel.ResourceSummary);

        panel.SelectSection(DockerPanelSection.Images);
        Assert.False(panel.ShowResourceProgress);
        panel.SelectSection(DockerPanelSection.Volumes);
        Assert.True(panel.ShowResourceProgress);

        volumeUsage.SetResult([new DockerVolumeUsage("app-data", "64 MB", 64_000_000)]);
        await panel.VolumeUsageLoading;

        Assert.False(panel.ShowVolumeSizeLoading);
        Assert.False(panel.ShowResourceProgress);
        Assert.Equal("1 resource", panel.ResourceSummary);
        Assert.Equal(ByteSize.Format(64_000_000), Assert.Single(panel.Resources).Subtitle);
    }

    [Fact]
    public async Task EngineFailurePresentsRetryableIssue()
    {
        var client = new FakeDockerEngineClient(new DockerError(
            DockerErrorCode.ConnectionFailed,
            "SSH target is offline.",
            true));
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.True(panel.HasIssue);
        Assert.Equal("Docker unavailable", panel.IssueTitle);
        Assert.Equal("SSH target is offline.", panel.IssueMessage);
        Assert.Equal("Retry available", panel.StatusText);
        Assert.False(panel.ShowsDockerInstallHelp);
    }

    [Fact]
    public async Task RuntimeUnavailableFailureOffersDockerInstallationHelp()
    {
        var client = new FakeDockerEngineClient(new DockerError(
            DockerErrorCode.RuntimeUnavailable,
            "Docker is not installed in this environment, or its daemon is not running.",
            true));
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Docker",
            client,
            BuiltInConnections.Local);

        await panel.Initialization;

        Assert.True(panel.ShowsDockerInstallHelp);
    }

    private static DockerEngineSnapshot Snapshot() => new(
        new DockerEngineSummary("28.3.0", "linux", "arm64", "1.51"),
        [
            new DockerContainerSummary(
                "abc",
                "api",
                "demo/api:latest",
                "running",
                "Up 2 hours",
                "8080/tcp",
                "2 hours ago",
                "2%",
                "128 MiB",
                "1 MB / 2 MB",
                "3 MB / 4 MB",
                "zeta",
                "api"),
            new DockerContainerSummary(
                "ghi",
                "web",
                "demo/web:latest",
                "exited",
                "Exited (0)",
                string.Empty,
                "3 hours ago",
                "—",
                "—",
                "—",
                "—",
                "zeta",
                "web"),
            new DockerContainerSummary(
                "def",
                "worker",
                "demo/worker:latest",
                "exited",
                "Exited (0)",
                string.Empty,
                "1 day ago",
                "—",
                "—",
                "—",
                "—",
                "alpha",
                "worker"),
        ],
        [new DockerImageSummary("image", "demo/api", "latest", "184 MB", "2 days ago")],
        [new DockerVolumeSummary("app-data", "local", "local", "/data")],
        [new DockerNetworkSummary("network", "app", "bridge", "local", "today")],
        DateTimeOffset.UtcNow);

    private static DockerContainerSummary Container(
        string id,
        string name,
        string state) => new(
        id,
        name,
        "demo/toolbox:latest",
        state,
string.Equals(state, "running", StringComparison.Ordinal) ? "Up 2 hours" : "Exited (0)",
        string.Empty,
        "2 hours ago",
        "—",
        "—",
        "—",
        "—");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The Docker panel did not reach the expected state in time.");
    }

    private sealed class FakeDockerEngineClient : IDockerEngineClient
    {
        private readonly DockerEngineSnapshot? _snapshot;
        private readonly DockerError? _error;

        public IReadOnlyList<DockerVolumeUsage> VolumeUsage { get; init; } = [];

        public TaskCompletionSource<IReadOnlyList<DockerVolumeUsage>>? VolumeUsageCompletion
        {
            get;
            init;
        }

        public TaskCompletionSource<DockerEngineSnapshot>? SnapshotCompletion
        {
            get;
            init;
        }

        public int VolumeUsageReadCount { get; private set; }

        public int SnapshotReadCount { get; private set; }

        public List<(string ContainerId, DockerContainerAction Action)> ContainerActions { get; } = [];

        public List<DockerContainerLogRequest> LogRequests { get; } = [];

        public Func<DockerContainerLogRequest, DockerContainerLogPage> LogPageFactory { get; init; } =
            _ => new DockerContainerLogPage(
                [new DockerContainerLogLine("2026-08-10T12:00:00Z", "ready")],
                false,
                "2026-08-10T12:00:00Z",
                "2026-08-10T12:00:00Z");

        public FakeDockerEngineClient(DockerEngineSnapshot snapshot) => _snapshot = snapshot;

        public FakeDockerEngineClient(DockerError error) => _error = error;

        public async ValueTask<DockerResult<DockerEngineSnapshot>> ReadSnapshotAsync(
            ConnectionProfile connection,
            CancellationToken cancellationToken)
        {
            SnapshotReadCount++;
            if (SnapshotCompletion is not null)
            {
                var completedSnapshot = await SnapshotCompletion.Task.WaitAsync(cancellationToken);
                return new DockerResult<DockerEngineSnapshot>.Success(completedSnapshot);
            }

            return _snapshot is { } snapshot
                ? new DockerResult<DockerEngineSnapshot>.Success(snapshot)
                : new DockerResult<DockerEngineSnapshot>.Failure(_error!);
        }

        public async ValueTask<DockerResult<IReadOnlyList<DockerVolumeUsage>>> ReadVolumeUsageAsync(
            ConnectionProfile connection,
            CancellationToken cancellationToken)
        {
            VolumeUsageReadCount++;
            var usage = VolumeUsageCompletion is null
                ? VolumeUsage
                : await VolumeUsageCompletion.Task.WaitAsync(cancellationToken);
            return new DockerResult<IReadOnlyList<DockerVolumeUsage>>.Success(usage);
        }

        public ValueTask<DockerResult<DockerResourceInspection>> InspectAsync(
            ConnectionProfile connection,
            DockerResourceReference resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerResourceInspection>>(
                new DockerResult<DockerResourceInspection>.Success(
                    new DockerResourceInspection(
                        resource,
                        [new DockerInspectionProperty("Id", resource.Id)],
                        "{}")));

        public ValueTask<DockerResult<DockerContainerLogPage>> ReadContainerLogsAsync(
            ConnectionProfile connection,
            DockerContainerLogRequest request,
            CancellationToken cancellationToken)
        {
            LogRequests.Add(request);
            return ValueTask.FromResult<DockerResult<DockerContainerLogPage>>(
                new DockerResult<DockerContainerLogPage>.Success(LogPageFactory(request)));
        }

        public ValueTask<DockerResult<bool>> DownloadContainerLogsAsync(
            ConnectionProfile connection,
            string containerId,
            Stream destination,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<bool>>(new DockerResult<bool>.Success(true));

        public ValueTask<DockerResult<string>> ResolveContainerShellAsync(
            ConnectionProfile connection,
            string containerId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<string>>(
                new DockerResult<string>.Success("/bin/sh"));

        public ValueTask<DockerResult<DockerFileListing>> ListFilesAsync(
            ConnectionProfile connection,
            DockerResourceReference resource,
            string path,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerFileListing>>(
                new DockerResult<DockerFileListing>.Success(
                    new DockerFileListing(resource, path, [])));

        public ValueTask<DockerResult<bool>> RunContainerActionAsync(
            ConnectionProfile connection,
            string containerId,
            DockerContainerAction action,
            CancellationToken cancellationToken)
        {
            ContainerActions.Add((containerId, action));
            return ValueTask.FromResult<DockerResult<bool>>(
                new DockerResult<bool>.Success(true));
        }
    }
}
