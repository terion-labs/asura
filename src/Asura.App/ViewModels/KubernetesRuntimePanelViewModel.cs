using System.Collections.ObjectModel;
using System.Windows.Input;
using Asura.Application;
using Asura.Core;
using FluentIcons.Common;

namespace Asura.App.ViewModels;

/// <summary>
/// One context-bound resource browser. Operations keep their original scope and
/// selection generation so late responses cannot populate a replacement view.
/// </summary>
public sealed partial class KubernetesRuntimePanelViewModel : RuntimePanelViewModel
{
    private readonly Func<CancellationToken, ValueTask<IKubernetesClientSession>>? _connect;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AsyncActionCommand _refreshCommand;
    private readonly AsyncActionCommand _logsCommand;
    private readonly AsyncActionCommand _moreCommand;
    private string? _continuation;
    private KubernetesListRequest? _loadedRequest;
    private IKubernetesClientSession? _session;
    private CancellationTokenSource? _selectionCancellation;
    private KubernetesApiResource? _selectedKind;
    private KubernetesResourceDocument? _selectedResource;
    private KubernetesResourceDocument? _inspection;
    private IReadOnlyList<KubernetesResourceDocument> _allResources = [];
    private IReadOnlyList<KubernetesResourceDocument> _resources = [];
    private string _namespace;
    private string _filter = string.Empty;
    private string _container = string.Empty;
    private string _logs = string.Empty;
    private string _status = "Select a saved Kubernetes connection.";
    private string? _issue;
    private bool _busy;
    private bool _disposed;
    private bool _updatingResourceRows;
    private int _generation;
    private bool _previousLogs;

    public KubernetesRuntimePanelViewModel(
        PanelInstanceId id,
        string title,
        KubernetesConnectionProfile? profile,
        Func<CancellationToken, ValueTask<IKubernetesClientSession>>? connect,
        KubernetesPanelTarget? target = null,
        long bindingRevision = 0)
        : base(id, PanelKind.Kubernetes, title, "Kubernetes")
    {
        Profile = profile;
        BindingRevision = bindingRevision;
        _connect = connect;
        InitialTarget = target;
        _namespace = target is not null ? target.NamespaceName ?? string.Empty : profile?.DefaultNamespace ?? "default";
        // The view can attach while the first API requests are still pending.
        // Its configured scope must already be selectable before discovery adds choices.
        _namespaceChoices = string.IsNullOrWhiteSpace(_namespace) ? ["All namespaces"] : ["All namespaces", _namespace.Trim()];
        _refreshCommand = new(RefreshAsync, () => !_disposed && !IsBusy && _connect is not null && !HasUnsavedChanges);
        _logsCommand = new(LoadLogsAsync, () => !_disposed && !IsBusy && CanReadLogs);
        _moreCommand = new(LoadMoreAsync, () => !_disposed && !IsBusy && HasMore);
        _dryRunCommand = new(DryRunAsync, () => !IsBusy && CanEditManifest && HasUnsavedChanges);
        _applyCommand = new(ApplyManifestAsync, () => CanApplyManifest);
        _discardCommand = new(() => { DiscardManifestChanges(); return Task.CompletedTask; }, () => HasUnsavedChanges && !IsBusy);
        Initialization = connect is null ? Task.CompletedTask : RefreshAsync();
    }

    public KubernetesConnectionProfile? Profile { get; }
    public long BindingRevision { get; }
    private KubernetesPanelTarget? InitialTarget { get; }
    public Task Initialization { get; }
    public Task SelectionLoading { get; private set; } = Task.CompletedTask;
    public ObservableCollection<KubernetesApiResource> Kinds { get; } = [];
    public ICommand RefreshCommand => _refreshCommand;
    public ICommand LoadLogsCommand => _logsCommand;
    public ICommand LoadMoreCommand => _moreCommand;
    public bool HasMore => _continuation is not null && _allResources.Count < 10000;
    public string ConnectionDisplayName => Profile?.Name ?? "Select connection";
    public string ContextName => Profile?.ContextName ?? "No context selected";
    public bool HasConnection => Profile is not null;
    public bool HasIssue => Issue is not null;

    /// <summary>
    /// The cluster's state for the header's status mark. It was green for any
    /// saved connection, reachable or not.
    /// </summary>
    public StatusTone ConnectionStatusTone => Profile is null ? StatusTone.Neutral
        : _session is null ? HasIssue ? StatusTone.Danger : IsBusy ? StatusTone.Warning : StatusTone.Neutral
        : HasIssue ? StatusTone.Warning
        : StatusTone.Success;

