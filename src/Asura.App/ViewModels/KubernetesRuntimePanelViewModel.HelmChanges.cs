using System.Globalization;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private KubernetesHelmChangeReview? _helmReview;
    private KubernetesHelmChangeKind _helmChangeKind;
    private string _chartReference = string.Empty;
    private string _chartVersion = string.Empty;
    private string _valuesYaml = string.Empty;
    private string _rollbackRevision = string.Empty;
    private bool _allowHooks;
    private bool _reuseValues = true;
    private string _helmChangeStatus = "Review the exact release change before confirming.";
    private int _helmEditGeneration;
    public IReadOnlyList<KubernetesHelmChangeKind> HelmChangeKinds { get; } = [KubernetesHelmChangeKind.Upgrade, KubernetesHelmChangeKind.Rollback, KubernetesHelmChangeKind.Uninstall];
    public bool HasHelmChanges => _session?.Features.HasFlag(KubernetesSessionFeatures.HelmChanges) == true && SelectedRelease is not null;
    public KubernetesHelmChangeKind HelmChangeKind { get => _helmChangeKind; set { if (SetProperty(ref _helmChangeKind, value)) { ClearHelmReview(); OnPropertyChanged(nameof(IsHelmUpgrade)); OnPropertyChanged(nameof(IsHelmRollback)); } } }
    public bool IsHelmUpgrade => HelmChangeKind == KubernetesHelmChangeKind.Upgrade;
    public bool IsHelmRollback => HelmChangeKind == KubernetesHelmChangeKind.Rollback;
    public string ChartReference { get => _chartReference; set { if (SetProperty(ref _chartReference, value)) { ClearHelmReview(); } } }
    public string ChartVersion { get => _chartVersion; set { if (SetProperty(ref _chartVersion, value)) { ClearHelmReview(); } } }
    public string ValuesYaml { get => _valuesYaml; set { if (SetProperty(ref _valuesYaml, value)) { ClearHelmReview(); } } }
    public string RollbackRevision { get => _rollbackRevision; set { if (SetProperty(ref _rollbackRevision, value)) { ClearHelmReview(); } } }
    public bool AllowHooks { get => _allowHooks; set { if (SetProperty(ref _allowHooks, value)) { ClearHelmReview(); } } }
    public bool ReuseValues { get => _reuseValues; set { if (SetProperty(ref _reuseValues, value)) { ClearHelmReview(); } } }
    public string HelmChangeStatus { get => _helmChangeStatus; private set => SetProperty(ref _helmChangeStatus, value); }
    public bool CanConfirmHelm => !IsBusy && _helmReview is { } review && review.ExpiresAt > DateTimeOffset.UtcNow && SelectedRelease is { } release
        && string.Equals(review.Release, release.Name, StringComparison.Ordinal) && string.Equals(review.Namespace, release.Namespace, StringComparison.Ordinal) && review.ExpectedRevision == release.Revision;
    public async Task ReviewHelmChangeAsync()
    {
        if (!HasHelmChanges || _session is null || SelectedRelease is not { } release || IsBusy) { return; }
        int? revision = null;
        if (IsHelmRollback)
        {
            if (!int.TryParse(RollbackRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            { Issue = "Enter a positive rollback revision from this release history."; return; }
            revision = number;
        }
        ClearHelmReview();
        var generation = _helmEditGeneration;
        var request = new KubernetesHelmChangeRequest(HelmChangeKind, release.Namespace, release.Name, release.Revision,
            IsHelmUpgrade ? ChartReference.Trim() : null, IsHelmUpgrade ? ChartVersion.Trim() : null,
            IsHelmUpgrade && !string.IsNullOrWhiteSpace(ValuesYaml) ? ValuesYaml : null, revision, AllowHooks, ReuseValues);
        IsBusy = true;
        Issue = null;
        try
        {
            var review = await _session.ReviewHelmChangeAsync(request, _lifetime.Token);
            if (_disposed || generation != _helmEditGeneration || SelectedRelease != release) { return; }
            _helmReview = review;
            HelmChangeStatus = $"{review.Kind}: {ContextName} · {review.Namespace}/{review.Release} · revision {review.ExpectedRevision}.\nChart: {review.PinnedChartReference ?? "unchanged"} · version {review.ChartVersion ?? "unchanged"} · rollback {review.RollbackRevision?.ToString(CultureInfo.InvariantCulture) ?? "none"}\nValues SHA256: {review.ValuesSha256 ?? "none"}\nHooks: {review.AllowHooks} · Reuse values: {review.ReuseValues} · Expires {review.ExpiresAt:HH:mm:ss} UTC. Helm checks the revision before dispatch; concurrent release changes can still race.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    public async Task ConfirmHelmChangeAsync()
    {
        if (!CanConfirmHelm || _session is null || _helmReview is not { } review) { return; }
        ClearHelmReview();
        IsBusy = true;
        try
        {
            var result = await _session.ExecuteHelmChangeAsync(review.ReviewToken, _lifetime.Token);
            HelmChangeStatus = result.Outcome == KubernetesMutationOutcome.OutcomeUnknown
                ? "Helm outcome unknown. Reload release history before any retry." : $"Helm operation: {result.Outcome}. Reload releases to inspect the result.";
            if (result.Outcome == KubernetesMutationOutcome.Applied) { ValuesYaml = string.Empty; }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    private void ClearHelmReview()
    {
        _helmReview = null; _helmEditGeneration++;
        OnPropertyChanged(nameof(CanConfirmHelm)); OnPropertyChanged(nameof(HasHelmChanges));
    }
}
