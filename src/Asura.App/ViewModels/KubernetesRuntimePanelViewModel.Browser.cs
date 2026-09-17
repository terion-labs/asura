using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private readonly KubernetesRowCollection _rows = [];
    private IReadOnlyList<KubernetesNavigationGroup> _navigationGroups = [];
    private IReadOnlyList<string> _namespaceChoices = ["All namespaces"];
    private string _labelSelector = string.Empty;
    private bool _publishingRows;
    private bool _namespaceDiscoveryAttempted;
    public IReadOnlyList<KubernetesResourceRow> Rows => _rows;
    public IReadOnlyList<KubernetesNavigationGroup> NavigationGroups { get => _navigationGroups; private set => SetProperty(ref _navigationGroups, value); }
    public IReadOnlyList<string> NamespaceChoices
    {
        get => _namespaceChoices;
        private set
        {
            // Replacing unchanged items during a selection change can make ComboBox
            // restore its previous item before the new scope finishes publishing.
            if (!_namespaceChoices.SequenceEqual(value, StringComparer.Ordinal))
            { SetProperty(ref _namespaceChoices, value); }
        }
    }
    public bool IsPodTable => SelectedKind is { Group: "", Resource: "pods" };
    public bool IsNodeTable => SelectedKind is { Group: "", Resource: "nodes" };
    public bool IsGenericTable => !IsPodTable && !IsNodeTable;
    public bool IsNamespaceScopeVisible => SelectedKind?.Namespaced == true;
    public string ResourceTitle => SelectedKind is { } kind ? KubernetesResourceNames.Title(kind) : "Resources";
    public string ResourceCountLabel => Filter.Length > 0 ? $"{Resources.Count} of {_allResources.Count} items" : $"{Resources.Count} items{(HasMore ? "+" : "")}";
    public string LabelSelector { get => _labelSelector; set { if (SetProperty(ref _labelSelector, value)) { OnPropertyChanged(nameof(Target)); } } }
    public string NamespaceSelection
    {
        get => EffectiveNamespace ?? "All namespaces";
        set
        {
            if (value is null) { return; }
            var next = value is "All namespaces" ? string.Empty : value.Trim();
            if (string.Equals(next, Namespace, StringComparison.Ordinal) || IsBusy) { return; }
            Namespace = next;
            if (string.Equals(Namespace, next, StringComparison.Ordinal)) { SelectionLoading = RefreshAsync(); }
            OnPropertyChanged();
        }
    }
    public KubernetesResourceRow? SelectedRow
    {
        get => Rows.FirstOrDefault(row => string.Equals(row.Document.Reference.Uid, SelectedResource?.Reference.Uid, StringComparison.Ordinal));
        set { if (_publishingRows || _updatingResourceRows) { return; } SelectedResource = value?.Document; }
    }

    private void BuildNavigation()
    {
        string[] order = ["Cluster", "Workloads", "Config", "Network", "Storage", "Access Control", "Custom Resources"];
        bool hasCoreEvents = Kinds.Any(item => item is { Group: "", Resource: "events" });
        var resources = Kinds.Where(item => !hasCoreEvents || item is not { Group: "events.k8s.io", Resource: "events" })
            .GroupBy(item => (item.Group, item.Resource)).Select(group => group.First()).ToArray();
        NavigationGroups = [.. order.Select(category => new KubernetesNavigationGroup(category,
            [.. resources.Where(item => string.Equals(KubernetesResourceNames.Category(item).Group, category, StringComparison.Ordinal))
                .OrderBy(item => KubernetesResourceNames.Category(item).Order).ThenBy(item => KubernetesResourceNames.Title(item), StringComparer.Ordinal)
                .Select(item => new KubernetesNavigationItem(KubernetesResourceNames.Title(item)
                    + (category is "Custom Resources" ? $" · {item.Group}" : ""), item, SelectNavigationAsync))]))
            .Where(group => group.Items.Count > 0)];
        PublishNavigation();
    }

    private void PublishNavigation()
    {
        foreach (var group in NavigationGroups)
        {
            foreach (var item in group.Items)
            {
                item.IsSelected = string.Equals(item.ApiResource.Resource, SelectedKind?.Resource, StringComparison.Ordinal) && string.Equals(item.ApiResource.Group, SelectedKind?.Group, StringComparison.Ordinal);
                if (item.IsSelected) { group.IsExpanded = true; }
            }
        }
        OnPropertyChanged(nameof(ResourceTitle)); OnPropertyChanged(nameof(IsPodTable)); OnPropertyChanged(nameof(IsNodeTable));
        OnPropertyChanged(nameof(IsGenericTable)); OnPropertyChanged(nameof(IsNamespaceScopeVisible));
        OnPropertyChanged(nameof(NamespaceSelection));
    }

    public async Task SelectNavigationAsync(KubernetesNavigationItem item)
    {
        if (IsBusy || _disposed) { return; }
        SelectedKind = item.ApiResource;
        PublishNavigation();
        await SelectionLoading;
    }

    public void PublishResourceRows()
    {
        var usage = Usage.Select(item => item.Entry).ToLookup(item => (item.Namespace, item.Name));
        _publishingRows = true;
        try
        {
            _rows.Replace([.. Resources.Select(document =>
            {
                var row = KubernetesResourceRow.Create(document, usage[(document.Reference.Namespace, document.Reference.Name)], Kinds);
                return row with
                {
                    NamespaceCommand = document.Reference.Namespace is { } ns
                        ? new AsyncActionCommand(() => ScopeToNamespaceAsync(ns), CanNavigateRow) : null,
                    ControlledByCommand = row.OwnerTarget is { } owner
                        ? new AsyncActionCommand(() => NavigateToResourceAsync(owner), CanNavigateRow) : null,
                    NodeCommand = row.NodeTarget is { } node
                        ? new AsyncActionCommand(() => NavigateToResourceAsync(node), CanNavigateRow) : null,
                };
            })]);
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(ResourceCountLabel));
        }
        finally { _publishingRows = false; }
        NamespaceChoices = ["All namespaces", .. NamespaceChoices.Where(value => value is not "All namespaces")
            .Concat(_allResources.Select(item => item.Reference.Namespace).OfType<string>())
            .Append(Namespace).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // Preserve the DataGrid collection view (including user sort) while publishing one
    // reset per snapshot. Per-item notifications make a large watch refresh quadratic.
    private sealed class KubernetesRowCollection : ObservableCollection<KubernetesResourceRow>
    {
        public void Replace(IReadOnlyList<KubernetesResourceRow> rows)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var row in rows) { Items.Add(row); }
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private bool CanNavigateRow() => !_disposed;

    private async Task ScopeToNamespaceAsync(string namespaceName)
    {
        if (HasUnsavedChanges) { Issue = "Apply or discard your manifest edits before changing namespace."; return; }
        if (_disposed || IsBusy) { return; }
        NamespaceSelection = namespaceName;
        await SelectionLoading;
    }

    private async Task LoadNamespaceChoicesAsync()
    {
        if (_namespaceDiscoveryAttempted) { return; }
        var kind = Kinds.FirstOrDefault(item => item is { Group: "", Resource: "namespaces" });
        if (_session is null || kind is null) { return; }
        _namespaceDiscoveryAttempted = true;
        try
        {
            var page = await _session.ListAsync(new(kind, Limit: 1000), _lifetime.Token);
            if (!_disposed)
            {
                NamespaceChoices = ["All namespaces", .. page.Items.Select(item => item.Reference.Name)
                    .Concat(NamespaceChoices.Where(value => value is not "All namespaces")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            }
        }
        catch (KubernetesRequestException) { /* Keep configured and observed namespaces when namespace-list access is unavailable. */ }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    public async Task NavigateToResourceAsync(KubernetesResourceReference reference)
    {
        if (HasUnsavedChanges || IsBusy || _disposed || _session is null)
        { if (HasUnsavedChanges) { Issue = "Apply or discard your manifest edits before navigating."; } return; }
        var kind = Kinds.FirstOrDefault(item => string.Equals(item.Group, reference.Group, StringComparison.Ordinal) && string.Equals(item.Resource, reference.Resource, StringComparison.Ordinal) && string.Equals(item.Version, reference.Version, StringComparison.Ordinal))
            ?? Kinds.FirstOrDefault(item => string.Equals(item.Group, reference.Group, StringComparison.Ordinal) && string.Equals(item.Resource, reference.Resource, StringComparison.Ordinal));
        if (kind is null) { Issue = "This resource API is not available in the current cluster."; return; }
        ClearScope();
        _selectedKind = kind;
        _namespace = reference.Namespace ?? string.Empty;
        _filter = string.Empty;
        _labelSelector = string.Empty;
        OnPropertyChanged(nameof(LabelSelector));
        OnPropertyChanged(nameof(SelectedKind)); OnPropertyChanged(nameof(Namespace)); OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(Target)); PublishNavigation();
        var generation = _generation + 1;
        await RefreshAsync();
        if (_disposed || generation != _generation) { return; }
        var document = Resources.FirstOrDefault(item => string.Equals(item.Reference.Name, reference.Name, StringComparison.Ordinal) && string.Equals(item.Reference.Namespace, reference.Namespace, StringComparison.Ordinal));
        if (document is null)
        {
            try { document = await _session.InspectAsync(reference with { Version = kind.Version }, _lifetime.Token); }
            catch (KubernetesRequestException exception) { if (!_disposed && generation == _generation) { PresentError(exception); } return; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            if (_disposed || generation != _generation) { return; }
            _allResources = [.. _allResources.Append(document)];
            ApplyFilter();
        }
        if (reference.Uid.Length > 0 && !string.Equals(document.Reference.Uid, reference.Uid, StringComparison.Ordinal))
        { Issue = "The related resource was replaced. Refresh before opening it."; return; }
        SelectedResource = document;
        await SelectionLoading;
    }
}