    public string ConnectionStatusLabel => ConnectionStatusTone switch
    {
        StatusTone.Success => $"Connected to {ContextName}",
        StatusTone.Danger => $"Can't reach “{ContextName}”",
        StatusTone.Warning when _session is null => $"Connecting to {ContextName}",
        StatusTone.Warning => $"Connected to {ContextName} with a problem",
        _ => Profile is null ? "No Kubernetes connection" : $"Not connected to {ContextName}",
    };

    public Symbol ConnectionStatusGlyph => StatusToneGlyph.For(ConnectionStatusTone);
    public bool HasSelection => SelectedResource is not null;
    public bool IsEmpty => HasConnection && !IsBusy && Resources.Count == 0 && !HasIssue;
    public bool CanReadLogs => SelectedResource is { Reference.Group: "", Reference.Resource: "pods" };
    public string Manifest => _inspection?.Json ?? SelectedResource?.Json ?? string.Empty;
    public string Summary => _inspection?.Summary ?? SelectedResource?.Summary ?? "Select a resource to inspect it.";
    public string SelectionTitle => SelectedResource?.Reference.Name ?? "Resource details";
    public KubernetesPanelTarget? Target => Profile is null ? null : new(
        Profile.Id, EffectiveNamespace, SelectedKind?.Resource ?? "pods", SelectedKind?.Group ?? "", SelectedKind?.Version ?? "v1");
    private string? EffectiveNamespace => string.IsNullOrWhiteSpace(Namespace) ? null : Namespace.Trim();

