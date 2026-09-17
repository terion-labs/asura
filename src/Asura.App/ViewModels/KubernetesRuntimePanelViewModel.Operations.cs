using System.Globalization;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private KubernetesMutationRequest? _reviewedOperation;
    private string _replicas = "1";
    private string _operationDescription = string.Empty;
    public string Replicas { get => _replicas; set { if (SetProperty(ref _replicas, value)) { ClearOperationReview(); } } }
    public string OperationDescription { get => _operationDescription; private set => SetProperty(ref _operationDescription, value); }
    public bool HasResourceActions => _session?.Features.HasFlag(KubernetesSessionFeatures.Mutations) == true && _inspection is not null
        && (SelectedKind?.Verbs.Contains("delete", StringComparer.Ordinal) == true || SelectedKind?.Verbs.Contains("patch", StringComparer.Ordinal) == true);
    public bool CanDelete => CanMutateSelection && SelectedKind?.Verbs.Contains("delete", StringComparer.Ordinal) == true;
    public bool CanScale => CanMutateSelection && SelectedKind?.Verbs.Contains("patch", StringComparer.Ordinal) == true
        && SelectedResource?.Kind is "Deployment" or "StatefulSet" or "ReplicaSet" or "ReplicationController";
    public bool CanRestart => CanMutateSelection && SelectedKind?.Verbs.Contains("patch", StringComparer.Ordinal) == true
        && SelectedResource?.Kind is "Deployment" or "StatefulSet" or "DaemonSet";
    public bool CanConfirmOperation => !IsBusy && _reviewedOperation?.Resource == _inspection?.Reference && _reviewedOperation is not null;
    public bool HasOperationReview => _reviewedOperation is not null;
    private bool CanMutateSelection => !IsBusy && !HasUnsavedChanges && _inspection is not null
        && _session?.Features.HasFlag(KubernetesSessionFeatures.Mutations) == true;

    public Task ReviewDeleteAsync() => CanDelete ? ReviewOperationAsync(KubernetesMutationKind.Delete, null, "Delete") : Task.CompletedTask;
    public Task ReviewScaleAsync()
    {
        if (!CanScale) { return Task.CompletedTask; }
        if (!int.TryParse(Replicas, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
        { Issue = "Enter a non-negative replica count."; return Task.CompletedTask; }
        var patch = new JsonArray(new JsonObject { ["op"] = "add", ["path"] = "/spec/replicas", ["value"] = count });
        return ReviewOperationAsync(KubernetesMutationKind.JsonPatch, patch.ToJsonString(), $"Scale to {count.ToString(CultureInfo.InvariantCulture)} replicas");
    }
    public Task ReviewRestartAsync()
    {
        if (!CanRestart || _inspection is null) { return Task.CompletedTask; }
        var resource = JsonNode.Parse(_inspection.Json);
        var metadata = resource?["spec"]?["template"]?["metadata"];
        var annotations = metadata?["annotations"];
        var patch = new JsonArray();
        if (metadata is null) { patch.Add((JsonNode)new JsonObject { ["op"] = "add", ["path"] = "/spec/template/metadata", ["value"] = new JsonObject() }); }
        if (annotations is null) { patch.Add((JsonNode)new JsonObject { ["op"] = "add", ["path"] = "/spec/template/metadata/annotations", ["value"] = new JsonObject() }); }
        patch.Add((JsonNode)new JsonObject
        {
            ["op"] = "add",
            ["path"] = "/spec/template/metadata/annotations/kubectl.kubernetes.io~1restartedAt",
            ["value"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
        return ReviewOperationAsync(KubernetesMutationKind.JsonPatch, patch.ToJsonString(), "Restart rollout");
    }
    private async Task ReviewOperationAsync(KubernetesMutationKind kind, string? body, string description)
    {
        if (_inspection is null || _session is null || IsBusy || HasUnsavedChanges || _disposed) { return; }
        ClearOperationReview();
        var request = new KubernetesMutationRequest(_inspection.Reference, kind, body);
        IsBusy = true;
        Issue = null;
        try
        {
            var result = await _session.MutateAsync(request, _lifetime.Token);
            if (_disposed || request.Resource != _inspection?.Reference) { return; }
            if (result.Outcome == KubernetesMutationOutcome.DryRun)
            {
                _reviewedOperation = request;
                OperationDescription = $"{description}: {ContextName} · {request.Resource.Namespace}/{request.Resource.Name}. Server dry run succeeded.";
                PreviewManifest = result.Resource?.Json ?? Manifest;
            }
            else { OperationDescription = "The operation could not be reviewed. No confirmation is available."; }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    public async Task ConfirmOperationAsync()
    {
        if (!CanConfirmOperation || _session is null || _reviewedOperation is not { } request || _disposed) { return; }
        IsBusy = true;
        ClearOperationReview();
        try
        {
            var result = await _session.MutateAsync(request with { DryRun = false }, _lifetime.Token);
            OperationDescription = result.Outcome switch
            {
                KubernetesMutationOutcome.Applied => "Operation completed. Refresh to inspect the current resource.",
                KubernetesMutationOutcome.OutcomeUnknown => "Connection ended after dispatch. Inspect the resource before trying another operation.",
                _ => "The operation was not dispatched.",
            };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    private void ClearOperationReview()
    {
        _reviewedOperation = null;
        OnPropertyChanged(nameof(HasOperationReview)); OnPropertyChanged(nameof(CanConfirmOperation));
    }
    private void PublishOperationState()
    {
        OnPropertyChanged(nameof(HasResourceActions)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(CanScale)); OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(HasOperationReview)); OnPropertyChanged(nameof(CanConfirmOperation));
    }
}
