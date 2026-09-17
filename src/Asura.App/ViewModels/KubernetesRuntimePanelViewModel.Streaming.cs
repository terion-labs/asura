using System.Text.Json;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private CancellationTokenSource? _watchCancellation;
    private CancellationTokenSource? _followCancellation;
    private string _resourceVersion = string.Empty;
    private bool _followingLogs;
    private string _shellExecutable = "/bin/sh";
    public IReadOnlyList<string> ShellExecutables { get; } = ["/bin/sh", "/bin/bash"];
    public string ShellExecutable { get => _shellExecutable; set => SetProperty(ref _shellExecutable, value); }
    public IReadOnlyList<string> ContainerChoices
    {
        get
        {
            if (_inspection is null) { return []; }
            using var json = JsonDocument.Parse(_inspection.Json);
            if (!json.RootElement.TryGetProperty("spec", out var spec) || !spec.TryGetProperty("containers", out var containers)) { return []; }
            return [.. containers.EnumerateArray().Where(item => item.TryGetProperty("name", out _))
                .Select(item => item.GetProperty("name").GetString()).OfType<string>()];
        }
    }
    public bool CanLaunchShell => CanOpenShell && (ContainerChoices.Contains(Container, StringComparer.Ordinal) || ContainerChoices.Count == 1);
    public KubernetesExecRequest? ShellRequest => CanLaunchShell && _inspection is { } pod
        ? new(pod.Reference, ContainerChoices.Contains(Container, StringComparer.Ordinal) ? Container : ContainerChoices[0], [ShellExecutable]) : null;
    public bool CanFollowLogs => CanReadLogs && _session?.Features.HasFlag(KubernetesSessionFeatures.FollowLogs) == true;
    public bool CanOpenShell => CanReadLogs && _session?.Features.HasFlag(KubernetesSessionFeatures.Exec) == true;
    public bool FollowingLogs { get => _followingLogs; private set => SetProperty(ref _followingLogs, value); }

    private void StartWatching()
    {
        if (_disposed || _loadedRequest is null || _continuation is not null
            || _session?.Features.HasFlag(KubernetesSessionFeatures.Watch) != true) { return; }
        StopWatching();
        _watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = WatchResourcesAsync(_watchCancellation, _generation, _loadedRequest);
    }

    private void StopWatching() => _watchCancellation?.Cancel();

    private async Task WatchResourcesAsync(CancellationTokenSource cancellation, int generation, KubernetesListRequest request)
    {
        try
        {
            await foreach (var change in _session!.WatchAsync(new(request.ApiResource, request.Namespace, _resourceVersion), cancellation.Token))
            {
                if (_disposed || generation != _generation || cancellation.IsCancellationRequested) { return; }
                if (change.Kind == KubernetesWatchEventKind.ResyncRequired)
                {
                    Status = "Resource history expired. Reloading…";
                    if (!HasUnsavedChanges) { await RefreshAsync(); }
                    else { Issue = "Resource history expired. Finish your manifest edits, then refresh."; }
                    return;
                }
                _resourceVersion = change.ResourceVersion;
                if (change.Resource is not { } resource) { continue; }
                var selectedUid = SelectedResource?.Reference.Uid;
                var rows = _allResources.Where(item => !string.Equals(item.Reference.Uid, resource.Reference.Uid, StringComparison.Ordinal));
                _allResources = change.Kind == KubernetesWatchEventKind.Deleted
                    ? [.. rows] : [.. rows.Append(resource).OrderBy(item => item.Reference.Name, StringComparer.Ordinal).Take(10000)];
                ApplyFilter();
                if (string.Equals(selectedUid, resource.Reference.Uid, StringComparison.Ordinal))
                {
                    ApplySelectedWatchChange(change.Kind, resource);
                }
                Status = $"{_allResources.Count} resources · Live";
            }
            if (!cancellation.IsCancellationRequested) { Status = "Live updates stopped. Refresh to reconnect."; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (KubernetesRequestException exception)
        {
            if (exception.Code == KubernetesErrorCode.ResourceExpired && !HasUnsavedChanges && !IsBusy) { await RefreshAsync(); }
            else if (!cancellation.IsCancellationRequested) { PresentError(exception); }
        }
        finally
        {
            if (ReferenceEquals(_watchCancellation, cancellation)) { _watchCancellation = null; }
            cancellation.Dispose();
        }
    }

    private void ApplySelectedWatchChange(KubernetesWatchEventKind kind, KubernetesResourceDocument resource)
    {
        var dirty = HasUnsavedChanges;
        InvalidateManifestReview();
        ClearOperationReview();
        ClearNodeReview();
        if (kind == KubernetesWatchEventKind.Deleted)
        {
            StopFollowingLogs();
            if (!dirty) { SelectedResource = null; }
            else { _inspection = null; Issue = "The selected resource was deleted. Your manifest draft is retained for copying."; }
        }
        else
        {
            _selectionCancellation?.Cancel();
            SetProperty(ref _selectedResource, resource, nameof(SelectedResource));
            if (dirty)
            {
                Issue = "The resource changed on the server. Your draft retains its original version; copy it if needed, then discard edits to load the current resource.";
            }
            else { _inspection = resource; }
        }
        PublishSelection();
    }

    private static bool SameResourceIdentity(KubernetesResourceReference? first, KubernetesResourceReference second) =>
        first is not null && string.Equals(first.Uid, second.Uid, StringComparison.Ordinal)
        && string.Equals(first.Name, second.Name, StringComparison.Ordinal)
        && string.Equals(first.Namespace, second.Namespace, StringComparison.Ordinal)
        && string.Equals(first.Resource, second.Resource, StringComparison.Ordinal)
        && string.Equals(first.Group, second.Group, StringComparison.Ordinal)
        && string.Equals(first.Version, second.Version, StringComparison.Ordinal);

    public void StopFollowingLogs()
    {
        _followCancellation?.Cancel();
        FollowingLogs = false;
    }

    public async Task FollowLogsAsync()
    {
        if (!CanFollowLogs || _session is null || SelectedResource is not { } resource || FollowingLogs || _disposed) { return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _followCancellation = cancellation;
        FollowingLogs = true;
        try
        {
            await foreach (var text in _session.FollowLogsAsync(new(resource.Reference,
                string.IsNullOrWhiteSpace(Container) ? null : Container.Trim(), Previous: PreviousLogs), cancellation.Token))
            {
                if (cancellation.IsCancellationRequested || !SameResourceIdentity(SelectedResource?.Reference, resource.Reference)) { return; }
                var combined = Logs + text;
                Logs = combined.Length > 262144 ? combined[^262144..] : combined;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { if (!cancellation.IsCancellationRequested) { PresentError(exception); } }
        finally
        {
            if (ReferenceEquals(_followCancellation, cancellation)) { _followCancellation = null; FollowingLogs = false; }
        }
    }
}