    public KubernetesApiResource? SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (HasUnsavedChanges) { Issue = "Discard or apply manifest edits before changing resource type."; OnPropertyChanged(); return; }
            if (SetProperty(ref _selectedKind, value))
            {
                OnPropertyChanged(nameof(Target));
                PublishNavigation();
                if (!IsBusy) { ClearScope(); SelectionLoading = RefreshAsync(); }
            }
        }
    }

    public KubernetesResourceDocument? SelectedResource
    {
        get => _selectedResource;
        set
        {
            if (_updatingResourceRows && value is null || value == _selectedResource) { return; }
            if (HasUnsavedChanges) { Issue = "Discard or apply manifest edits before changing resource."; OnPropertyChanged(); return; }
            if (!SetProperty(ref _selectedResource, value)) { return; }
            _selectionCancellation?.Cancel();
            DiscardManifestChanges();
            StopFollowingLogs();
            _inspection = null;
            Logs = string.Empty;
            MetricHistory = [];
            ServiceForwardChoices = []; SelectedServiceForward = null;
            PublishSelection();
            SelectionLoading = InspectAsync(value);
        }
    }

    public IReadOnlyList<KubernetesResourceDocument> Resources
    {
        get => _resources;
        private set { SetProperty(ref _resources, value); OnPropertyChanged(nameof(IsEmpty)); PublishResourceRows(); }
    }

    /// <summary>Empty means all namespaces. Manual entry works without namespace-list permission.</summary>
    public string Namespace { get => _namespace; set { if (HasUnsavedChanges) { Issue = "Discard or apply manifest edits before changing namespace."; OnPropertyChanged(); return; } if (SetProperty(ref _namespace, value)) { ClearScope(); OnPropertyChanged(nameof(Target)); OnPropertyChanged(nameof(NamespaceSelection)); } } }
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) { ApplyFilter(); } } }
    public string Container { get => _container; set { if (SetProperty(ref _container, value)) { OnPropertyChanged(nameof(CanLaunchShell)); } } }
    public bool PreviousLogs { get => _previousLogs; set => SetProperty(ref _previousLogs, value); }
    public string Logs { get => _logs; private set => SetProperty(ref _logs, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string? Issue
    {
        get => _issue;
        private set { ReportError(value, "Kubernetes error"); SetProperty(ref _issue, value); OnPropertyChanged(nameof(HasIssue)); OnPropertyChanged(nameof(IsEmpty)); PublishConnectionStatus(); }
    }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            SetProperty(ref _busy, value);
            _refreshCommand.RaiseCanExecuteChanged();
            _logsCommand.RaiseCanExecuteChanged();
            _moreCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(IsEmpty));
            PublishMutationState();
            PublishConnectionStatus();
        }
    }

    private void PublishConnectionStatus()
    {
        OnPropertyChanged(nameof(ConnectionStatusTone));
        OnPropertyChanged(nameof(ConnectionStatusLabel));
        OnPropertyChanged(nameof(ConnectionStatusGlyph));
    }

    public async Task RefreshAsync()
    {
        if (_disposed || IsBusy || _connect is null || HasUnsavedChanges) { return; }
        StopWatching();
        _loadedRequest = null;
        _continuation = null;
        OnPropertyChanged(nameof(HasMore));
        IsBusy = true;
        Issue = null;
        var generation = ++_generation;
        try
        {
            Status = "Connecting…";
            if (_session is null)
            {
                var session = await _connect(_lifetime.Token);
                if (_disposed) { await session.DisposeAsync(); return; }
                _session = session;
                PublishConnectionStatus();
                OnPropertyChanged(nameof(HasMetrics)); OnPropertyChanged(nameof(HasHelm));
                OnPropertyChanged(nameof(IsHelmNavigationVisible)); OnPropertyChanged(nameof(HasNavigationMatches));
            }
            if (Kinds.Count == 0)
            {
                var discovery = await _session.DiscoverAsync(_lifetime.Token);
                foreach (var kind in discovery.Resources.Where(item => item.Verbs.Contains("list", StringComparer.Ordinal))
                    .OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Group, StringComparer.Ordinal))
                {
                    Kinds.Add(kind);
                }
                BuildNavigation();
                SelectedKind = Kinds.FirstOrDefault(item => string.Equals(item.Resource, InitialTarget?.Resource ?? "pods", StringComparison.Ordinal)
                    && string.Equals(item.Group, InitialTarget?.ApiGroup ?? "", StringComparison.Ordinal) && string.Equals(item.Version, InitialTarget?.ApiVersion ?? "v1", StringComparison.Ordinal))
                    ?? Kinds.FirstOrDefault();
                if (discovery.UnavailableGroups.Count > 0)
                {
                    Issue = "Asura couldn't list some kinds of resources. The ones it could list are shown.";
                }
            }
            if (SelectedKind is not { } kindToLoad) { Status = "No listable resource types were discovered."; return; }
            var scope = kindToLoad.Namespaced ? EffectiveNamespace : null;
            Status = $"Loading {kindToLoad.Kind}…";
            var request = new KubernetesListRequest(kindToLoad, scope, LabelSelector: string.IsNullOrWhiteSpace(LabelSelector) ? null : LabelSelector.Trim());
            var page = await _session.ListAsync(request, _lifetime.Token);
            if (_disposed || generation != _generation) { return; }
            var selectedUid = SelectedResource?.Reference.Uid;
            _resourceVersion = page.ResourceVersion;
            _loadedRequest = request;
            _continuation = page.ContinueToken;
            _allResources = page.Items;
            OnPropertyChanged(nameof(HasMore));
            ApplyFilter();
            SelectedResource = Resources.FirstOrDefault(item => string.Equals(item.Reference.Uid, selectedUid, StringComparison.Ordinal));
            Status = TableStatus(page.IsTruncated || page.ContinueToken is not null ? "Results limited" : null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                if (generation == _generation) { StartWatching(); await RefreshResourceUsageAsync(); }
            }
        }
        if (!_disposed) { await LoadNamespaceChoicesAsync(); }
    }

    private async Task InspectAsync(KubernetesResourceDocument? resource)
    {
        if (resource is null || _session is null || _disposed) { return; }
        _selectionCancellation?.Dispose();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _selectionCancellation = cancellation;
        try
        {
            var inspection = await _session.InspectAsync(resource.Reference, cancellation.Token);
            if (!_disposed && !cancellation.IsCancellationRequested && SameResourceIdentity(SelectedResource?.Reference, resource.Reference))
            {
                // A watch event may have delivered a newer version while this read was pending.
                if (SelectedResource?.Reference == resource.Reference) { _inspection = inspection; }
                PublishSelection();
                await LoadRelatedEventsAsync(_inspection ?? inspection, cancellation.Token);
                if (!cancellation.IsCancellationRequested) { await LoadSelectedHistoryAsync(); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (KubernetesRequestException exception)
        {
            if (!cancellation.IsCancellationRequested) { PresentError(exception); }
        }
        finally { if (ReferenceEquals(_selectionCancellation, cancellation)) { _selectionCancellation = null; } }
    }

    public async Task LoadLogsAsync()
    {
        if (_session is null || SelectedResource is not { } resource || !CanReadLogs || IsBusy || _disposed) { return; }
        StopFollowingLogs();
        IsBusy = true;
        Issue = null;
        try
        {
            var page = await _session.ReadLogsAsync(new(resource.Reference,
                string.IsNullOrWhiteSpace(Container) ? null : Container.Trim(), Previous: PreviousLogs), _lifetime.Token);
            if (!_disposed && SelectedResource?.Reference == resource.Reference)
            {
                Logs = page.Text;
                Status = page.IsTruncated ? "Showing the most recent log lines · Older lines omitted" : "Showing the most recent log lines";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }

    private void ClearScope()
    {
        StopWatching();
        StopFollowingLogs();
        ClearObservability();
        DiscardManifestChanges();
        _generation++;
        _allResources = [];
        _continuation = null;
        _loadedRequest = null;
        SelectedResource = null;
        ApplyFilter();
        OnPropertyChanged(nameof(HasMore));
    }

    private void ApplyFilter()
    {
        _updatingResourceRows = true;
        try
        {
            Resources = string.IsNullOrWhiteSpace(Filter) ? _allResources
                : [.. _allResources.Where(item => item.Reference.Name.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)
                    || item.Summary.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)
                    || (item.Reference.Namespace?.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)
                    || item.Json.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase))];
        }
        finally { _updatingResourceRows = false; }
    }

    private void PublishSelection()
    {
        PublishLayout();
        PublishDetails();
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(SelectedCpuUsage)); OnPropertyChanged(nameof(SelectedMemoryUsage)); OnPropertyChanged(nameof(HasResourceMetrics));
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(HasInspectorContent)); OnPropertyChanged(nameof(CanReadLogs)); OnPropertyChanged(nameof(HasMetricHistory));
        OnPropertyChanged(nameof(Manifest)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(SelectionTitle));
        _logsCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanFollowLogs));
        OnPropertyChanged(nameof(CanOpenShell)); OnPropertyChanged(nameof(ContainerChoices)); OnPropertyChanged(nameof(CanLaunchShell));
        OnPropertyChanged(nameof(HasInspectorActions));
        OnPropertyChanged(nameof(IsServiceForward)); OnPropertyChanged(nameof(IsPodForward));
        OnPropertyChanged(nameof(CanForward)); OnPropertyChanged(nameof(HasForwardTools)); OnPropertyChanged(nameof(DeclaredPorts));
        PublishMutationState();
    }

    private void PresentError(KubernetesRequestException exception)
    {
        Status = ErrorStatus(exception.Code);
        Issue = exception.Code == KubernetesErrorCode.Forbidden
            ? "Access denied for this scope. Enter an allowed namespace and refresh, or review your Kubernetes permissions."
            : exception.Message;
    }

    /// <summary>
    /// What the status bar says about the table: which resources, where, and
    /// how current they are. The count lives once, beside the filter.
    /// </summary>
    private string TableStatus(string? note)
    {
        var scope = SelectedKind?.Namespaced == true
            ? EffectiveNamespace is { Length: > 0 } ns ? $"in {ns}" : "in all namespaces"
            : "across the cluster";
        var table = $"{ResourceTitle} {scope}";
        return note is null ? table : $"{table} · {note}";
    }

    /// <summary>A request failure in the status bar, in words rather than an error code.</summary>
    internal static string ErrorStatus(KubernetesErrorCode code) => code switch
    {
        KubernetesErrorCode.InvalidConfiguration => "Connection settings need attention",
        KubernetesErrorCode.Unauthorized => "Sign-in required",
        KubernetesErrorCode.Forbidden => "Access denied",
        KubernetesErrorCode.NotFound => "Not found",
        KubernetesErrorCode.Conflict => "Changed on the server",
        KubernetesErrorCode.ResourceExpired => "Out of date",
        KubernetesErrorCode.TooManyRequests => "The cluster is busy",
        KubernetesErrorCode.ServerUnavailable => "Cluster unavailable",
        KubernetesErrorCode.InvalidResponse => "Unexpected response from the cluster",
        KubernetesErrorCode.ResponseTooLarge => "Response too large",
        KubernetesErrorCode.TargetChanged => "The cluster changed",
        KubernetesErrorCode.ConnectionFailed => "Can’t connect to the cluster",
        KubernetesErrorCode.Unsupported => "Not supported by this cluster",
        _ => "Request failed",
    };

    public override void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        StopWatching();
        StopFollowingLogs();
        if (ForwardState is { } forwards) { forwards.Forwards.CollectionChanged -= OnForwardsChanged; }
        _hostedSession?.Dispose();
        _lifetime.Cancel();
        _selectionCancellation?.Cancel();
        if (_session is { } session) { _ = session.DisposeAsync().AsTask(); }
        _lifetime.Dispose();
        base.Dispose();
    }
}
