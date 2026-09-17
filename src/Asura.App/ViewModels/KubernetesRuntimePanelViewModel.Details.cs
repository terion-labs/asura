using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private KubernetesResourceReference? _detailsResource;
    private int _detailsGeneration;
    private KubernetesResourceDetails _details = KubernetesResourceDetails.Empty;
    private IReadOnlyList<KubernetesDetailEvent> _relatedEvents = [];
    private bool _isDetailsLoading;
    private string _eventsStatus = "Select a resource to inspect its events.";

    public KubernetesResourceDetails Details => _details;
    public IReadOnlyList<KubernetesDetailProperty> DetailProperties => Details.Properties;
    public IReadOnlyList<KubernetesDetailProperty> DetailLabels => Details.Labels;
    public IReadOnlyList<KubernetesDetailProperty> DetailAnnotations => Details.Annotations;
    public IReadOnlyList<KubernetesDetailCondition> DetailConditions => Details.Conditions;
    public IReadOnlyList<KubernetesDetailContainer> DetailContainers => Details.Containers;
    public string DetailsStatus => Details.Status;
    public bool HasDetailLabels => DetailLabels.Count > 0;
    public bool HasDetailAnnotations => DetailAnnotations.Count > 0;
    public bool HasDetailConditions => DetailConditions.Count > 0;
    public bool HasDetailContainers => DetailContainers.Count > 0;
    public bool HasRelatedEvents => RelatedEvents.Count > 0;
    public IReadOnlyList<KubernetesDetailEvent> RelatedEvents
    {
        get => _relatedEvents;
        private set { SetProperty(ref _relatedEvents, value); OnPropertyChanged(nameof(HasRelatedEvents)); }
    }
    public bool IsDetailsLoading { get => _isDetailsLoading; private set => SetProperty(ref _isDetailsLoading, value); }
    public string EventsStatus { get => _eventsStatus; private set => SetProperty(ref _eventsStatus, value); }

    private void PublishDetails()
    {
        var resource = _inspection ?? SelectedResource;
        var identity = resource is null ? null : resource.Reference with { ResourceVersion = string.Empty };
        if (_detailsResource != identity)
        {
            _detailsResource = identity;
            _detailsGeneration++;
            RelatedEvents = [];
            IsDetailsLoading = false;
            EventsStatus = resource is null ? "Select a resource to inspect its events." : "Loading related events…";
        }
        var projected = resource is null ? KubernetesResourceDetails.Empty
            : KubernetesResourceDetails.Project(resource, DateTimeOffset.UtcNow, [.. Kinds]);
        _details = projected with
        {
            Properties = [.. projected.Properties.Select(property => property.Target is { } target
                ? property with { NavigateCommand = new AsyncActionCommand(() => NavigateToResourceAsync(target), () => !_disposed && !HasUnsavedChanges && !IsBusy) }
                : property)],
        };
        foreach (var name in new[] { nameof(Details), nameof(DetailProperties), nameof(DetailLabels), nameof(DetailAnnotations),
            nameof(DetailConditions), nameof(DetailContainers), nameof(DetailsStatus), nameof(HasDetailLabels),
            nameof(HasDetailAnnotations), nameof(HasDetailConditions), nameof(HasDetailContainers) })
        { OnPropertyChanged(name); }
    }

    public async Task LoadRelatedEventsAsync(KubernetesResourceDocument resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (_disposed || _session is null || !IsCurrentDetails(resource.Reference)) { return; }
        var generation = _detailsGeneration;
        var scopeGeneration = _generation;
        var uid = resource.Reference.Uid;
        var api = Kinds.FirstOrDefault(item => item.Group.Length == 0 && item.Resource is "events" && item.Version is "v1")
            ?? Kinds.FirstOrDefault(item => item.Group is "events.k8s.io" && item.Resource is "events");
        if (api is null || !api.Verbs.Contains("list", StringComparer.Ordinal))
        { EventsStatus = "Events are not available in API discovery."; return; }
        if (uid.Length is < 1 or > 128 || uid.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        { EventsStatus = "Events require a valid resource identity."; return; }
        IsDetailsLoading = true;
        EventsStatus = "Loading related events…";
        try
        {
            var field = api.Group.Length == 0 ? "involvedObject.uid" : "regarding.uid";
            var page = await _session.ListAsync(new(api, resource.Reference.Namespace, FieldSelector: field + "=" + uid, Limit: 100), cancellationToken);
            if (!IsCurrent()) { return; }
            RelatedEvents = [.. page.Items.Take(100)
                .Select(item => KubernetesResourceDetails.ProjectEvent(item, uid, DateTimeOffset.UtcNow))
                .OfType<KubernetesDetailEvent>().OrderByDescending(item => item.ObservedAt)];
            EventsStatus = page.IsTruncated || page.ContinueToken is not null || page.Items.Count > 100
                ? "Showing the first 100 related events."
                : RelatedEvents.Count == 0 ? "No events reported for this resource." : $"{RelatedEvents.Count} related events";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception)
        {
            if (IsCurrent())
            {
                RelatedEvents = [];
                EventsStatus = exception.Code == KubernetesErrorCode.Forbidden
                    ? "Permission to read events is required for this namespace."
                    : "Related events are unavailable. Refresh the resource to try again.";
            }
        }
        finally { if (IsCurrent()) { IsDetailsLoading = false; } }

        bool IsCurrent() => !_disposed && !cancellationToken.IsCancellationRequested
            && generation == _detailsGeneration && scopeGeneration == _generation && IsCurrentDetails(resource.Reference);
    }

    private bool IsCurrentDetails(KubernetesResourceReference reference) =>
        _detailsResource == reference with { ResourceVersion = string.Empty };
}
