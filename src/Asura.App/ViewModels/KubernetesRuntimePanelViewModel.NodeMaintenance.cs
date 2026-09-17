using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private KubernetesNodeSchedulingRequest? _schedulingReview;
    private KubernetesDrainReview? _drainReview;
    private bool _allowUnmanagedPods;
    private bool _deleteEmptyDirData;
    private string _nodeStatus = "Preview the exact node operation before confirming.";
    private IReadOnlyList<KubernetesDrainPod> _drainPods = [];
    private IReadOnlyList<KubernetesDrainPodResult> _drainResults = [];
    public bool HasNodeMaintenance => _inspection is { Kind: "Node", Reference.Resource: "nodes" }
        && _session?.Features.HasFlag(KubernetesSessionFeatures.NodeMaintenance) == true;
    public bool AllowUnmanagedPods { get => _allowUnmanagedPods; set { if (SetProperty(ref _allowUnmanagedPods, value)) { ClearNodeReview(); } } }
    public bool DeleteEmptyDirData { get => _deleteEmptyDirData; set { if (SetProperty(ref _deleteEmptyDirData, value)) { ClearNodeReview(); } } }
    public string NodeStatus { get => _nodeStatus; private set => SetProperty(ref _nodeStatus, value); }
    public IReadOnlyList<KubernetesDrainPod> DrainPods { get => _drainPods; private set => SetProperty(ref _drainPods, value); }
    public IReadOnlyList<KubernetesDrainPodResult> DrainResults { get => _drainResults; private set => SetProperty(ref _drainResults, value); }
    public bool CanConfirmNode => !IsBusy && (_schedulingReview?.Node == _inspection?.Reference && _schedulingReview is not null
        || _drainReview is { } review && review.Node == _inspection?.Reference && review.ExpiresAt > DateTimeOffset.UtcNow
            && review.Pods.All(pod => pod.Disposition is not (KubernetesDrainPodDisposition.BlockUnmanaged or KubernetesDrainPodDisposition.BlockEmptyDir)));
    public async Task ReviewNodeSchedulingAsync(bool unschedulable)
    {
        if (!HasNodeMaintenance || _session is null || _inspection is null || IsBusy || HasUnsavedChanges) { return; }
        ClearNodeReview();
        var request = new KubernetesNodeSchedulingRequest(_inspection.Reference, unschedulable);
        IsBusy = true;
        try
        {
            var result = await _session.SetNodeSchedulableAsync(request, _lifetime.Token);
            if (_disposed || _inspection?.Reference != request.Node) { return; }
            if (result.Outcome == KubernetesMutationOutcome.DryRun)
            {
                _schedulingReview = request;
                NodeStatus = $"{(unschedulable ? "Cordon" : "Uncordon")} {ContextName}/{request.Node.Name}. Server dry run succeeded.";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    public async Task ReviewNodeDrainAsync()
    {
        if (!HasNodeMaintenance || _session is null || _inspection is null || IsBusy || HasUnsavedChanges) { return; }
        ClearNodeReview();
        var request = new KubernetesNodeDrainRequest(_inspection.Reference, new(AllowUnmanagedPods, DeleteEmptyDirData));
        IsBusy = true;
        try
        {
            var review = await _session.ReviewNodeDrainAsync(request, _lifetime.Token);
            if (_disposed || _inspection?.Reference != request.Node || request.Options != new KubernetesDrainOptions(AllowUnmanagedPods, DeleteEmptyDirData)) { return; }
            _drainReview = review;
            DrainPods = review.Pods;
            NodeStatus = $"Drain {ContextName}/{review.Node.Name}. Preview expires {review.ExpiresAt:HH:mm:ss} UTC. Drain cordons the node and evicts pods; it can complete partially and respects disruption budgets.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    public async Task ConfirmNodeAsync()
    {
        if (!CanConfirmNode || _session is null) { return; }
        var scheduling = _schedulingReview;
        var drain = _drainReview;
        ClearNodeReview();
        IsBusy = true;
        try
        {
            if (scheduling is not null)
            {
                var result = await _session.SetNodeSchedulableAsync(scheduling with { DryRun = false }, _lifetime.Token);
                NodeStatus = result.Outcome == KubernetesMutationOutcome.OutcomeUnknown ? "Scheduling outcome unknown. Inspect the node before retrying." : $"Scheduling operation: {result.Outcome}.";
            }
            else if (drain is not null)
            {
                var result = await _session.ExecuteNodeDrainAsync(drain.ReviewToken, _lifetime.Token);
                DrainResults = result.Pods;
                NodeStatus = $"Drain: {result.Outcome}. Node cordoned: {result.NodeCordoned?.ToString() ?? "unknown"}. Review every pod result before another operation.";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    private void ClearNodeReview()
    {
        _schedulingReview = null; _drainReview = null; DrainPods = []; DrainResults = [];
        OnPropertyChanged(nameof(CanConfirmNode));
    }
}
