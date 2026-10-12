using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IWorkspaceIsolationProvider? _workspaceIsolationProvider;
    private readonly IWorkspaceIsolationRuntimeInstaller?
        _workspaceIsolationRuntimeInstaller;
    private readonly IWorkspaceRuntimeServicesFactory?
        _workspaceRuntimeServicesFactory;
    private readonly WorkspaceRuntimeServices _hostWorkspaceRuntimeServices;
    private readonly WorkspaceRuntimeLeaseCoordinator _workspaceRuntimeLeases;
    private Guid? _workspaceIsolationStartupId;
    private WorkspaceId? _workspaceIsolationStartingWorkspaceId;
    private string? _workspaceIsolationStartingWorkspaceName;
    private string? _workspaceIsolationStartingStatus;
    private double? _workspaceIsolationStartingFraction;
    private bool _workspaceIsolationStartupInFront;
    private RuntimeWorkspaceViewModel? _workspaceBehindIsolationProgress;
    private CancellationTokenSource? _workspaceIsolationStartupCancellation;
    private AsyncActionCommand? _cancelWorkspaceIsolationStartupCommand;

    public bool IsWorkspaceIsolationStarting =>
        _workspaceIsolationStartupId is not null;

    /// <summary>
    /// Whether the starting workspace's progress is what the canvas shows.
    /// Only the canvas: the rail, tab strip and title band stay live, so
    /// another workspace can be brought forward while this one starts, and
    /// choosing this one again brings its progress back.
    /// </summary>
    public bool IsWorkspaceIsolationProgressVisible =>
        IsWorkspaceIsolationStarting && _workspaceIsolationStartupInFront;

    /// <summary>
    /// The provider's measured progress, 0-100, when it can measure the
    /// current step; otherwise the bar is indeterminate.
    /// </summary>
    public double WorkspaceIsolationStartingPercent =>
        (_workspaceIsolationStartingFraction ?? 0) * 100;

    public bool HasWorkspaceIsolationStartingPercent =>
        _workspaceIsolationStartingFraction is not null;

    /// <summary>
    /// Cancel is offered while the isolated environment is being prepared -
    /// the image pull and container start, which are the long part. Once the
    /// panels are being restored there is nothing left worth stopping.
    /// </summary>
    public bool CanCancelWorkspaceIsolationStartup =>
        _workspaceIsolationStartupCancellation is { IsCancellationRequested: false };

    public System.Windows.Input.ICommand CancelWorkspaceIsolationStartupCommand =>
        _cancelWorkspaceIsolationStartupCommand ??= new AsyncActionCommand(
            () =>
            {
                CancelWorkspaceIsolationStartup();
                return Task.CompletedTask;
            },
            () => CanCancelWorkspaceIsolationStartup);

    /// <summary>Stops preparing the starting workspace; the one before it stays.</summary>
    public void CancelWorkspaceIsolationStartup()
    {
        if (_workspaceIsolationStartupCancellation is not { IsCancellationRequested: false } startup)
        {
            return;
        }

        startup.Cancel();
        RaiseWorkspaceIsolationCancellationChanged();
    }

    public string WorkspaceIsolationStartingHeading =>
        _workspaceIsolationStartingStatus
        ?? $"Starting {WorkspaceIsolationRuntimeDisplayName}…";

    public string WorkspaceIsolationStartingBody =>
        _workspaceIsolationStartingWorkspaceName is { } name
            ? $"Preparing the isolated environment for “{name}”."
            : "Preparing the workspace's isolated environment.";

    private string WorkspaceIsolationRuntimeDisplayName =>
        _workspaceIsolationRuntimeInstaller?.RuntimeDisplayName
        ?? _workspaceIsolationProvider?.Descriptor.DisplayName
        ?? "workspace isolation runtime";

    public bool CanInstallWorkspaceIsolationRuntime =>
        _workspaceIsolationProvider is null
        && _workspaceIsolationRuntimeInstaller is not null;

    private string? ActiveIsolationImageReference(WorkspaceId workspaceId)
    {
        foreach (var runtime in _openWorkspaces.Append(RuntimeWorkspace).OfType<RuntimeWorkspaceViewModel>())
        {
            if (_runtimeSources.TryGetValue(runtime.Id, out var source)
                && source.SourceDefinition.Kind == WorkspaceDefinition.Kind
                && string.Equals(
                    source.SourceDefinition.Value,
                    workspaceId.Value,
                    StringComparison.Ordinal))
            {
                return runtime.IsolationBinding?.RuntimeImageReference;
            }
        }

        return _workspaceRuntimeLeases.KnownIsolationImage(workspaceId);
    }

    private (bool Required, Uri? Proxy) AgentNetworkProxyFor(
        WorkspaceInstanceId workspaceId)
    {
        var runInIsolation = _runtimeSources.TryGetValue(workspaceId, out var source)
            && source.SourceDefinition.Kind == WorkspaceDefinition.Kind
            && _catalog.Snapshot.Workspaces.FirstOrDefault(
                item => item.Value.Key == source.SourceDefinition) is { } stored
            && stored.Value.RunAgentInIsolation;
        var runtimeServices = _workspaceRuntimeLeases.RuntimeServicesFor(workspaceId);
        if (runtimeServices?.IsNetworkBlocked is true)
        {
            return (true, null);
        }

        return (runInIsolation, runtimeServices?.EffectiveProxyUri);
    }

    public WorkspaceIsolationRuntimeInstallResult InstallWorkspaceIsolationRuntime()
    {
        ClearError();
        if (!CanInstallWorkspaceIsolationRuntime)
        {
            var unavailable = WorkspaceIsolationRuntimeInstallResult.Failure(
                "No workspace isolation runtime installer is available on this host.");
            SetError(unavailable.Error!);
            return unavailable;
        }

        var result = _workspaceIsolationRuntimeInstaller!.BeginInstallation();
        if (!result.Started)
        {
            SetError(result.Error!);
        }

        return result;
    }

    private void BeginWorkspaceIsolationStartup(
        WorkspaceDefinition workspace,
        Guid activationId)
    {
        _workspaceIsolationStartupId = activationId;
        _workspaceIsolationStartingWorkspaceId = workspace.Id;
        _workspaceIsolationStartingWorkspaceName = workspace.Name;
        _workspaceIsolationStartingStatus = null;
        _workspaceIsolationStartingFraction = null;
        _workspaceIsolationStartupInFront = true;
        RaiseWorkspaceIsolationStartupChanged();

        // Navigation changes before runtime preparation is awaited. The
        // requested workspace is therefore the foreground surface while its
        // platform runtime starts, rather than an error appearing over the
        // workspace the user was leaving.
        Route = ShellRoute.Workspace;
    }

    private void CompleteWorkspaceIsolationStartup(Guid activationId)
    {
        if (_workspaceIsolationStartupId != activationId)
        {
            return;
        }

        _workspaceIsolationStartupId = null;
        _workspaceIsolationStartingWorkspaceId = null;
        _workspaceIsolationStartingWorkspaceName = null;
        _workspaceIsolationStartingStatus = null;
        _workspaceIsolationStartingFraction = null;
        _workspaceIsolationStartupInFront = false;
        RaiseWorkspaceIsolationStartupChanged();
    }

    /// <summary>
    /// Another open workspace was chosen while this one starts. Its progress
    /// steps aside so that workspace's canvas shows; the start carries on.
    /// </summary>
    private void StepWorkspaceIsolationStartupAside()
    {
        if (!IsWorkspaceIsolationProgressVisible)
        {
            return;
        }

        _workspaceIsolationStartupInFront = false;
        RaiseWorkspaceIsolationStartupChanged();
    }

    /// <summary>
    /// Whether this start's progress was moved aside for another workspace,
    /// which then keeps the front when the start finishes.
    /// </summary>
    private bool IsWorkspaceIsolationStartupSteppedAside(Guid activationId) =>
        _workspaceIsolationStartupId == activationId && !_workspaceIsolationStartupInFront;

    /// <summary>
    /// The name the window title carries while the starting workspace's
    /// progress is in front. The workspace behind the progress is not what
    /// the window shows, so its name and tab are not the title.
    /// </summary>
    public string? WorkspaceIsolationProgressTitle =>
        IsWorkspaceIsolationProgressVisible ? _workspaceIsolationStartingWorkspaceName : null;

    /// <summary>
    /// The progress covers only the canvas, so the tab strip still belongs
    /// to the workspace behind it. Picking one of its tabs, opening or
    /// closing one, moves the progress aside to show what that changed,
    /// instead of acting out of sight behind the progress.
    /// </summary>
    private void FollowWorkspaceBehindIsolationProgress()
    {
        var covered = IsWorkspaceIsolationProgressVisible ? RuntimeWorkspace : null;
        if (ReferenceEquals(covered, _workspaceBehindIsolationProgress))
        {
            return;
        }

        if (_workspaceBehindIsolationProgress is { } previous)
        {
            previous.PropertyChanged -= OnWorkspaceBehindIsolationProgressChanged;
            previous.Tabs.CollectionChanged -= OnWorkspaceBehindIsolationProgressTabsChanged;
        }

        _workspaceBehindIsolationProgress = covered;
        if (covered is not null)
        {
            covered.PropertyChanged += OnWorkspaceBehindIsolationProgressChanged;
            covered.Tabs.CollectionChanged += OnWorkspaceBehindIsolationProgressTabsChanged;
        }
    }

    private void OnWorkspaceBehindIsolationProgressChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(
            e.PropertyName,
            nameof(RuntimeWorkspaceViewModel.ActiveTab),
            StringComparison.Ordinal))
        {
            StepWorkspaceIsolationStartupAside();
        }
    }

    private void OnWorkspaceBehindIsolationProgressTabsChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        StepWorkspaceIsolationStartupAside();
    }

    /// <summary>
    /// The workspace that is starting was chosen again: show its progress
    /// rather than trying to open it a second time.
    /// </summary>
    private bool TryBringWorkspaceIsolationStartupForward(WorkspaceId workspaceId)
    {
        if (_workspaceIsolationStartingWorkspaceId != workspaceId)
        {
            return false;
        }

        if (!_workspaceIsolationStartupInFront)
        {
            _workspaceIsolationStartupInFront = true;
            RaiseWorkspaceIsolationStartupChanged();
        }

        Route = ShellRoute.Workspace;
        return true;
    }

    private void RaiseWorkspaceIsolationStartupChanged()
    {
        OnPropertyChanged(nameof(IsWorkspaceIsolationStarting));
        OnPropertyChanged(nameof(IsWorkspaceIsolationProgressVisible));
        OnPropertyChanged(nameof(WorkspaceIsolationStartingHeading));
        OnPropertyChanged(nameof(WorkspaceIsolationStartingBody));
        OnPropertyChanged(nameof(WorkspaceIsolationStartingPercent));
        OnPropertyChanged(nameof(HasWorkspaceIsolationStartingPercent));
        OnPropertyChanged(nameof(IsWorkspaceCanvasVisible));
        OnPropertyChanged(nameof(WorkspaceIsolationProgressTitle));
        FollowWorkspaceBehindIsolationProgress();
        RefreshWorkspaceRuntimeFlags();
    }

    private void RaiseWorkspaceIsolationCancellationChanged()
    {
        OnPropertyChanged(nameof(CanCancelWorkspaceIsolationStartup));
        _cancelWorkspaceIsolationStartupCommand?.RaiseCanExecuteChanged();
    }

    private void ReportWorkspaceIsolationProgress(WorkspaceIsolationProgress progress)
    {
        if (_workspaceIsolationStartupId is null
            || (string.Equals(
                    _workspaceIsolationStartingStatus,
                    progress.Status,
                    StringComparison.Ordinal)
                && _workspaceIsolationStartingFraction == progress.Fraction))
        {
            return;
        }

        _workspaceIsolationStartingStatus = progress.Status;
        _workspaceIsolationStartingFraction = progress.Fraction;
        OnPropertyChanged(nameof(WorkspaceIsolationStartingHeading));
        OnPropertyChanged(nameof(WorkspaceIsolationStartingPercent));
        OnPropertyChanged(nameof(HasWorkspaceIsolationStartingPercent));
    }

    private ValueTask<WorkspaceIsolationPreparation> PrepareWorkspaceIsolationAsync(
        WorkspaceDefinition workspace,
        CancellationToken cancellationToken)
    {
        if (_shutdownStarted || _runtimeGraphLifetime.IsCancellationRequested)
        {
            SetError("This window is closing, so the workspace can't open here. Open it in another window.");
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
        }

        if (!workspace.IsIsolated)
        {
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Host);
        }

        if (_workspaceIsolationProvider is null)
        {
            SetError("Workspace isolation isn't available on this platform.");
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
        }

        if (!_workspaceRuntimeLeases.TryBeginPreparation(
                _shutdownStarted || _runtimeGraphLifetime.IsCancellationRequested,
                out var preparationId,
                out var completion))
        {
            SetError("This window is closing, so the workspace can't open here. Open it in another window.");
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
        }

        return PrepareTrackedWorkspaceIsolationAsync(
            workspace,
            preparationId,
            completion,
            cancellationToken);
    }

    private async ValueTask<WorkspaceIsolationPreparation>
        PrepareTrackedWorkspaceIsolationAsync(
            WorkspaceDefinition workspace,
            Guid preparationId,
            TaskCompletionSource completion,
            CancellationToken cancellationToken)
    {
        // Only the visible start of this workspace can be cancelled from its
        // progress; a preparation nobody can see has no Cancel to press.
        using var userCancellation = _workspaceIsolationStartingWorkspaceId == workspace.Id
            ? new CancellationTokenSource()
            : null;
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            userCancellation?.Token ?? CancellationToken.None);
        if (userCancellation is not null)
        {
            _workspaceIsolationStartupCancellation = userCancellation;
            RaiseWorkspaceIsolationCancellationChanged();
        }

        try
        {
            return await PrepareWorkspaceIsolationCoreAsync(workspace, preparation.Token);
        }
        catch (OperationCanceledException) when (_runtimeGraphLifetime.IsCancellationRequested)
        {
            return WorkspaceIsolationPreparation.Failed;
        }
        catch (OperationCanceledException) when (userCancellation?.IsCancellationRequested == true)
        {
            // Asked for, so nothing to report: the workspace that was in front
            // before the start comes back.
            return WorkspaceIsolationPreparation.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetError("Opening the isolated workspace was canceled.");
            return WorkspaceIsolationPreparation.Failed;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SecretSafeDiagnostics.WriteTraceAndStandardError(
                "workspace-isolation.prepare.failed",
                exception);
            SetError("Asura couldn't set up the isolated workspace. Check the workspace's isolation settings, then try again.");
            return WorkspaceIsolationPreparation.Failed;
        }
        finally
        {
            if (userCancellation is not null
                && ReferenceEquals(_workspaceIsolationStartupCancellation, userCancellation))
            {
                _workspaceIsolationStartupCancellation = null;
                RaiseWorkspaceIsolationCancellationChanged();
            }

            _workspaceRuntimeLeases.CompletePreparation(preparationId, completion);
        }
    }

    private async ValueTask<WorkspaceIsolationPreparation>
        PrepareWorkspaceIsolationCoreAsync(
            WorkspaceDefinition workspace,
            CancellationToken cancellationToken)
    {
        var provider = _workspaceIsolationProvider
            ?? throw new InvalidOperationException(
                "Workspace isolation preparation requires a platform provider.");
        var result = await provider.PrepareAsync(
            new WorkspaceIsolationPrepareRequest(
                workspace.Id,
                IsolationMountsOf(workspace),
                workspace.IsolationImageReference),
            new Progress<WorkspaceIsolationProgress>(ReportWorkspaceIsolationProgress),
            cancellationToken);
        if (result is WorkspaceIsolationResult<WorkspaceIsolationBinding>.Failure failure)
        {
            if (failure.CleanupValue is { } cleanupBinding)
            {
                _workspaceRuntimeLeases.TryOwnPreparedBinding(cleanupBinding);

                if (await ReleasePreparedWorkspaceIsolationAsync(
                        cleanupBinding,
                        CancellationToken.None) is not null)
                {
                    SecretSafeDiagnosticProjection.WriteStandardError(
                        "workspace-isolation.failed-prepare-stop.failed",
                        SecretSafeDiagnosticKind.Unexpected);
                }
            }

            SetError(WorkspaceIsolationText.Message(failure.Error));
            return WorkspaceIsolationPreparation.Failed;
        }

        var binding = ((WorkspaceIsolationResult<WorkspaceIsolationBinding>.Success)result).Value;
        if (binding.WorkspaceId != workspace.Id
            || binding.Provider != provider.Descriptor.Id)
        {
            _workspaceRuntimeLeases.TryOwnPreparedBinding(binding);

            var cleanupError = await ReleasePreparedWorkspaceIsolationAsync(
                binding,
                CancellationToken.None);
            SetError("Asura couldn't start the isolated workspace. Close the workspace and open it again.");
            if (cleanupError is not null)
            {
                SecretSafeDiagnosticProjection.WriteStandardError(
                    "workspace-isolation.invalid-binding-stop.failed",
                    SecretSafeDiagnosticKind.Unexpected);
            }

            return WorkspaceIsolationPreparation.Failed;
        }

        _workspaceRuntimeLeases.OwnPreparedBinding(binding);

        return new WorkspaceIsolationPreparation(true, binding);
    }

    private ValueTask<WorkspaceIsolationPreparation> PrepareRecoveredWorkspaceIsolationAsync(
        RuntimeWorkspaceRecoveryPayload recovered,
        Guid activationId,
        CancellationToken cancellationToken)
    {
        if (recovered.HistorySource?.ToHistorySource() is not { } source
            || source.SourceDefinition.Kind != WorkspaceDefinition.Kind)
        {
            if (recovered.IsIsolated)
            {
                SetError(
                    "The recovered isolated workspace has no saved workspace identity. "
                    + "Recovery was stopped to prevent host execution.");
                return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
            }

            return ValueTask.FromResult(WorkspaceIsolationPreparation.Host);
        }

        var workspace = _catalog.Snapshot.Workspaces
            .Select(item => item.Value)
            .FirstOrDefault(candidate => candidate.Key == source.SourceDefinition);
        if (workspace is null)
        {
            if (recovered.IsIsolated)
            {
                SetError(
                    "Asura couldn't find the saved settings for this isolated workspace, so it didn't restore it. "
                    + "Restoring it without them would run its commands outside the isolated environment.");
                return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
            }

            return ValueTask.FromResult(WorkspaceIsolationPreparation.Host);
        }

        if (workspace.IsIsolated != recovered.IsIsolated)
        {
            SetError(
                "Workspace isolation changed since this recovery snapshot was written. "
                + "Discard the snapshot and reopen the workspace.");
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
        }

        if (workspace.IsIsolated
            && !IsolationMountsOf(workspace).SequenceEqual(
                recovered.IsolationMounts?.Select(mount => mount.ToMount()) ?? []))
        {
            SetError(
                "Workspace mounts changed since this recovery snapshot was written. "
                + "Discard the snapshot and reopen the workspace.");
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
        }

        if (workspace.IsIsolated
            && !string.Equals(
                workspace.IsolationImageReference,
                recovered.IsolationImageReference,
                StringComparison.Ordinal))
        {
            SetError(
                "The workspace runtime image changed since this recovery snapshot was written. "
                + "Discard the snapshot and reopen the workspace.");
            return ValueTask.FromResult(WorkspaceIsolationPreparation.Failed);
        }

        if (workspace.IsIsolated)
        {
            // Session restore needs the same visible preparation state as a
            // manual open, before the first provider operation can suspend.
            BeginWorkspaceIsolationStartup(workspace, activationId);
        }

        return PrepareWorkspaceIsolationAsync(workspace, cancellationToken);
    }

    private void RegisterWorkspaceConnectionRuntime(RuntimeWorkspaceViewModel workspace)
    {
        if (_shutdownStarted || _runtimeGraphLifetime.IsCancellationRequested)
        {
            throw new OperationCanceledException(_runtimeGraphLifetime.Token);
        }

        _workspaceRuntimeLeases.Register(workspace.Id, workspace.IsolationBinding);
    }

    private IConnectionRuntime ConnectionRuntimeFor(WorkspaceInstanceId workspaceId)
    {
        var workspace = _openWorkspaces.FirstOrDefault(candidate => candidate.Id == workspaceId)
            ?? (RuntimeWorkspace?.Id == workspaceId ? RuntimeWorkspace : null);
        if (workspace is not null && !IsolationIntentMatches(workspace))
        {
            return WorkspaceIsolationUnavailableConnectionRuntime.ScopeChanged;
        }

        if (_workspaceRuntimeLeases.ConnectionRuntimeFor(workspaceId) is { } runtime)
        {
            return runtime;
        }

        return workspace is null || workspace.IsolationBinding is null
            ? _connectionRuntime
            : WorkspaceIsolationUnavailableConnectionRuntime.BindingMissing;
    }

    private bool IsolationIntentMatches(RuntimeWorkspaceViewModel runtime)
    {
        if (!_runtimeSources.TryGetValue(runtime.Id, out var source)
            || source.SourceDefinition.Kind != WorkspaceDefinition.Kind
            || _catalog.Snapshot.Workspaces.FirstOrDefault(
                item => item.Value.Key == source.SourceDefinition) is not { } stored)
        {
            return true;
        }

        if (stored.Value.IsIsolated != (runtime.IsolationBinding is not null))
        {
            return false;
        }

        return runtime.IsolationBinding is not { } binding
            || (IsolationMountsOf(stored.Value).SequenceEqual(binding.Mounts)
                && string.Equals(
                    stored.Value.IsolationImageReference,
                    binding.ImageReference,
                    StringComparison.Ordinal));
    }

    private bool WorkspaceIsolationConfigurationMatches(
        WorkspaceDefinition expected)
    {
        var current = _catalog.Snapshot.Workspaces
            .FirstOrDefault(item => item.Value.Key == expected.Key);
        return current is not null
            && current.Value.IsIsolated == expected.IsIsolated
            && IsolationMountsOf(current.Value).SequenceEqual(
                IsolationMountsOf(expected))
            && string.Equals(
                current.Value.IsolationImageReference,
                expected.IsolationImageReference,
                StringComparison.Ordinal);
    }

    private bool RecoveredWorkspaceIsolationConfigurationMatches(
        RuntimeWorkspaceRecoveryPayload recovered)
    {
        if (recovered.HistorySource?.ToHistorySource() is not { } source
            || source.SourceDefinition.Kind != WorkspaceDefinition.Kind)
        {
            return !recovered.IsIsolated;
        }

        var current = _catalog.Snapshot.Workspaces
            .FirstOrDefault(item => item.Value.Key == source.SourceDefinition);
        return current is not null
            && current.Value.IsIsolated == recovered.IsIsolated
            && (!recovered.IsIsolated
                || (IsolationMountsOf(current.Value).SequenceEqual(
                        recovered.IsolationMounts?.Select(mount => mount.ToMount()) ?? [])
                    && string.Equals(
                        current.Value.IsolationImageReference,
                        recovered.IsolationImageReference,
                        StringComparison.Ordinal)));
    }

    private bool IsolationBadgeFor(WorkspaceDefinition definition) =>
        FindOpenWorkspace(definition.Key) is { } runtime
            ? runtime.IsolationBinding is not null
            : definition.IsIsolated;

    private static IReadOnlyList<WorkspaceIsolationMount> IsolationMountsOf(
        WorkspaceDefinition workspace) =>
        [.. workspace.IsolationMounts.Select(mount => new WorkspaceIsolationMount(
            mount.HostPath,
            mount.GuestPath,
            mount.IsReadOnly))];

    private async ValueTask<WorkspaceIsolationError?> ReleaseWorkspaceIsolationAsync(
        RuntimeWorkspaceViewModel workspace,
        CancellationToken cancellationToken) =>
        await ReleaseWorkspaceIsolationAsync(workspace.Id, cancellationToken);

    /// <summary>
    /// Starts the asynchronous part of closing an isolated workspace from sync-only
    /// graph and presentation callbacks. The task remains tracked until shutdown so
    /// every close path observes the same release attempt and shutdown can retry a
    /// failed stop without losing the exact lease.
    /// </summary>
    private Task<WorkspaceIsolationError?> ScheduleWorkspaceIsolationCleanup(
        RuntimeWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var schedule = _workspaceRuntimeLeases.ScheduleRelease(workspace.Id);
        return schedule.IsNew
            ? ReportWorkspaceRuntimeCleanupAsync(schedule.Completion)
            : schedule.Completion;
    }

    private async Task<WorkspaceIsolationError?> ReportWorkspaceRuntimeCleanupAsync(
        Task<WorkspaceIsolationError?> cleanup)
    {
        var error = await cleanup.ConfigureAwait(false);

        if (error is not null && !_shutdownStarted)
        {
            await _uiThreadDispatcher.InvokeAsync(
                    () => SetError(WorkspaceIsolationText.Message(error)),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return error;
    }

    private async Task AwaitWorkspaceIsolationCleanupAsync()
        => await _workspaceRuntimeLeases.AwaitScheduledCleanupAsync()
            .ConfigureAwait(false);

    private async Task AwaitWorkspaceIsolationPreparationsAsync()
        => await _workspaceRuntimeLeases.AwaitPreparationsAsync().ConfigureAwait(false);

    private bool TryBeginWorkspaceActivation(
        out Guid activationId,
        out TaskCompletionSource completion)
    {
        return _workspaceRuntimeLeases.TryBeginActivation(
            _shutdownStarted || _runtimeGraphLifetime.IsCancellationRequested,
            out activationId,
            out completion);
    }

    private void CompleteWorkspaceActivation(
        Guid activationId,
        TaskCompletionSource completion)
    {
        _workspaceRuntimeLeases.CompleteActivation(activationId, completion);
    }

    private async Task AwaitWorkspaceActivationsAsync()
        => await _workspaceRuntimeLeases.AwaitActivationsAsync().ConfigureAwait(false);

    private Task[] BeginWindowCloseActivationDrain()
        => _workspaceRuntimeLeases.BeginWindowCloseActivationDrain();

    internal void ResumeAfterWindowCloseAttempt()
        => _workspaceRuntimeLeases.ResumeAfterWindowCloseAttempt(_shutdownStarted);

    private void MarkWorkspaceGraphRegistrationAttempt(WorkspaceInstanceId workspaceId)
        => _workspaceRuntimeLeases.MarkGraphRegistrationAttempt(workspaceId);

    private void RetainWorkspaceGraph(WorkspaceInstanceId workspaceId)
        => _workspaceRuntimeLeases.RetainGraph(workspaceId);

    private bool RequiresWorkspaceGraphRollback(WorkspaceInstanceId workspaceId)
        => _workspaceRuntimeLeases.RequiresGraphRollback(workspaceId);

    private async Task<bool> TryRollbackWorkspaceGraphAsync(
        WorkspaceInstanceId workspaceId)
    {
        HostResult<CloseScopeResult> result;
        try
        {
            result = await RuntimeGraph.CloseAsync(
                    CloseScopeRequest.Workspace(workspaceId, CloseDecision.Confirm),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SecretSafeDiagnostics.WriteTraceAndStandardError(
                "runtime-graph.registration-rollback.failed",
                exception);
            return false;
        }

        if (result is not HostResult<CloseScopeResult>.Success
            {
                Value: CloseScopeResult.Completed completed,
            }
            || completed.Scope != CloseScopeKind.Workspace
            || !string.Equals(
                completed.TargetId,
                workspaceId.Value,
                StringComparison.Ordinal)
            || completed.Sessions.Any(session => session.Outcome is not (
                SessionCloseOutcome.GracefullyClosed
                or SessionCloseOutcome.ForceTerminated
                or SessionCloseOutcome.AlreadyClosed)))
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "runtime-graph.registration-rollback.rejected",
                SecretSafeDiagnosticKind.Unexpected);
            return false;
        }

        _workspaceRuntimeLeases.RetainGraph(workspaceId);

        return true;
    }

    private async Task RetryWorkspaceGraphRollbacksAsync()
    {
        foreach (var workspaceId in _workspaceRuntimeLeases.PendingGraphRollbacks())
        {
            _ = await TryRollbackWorkspaceGraphAsync(workspaceId).ConfigureAwait(false);
        }
    }

    private async ValueTask<WorkspaceIsolationError?> ReleaseWorkspaceIsolationAsync(
        WorkspaceInstanceId workspaceId,
        CancellationToken cancellationToken)
        => await _workspaceRuntimeLeases.ReleaseAsync(workspaceId, cancellationToken);

    private async ValueTask<WorkspaceIsolationError?> ReleasePreparedWorkspaceIsolationAsync(
        WorkspaceIsolationBinding binding,
        CancellationToken cancellationToken)
        => await _workspaceRuntimeLeases.ReleasePreparedAsync(binding, cancellationToken);

    private async Task<IReadOnlyList<WorkspaceIsolationError>> ReleaseAllWorkspaceIsolationAsync()
        => await _workspaceRuntimeLeases.ReleaseAllAsync();

    internal static bool SharesExecutionScope(
        RuntimeWorkspaceViewModel source,
        RuntimeWorkspaceViewModel destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        return (source.IsolationBinding, destination.IsolationBinding) switch
        {
            (null, null) => true,
            ({ } sourceBinding, { } destinationBinding) =>
                sourceBinding.Provider == destinationBinding.Provider
                && string.Equals(
                    sourceBinding.ResourceName,
                    destinationBinding.ResourceName,
                    StringComparison.Ordinal),
            _ => false,
        };
    }

    private readonly record struct WorkspaceIsolationPreparation(
        bool Succeeded,
        WorkspaceIsolationBinding? Binding)
    {
        public static WorkspaceIsolationPreparation Host { get; } = new(true, null);

        public static WorkspaceIsolationPreparation Failed { get; } = new(false, null);
    }
}
