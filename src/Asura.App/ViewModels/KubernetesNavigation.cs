using System.Windows.Input;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed class KubernetesNavigationItem : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;
    public KubernetesNavigationItem(string title, KubernetesApiResource resource, Func<KubernetesNavigationItem, Task> select)
    {
        Title = title;
        ApiResource = resource;
        SelectCommand = new AsyncActionCommand(() => select(this), () => true);
    }
    public string Title { get; }
    public KubernetesApiResource ApiResource { get; }
    public ICommand SelectCommand { get; }
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
    public bool IsVisible { get => _isVisible; internal set => SetProperty(ref _isVisible, value); }
}

public sealed class KubernetesNavigationGroup(string title, IReadOnlyList<KubernetesNavigationItem> items) : ObservableObject
{
    private bool _isExpanded = title is "Cluster" or "Workloads";
    public string Title { get; } = title;
    public IReadOnlyList<KubernetesNavigationItem> Items { get; } = items;
    private bool _isFiltering;
    public bool IsExpanded { get => _isExpanded; set { if (SetProperty(ref _isExpanded, value)) { OnPropertyChanged(nameof(IsOpen)); } } }
    // A filter looks inside every family; folding is remembered for when it clears.
    public bool IsOpen => IsExpanded || _isFiltering;
    public bool HasVisibleItems => Items.Any(item => item.IsVisible);

    internal void ApplyFilter(string filter)
    {
        _isFiltering = filter.Length > 0;
        foreach (var item in Items) { item.IsVisible = !_isFiltering || item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase); }
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(HasVisibleItems));
    }
}

internal static class KubernetesResourceNames
{
    // A CRD may reuse a built-in plural — Rancher serves its own "nodes" — so a
    // built-in name and family only apply inside the API groups Kubernetes ships.
    private static bool IsBuiltIn(KubernetesApiResource resource) => resource.Group is "" or "apps" or "batch" or "autoscaling"
        or "policy" or "networking.k8s.io" or "discovery.k8s.io" or "storage.k8s.io" or "rbac.authorization.k8s.io" or "events.k8s.io";

    private static string KindTitle(KubernetesApiResource resource) => resource.Kind + (resource.Kind.EndsWith('s') ? "" : "s");

    internal static string Title(KubernetesApiResource resource) => !IsBuiltIn(resource) ? KindTitle(resource) : resource.Resource switch
    {
        "nodes" => "Nodes",
        "namespaces" => "Namespaces",
        "events" => "Events",
        "pods" => "Pods",
        "deployments" => "Deployments",
        "daemonsets" => "Daemon Sets",
        "statefulsets" => "Stateful Sets",
        "replicasets" => "Replica Sets",
        "replicationcontrollers" => "Replication Controllers",
        "jobs" => "Jobs",
        "cronjobs" => "Cron Jobs",
        "horizontalpodautoscalers" => "Horizontal Pod Autoscalers",
        "configmaps" => "Config Maps",
        "secrets" => "Secrets",
        "resourcequotas" => "Resource Quotas",
        "limitranges" => "Limit Ranges",
        "poddisruptionbudgets" => "Pod Disruption Budgets",
        "services" => "Services",
        "endpoints" => "Endpoints",
        "endpointslices" => "Endpoint Slices",
        "ingresses" => "Ingresses",
        "ingressclasses" => "Ingress Classes",
        "networkpolicies" => "Network Policies",
        "persistentvolumeclaims" => "Persistent Volume Claims",
        "persistentvolumes" => "Persistent Volumes",
        "storageclasses" => "Storage Classes",
        "csidrivers" => "CSI Drivers",
        "csinodes" => "CSI Nodes",
        "volumeattachments" => "Volume Attachments",
        "roles" => "Roles",
        "rolebindings" => "Role Bindings",
        "clusterroles" => "Cluster Roles",
        "clusterrolebindings" => "Cluster Role Bindings",
        "serviceaccounts" => "Service Accounts",
        _ => KindTitle(resource),
    };

    internal static (string Group, int Order) Category(KubernetesApiResource resource) => !IsBuiltIn(resource) ? ("Custom Resources", 0) : resource.Resource switch
    {
        "nodes" => ("Cluster", 0),
        "namespaces" => ("Cluster", 1),
        "events" => ("Cluster", 2),
        "pods" => ("Workloads", 0),
        "deployments" => ("Workloads", 1),
        "daemonsets" => ("Workloads", 2),
        "statefulsets" => ("Workloads", 3),
        "replicasets" => ("Workloads", 4),
        "replicationcontrollers" => ("Workloads", 5),
        "jobs" => ("Workloads", 6),
        "cronjobs" => ("Workloads", 7),
        "horizontalpodautoscalers" => ("Workloads", 8),
        "configmaps" or "secrets" or "resourcequotas" or "limitranges" or "poddisruptionbudgets" => ("Config", 0),
        "services" or "endpoints" or "endpointslices" or "ingresses" or "ingressclasses" or "networkpolicies" => ("Network", 0),
        "persistentvolumeclaims" or "persistentvolumes" or "storageclasses" or "csidrivers" or "csinodes" or "volumeattachments" => ("Storage", 0),
        "roles" or "rolebindings" or "clusterroles" or "clusterrolebindings" or "serviceaccounts" => ("Access Control", 0),
        _ => ("Custom Resources", 0),
    };
}
