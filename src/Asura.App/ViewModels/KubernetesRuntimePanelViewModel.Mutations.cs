using System.Windows.Input;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private readonly AsyncActionCommand _dryRunCommand;
    private readonly AsyncActionCommand _applyCommand;
    private readonly AsyncActionCommand _discardCommand;
    private bool _forceOwnership;
    private bool _reviewedForceOwnership;
    public bool ForceOwnership
    {
        get => _forceOwnership;
        set { if (!IsBusy && SetProperty(ref _forceOwnership, value)) { _reviewedManifest = null; PreviewManifest = string.Empty; PublishMutationState(); } }
    }
    private string? _manifestDraft;
    private string? _reviewedManifest;
    private string? _reviewedJson;
    private string _previewManifest = string.Empty;
    private KubernetesResourceReference? _reviewedResource;
    private string _mutationStatus = "Edit YAML or JSON. Review the server dry run before applying.";

    public string PreviewManifest { get => _previewManifest; private set => SetProperty(ref _previewManifest, FormatManifestJson(value)); }
    public bool HasMutationPreview => PreviewManifest.Length > 0;
    public ICommand DryRunCommand => _dryRunCommand;
    public ICommand ApplyManifestCommand => _applyCommand;
    public ICommand DiscardManifestCommand => _discardCommand;
    public string MutationStatus { get => _mutationStatus; private set => SetProperty(ref _mutationStatus, value); }
    public bool CanEditManifest => _session?.Features.HasFlag(KubernetesSessionFeatures.Mutations | KubernetesSessionFeatures.ManifestConversion) == true && _inspection is not null && SelectedKind?.Verbs.Contains("patch", StringComparer.Ordinal) == true
        && !string.Equals(SelectedKind.Resource, "secrets", StringComparison.Ordinal);
    public bool ManifestReadOnly => !CanEditManifest || IsBusy;
    public bool HasUnsavedChanges => _manifestDraft is not null && !string.Equals(_manifestDraft, FormattedManifest, StringComparison.Ordinal);
    public bool CanApplyManifest => !IsBusy && CanEditManifest && HasUnsavedChanges
        && _reviewedForceOwnership == ForceOwnership && _reviewedResource == _inspection?.Reference && string.Equals(_reviewedManifest, ManifestDraft, StringComparison.Ordinal);
    public string ManifestDraft
    {
        get => _manifestDraft ?? FormattedManifest;
        set
        {
            if (!CanEditManifest || IsBusy || string.Equals(value, ManifestDraft, StringComparison.Ordinal)) { return; }
            ClearOperationReview(); ClearNodeReview();
            _manifestDraft = string.Equals(value, FormattedManifest, StringComparison.Ordinal) ? null : value;
            _reviewedManifest = null;
            _reviewedJson = null;
            PreviewManifest = string.Empty;
            _reviewedResource = null;
            PublishMutationState();
        }
    }

    private void InvalidateManifestReview()
    {
        _reviewedManifest = null; _reviewedJson = null; _reviewedResource = null;
        PreviewManifest = string.Empty;
        PublishMutationState();
    }

    public void DiscardManifestChanges()
    {
        if (HasUnsavedChanges && _inspection is not null && SelectedResource is { } current
            && SameResourceIdentity(_inspection.Reference, current.Reference))
        {
            _inspection = current;
            Issue = null;
            OnPropertyChanged(nameof(Manifest)); OnPropertyChanged(nameof(Summary));
        }
        ClearOperationReview(); ClearNodeReview();
        _forceOwnership = false;
        OnPropertyChanged(nameof(ForceOwnership));
        _manifestDraft = null;
        _reviewedJson = null;
        PreviewManifest = string.Empty;
        _reviewedManifest = null;
        _reviewedResource = null;
        PublishMutationState();
    }

    private void PublishMutationState()
    {
        OnPropertyChanged(nameof(HasNodeMaintenance)); OnPropertyChanged(nameof(CanConfirmNode)); OnPropertyChanged(nameof(CanConfirmHelm));
        PublishOperationState();
        OnPropertyChanged(nameof(HasMutationPreview));
        OnPropertyChanged(nameof(FormattedManifest)); OnPropertyChanged(nameof(ManifestGrammarExtension));
        OnPropertyChanged(nameof(ManifestDraft)); OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanEditManifest)); OnPropertyChanged(nameof(ManifestReadOnly)); OnPropertyChanged(nameof(CanApplyManifest));
        _refreshCommand.RaiseCanExecuteChanged();
        _dryRunCommand.RaiseCanExecuteChanged(); _applyCommand.RaiseCanExecuteChanged(); _discardCommand.RaiseCanExecuteChanged();
    }

    public Task DryRunAsync() => RunManifestMutationAsync(dryRun: true);
    public Task ApplyManifestAsync() => RunManifestMutationAsync(dryRun: false);

    private async Task RunManifestMutationAsync(bool dryRun)
    {
        if (_session is null || _inspection is null || IsBusy || !CanEditManifest || !HasUnsavedChanges
            || !dryRun && !CanApplyManifest || _disposed) { return; }
        var body = ManifestDraft;
        var resource = _inspection.Reference;
        IsBusy = true;
        Issue = null;
        try
        {
            var json = dryRun ? await _session.ConvertManifestToJsonAsync(body, _lifetime.Token) : _reviewedJson!;
            var result = await _session.MutateAsync(new(resource, KubernetesMutationKind.Apply, json, DryRun: dryRun, ForceOwnership: ForceOwnership), _lifetime.Token);
            if (_disposed || _inspection?.Reference != resource || SelectedResource?.Reference != resource) { return; }
            MutationStatus = result.Outcome switch
            {
                KubernetesMutationOutcome.DryRun => "Server dry run succeeded. Apply sends these exact edits to this context.",
                KubernetesMutationOutcome.Applied => "Changes applied.",
                KubernetesMutationOutcome.OutcomeUnknown => "The connection ended after dispatch. Check the resource before making another change.",
                _ => "The change was not dispatched. Review the error and retry.",
            };
            if (dryRun && result.Outcome == KubernetesMutationOutcome.DryRun)
            {
                _reviewedForceOwnership = ForceOwnership;
                _reviewedManifest = body;
                _reviewedJson = json;
                PreviewManifest = result.Resource?.Json ?? json;
                _reviewedResource = resource;
            }
            else
            {
                _reviewedManifest = null;
                _reviewedResource = null;
                if (result.Outcome == KubernetesMutationOutcome.Applied)
                {
                    _manifestDraft = null;
                    if (result.Resource is { } updated) { _inspection = updated; }
                    PublishSelection();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (NotSupportedException) { Issue = "This execution backend does not support manifest editing."; }
        catch (KubernetesRequestException exception)
        {
            _reviewedManifest = null;
            _reviewedResource = null;
            PresentError(exception);
        }
        finally { if (!_disposed) { IsBusy = false; } }
    }
}
