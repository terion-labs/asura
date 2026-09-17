using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>A detached, sortable table row. Missing measurements remain unavailable.</summary>
public sealed record KubernetesResourceRow(KubernetesResourceDocument Document)
{
    public string Name => Document.Reference.Name;
    public string Namespace => Document.Reference.Namespace ?? "—";
    public string Cpu { get; init; } = "N/A";
    public string Memory { get; init; } = "N/A";
    public decimal? CpuValue { get; init; }
    public decimal? MemoryValue { get; init; }
    public string Ready { get; init; } = "—";
    public long RestartCount { get; init; }
    public string Restarts => RestartCount.ToString(CultureInfo.InvariantCulture);
    public string Owner { get; init; } = "—";
    public string Node { get; init; } = "—";
    public KubernetesResourceReference? OwnerTarget { get; init; }
    public KubernetesResourceReference? NodeTarget { get; init; }
    public ICommand? NamespaceCommand { get; init; }
    public ICommand? ControlledByCommand { get; init; }
    public ICommand? NodeCommand { get; init; }
    public bool CanNavigateNamespace => NamespaceCommand is not null;
    public bool CanNavigateOwner => ControlledByCommand is not null;
    public bool CanNavigateNode => NodeCommand is not null;
    public string Qos { get; init; } = "—";
    public string Status { get; init; } = "Unknown";
    public string Age { get; init; } = "—";
    public double AgeSeconds { get; init; }
    public string Roles { get; init; } = "—";
    public string Version { get; init; } = "—";
    public string Taints { get; init; } = "—";
    public string Disk => "N/A";
    public string Type { get; init; } = "—";
    public string Detail => Document.Summary;
    public string StatusTone { get; init; } = "Muted";
    public bool IsHealthy => StatusTone is "Success";
    public bool IsWarning => StatusTone is "Warning";
    public bool IsError => StatusTone is "Danger";

    public static KubernetesResourceRow Create(KubernetesResourceDocument document, IEnumerable<KubernetesUsageEntry> usage, IReadOnlyList<KubernetesApiResource>? discovered = null)
    {
        var measurements = usage.Where(item => string.Equals(item.Name, document.Reference.Name, StringComparison.Ordinal) && string.Equals(item.Namespace, document.Reference.Namespace, StringComparison.Ordinal)).ToArray();
        var cpu = SumKnown(measurements.Select(item => item.CpuCores));
        var memory = SumKnown(measurements.Select(item => item.MemoryBytes));
        var row = new KubernetesResourceRow(document)
        {
            CpuValue = cpu,
            MemoryValue = memory,
            Cpu = cpu?.ToString("0.###", CultureInfo.InvariantCulture) ?? "N/A",
            Memory = memory is { } bytes ? FormatBytes(bytes) : "N/A",
            Type = document.Kind,
        };
        try
        {
            using var json = JsonDocument.Parse(document.Json);
            var root = json.RootElement;
            var meta = Property(root, "metadata");
            var spec = Property(root, "spec");
            var state = Property(root, "status");
            var age = AgeOf(Text(meta, "creationTimestamp"));
            var owners = Elements(Property(meta, "ownerReferences"));
            var owner = owners.FirstOrDefault(item => Boolean(item, "controller"));
            if (owner.ValueKind == JsonValueKind.Undefined) { owner = owners.FirstOrDefault(); }
            row = row with
            {
                AgeSeconds = age.TotalSeconds,
                Age = age.Display,
                Owner = Text(owner, "kind", "—"),
                OwnerTarget = OwnerReference(owner, document.Reference.Namespace, discovered),
            };
            if (document.Reference.Resource is "pods" && document.Reference.Group.Length == 0)
            {
                var declaredContainers = Elements(Property(spec, "containers"));
                if (declaredContainers.Any(container => !measurements.Any(measurement =>
                    string.Equals(measurement.Container, Text(container, "name"), StringComparison.Ordinal))))
                {
                    row = row with { CpuValue = null, MemoryValue = null, Cpu = "N/A", Memory = "N/A" };
                }
                var statuses = Elements(Property(state, "containerStatuses"));
                var initStatuses = Elements(Property(state, "initContainerStatuses"));
                var containerCount = Elements(Property(spec, "containers")).Length;
                var phase = Text(state, "phase", "Unknown");
                var waiting = statuses.Concat(initStatuses).Select(item => Text(Property(Property(item, "state"), "waiting"), "reason"))
                    .FirstOrDefault(reason => !string.IsNullOrEmpty(reason));
                var terminated = statuses.Concat(initStatuses).Select(item => Property(Property(item, "state"), "terminated"))
                    .FirstOrDefault(item => Number(item, "exitCode") != 0);
                var failed = terminated.ValueKind == JsonValueKind.Undefined ? null : Text(terminated, "reason", "Error");
                var readyCount = statuses.Count(item => Boolean(item, "ready"));
                var status = Text(meta, "deletionTimestamp").Length > 0 ? "Terminating" : waiting ?? failed
                    ?? (phase is "Running" && readyCount < containerCount ? "Running / Not ready" : phase);
                return row with
                {
                    Ready = $"{readyCount}/{containerCount}",
                    RestartCount = statuses.Concat(initStatuses).Sum(item => Number(item, "restartCount")),
                    Node = Text(spec, "nodeName", "—"),
                    NodeTarget = Text(spec, "nodeName") is { Length: > 0 } node
                        ? new("", "v1", "nodes", null, node, "", "") : null,
                    Qos = Text(state, "qosClass", "—"),
                    Status = status,
                    StatusTone = status is "Running" or "Succeeded" ? "Success" : status is "Pending" or "ContainerCreating" or "PodInitializing" or "Terminating" or "Running / Not ready" ? "Warning" : "Danger",
                };
            }
            if (document.Reference.Resource is "nodes" && document.Reference.Group.Length == 0)
            {
                var ready = Elements(Property(state, "conditions")).FirstOrDefault(item => Text(item, "type") is "Ready");
                var readiness = Text(ready, "status");
                var healthy = readiness is "True";
                var unknown = readiness is not "True" and not "False";
                var labels = Property(meta, "labels");
                var roles = labels.ValueKind == JsonValueKind.Object
                    ? labels.EnumerateObject().Where(item => item.Name.StartsWith("node-role.kubernetes.io/", StringComparison.Ordinal))
                        .Select(item => item.Name["node-role.kubernetes.io/".Length..]).Where(value => value.Length > 0).ToArray() : [];
                var cordoned = Boolean(spec, "unschedulable");
                return row with
                {
                    Status = unknown ? "Unknown" : healthy ? cordoned ? "Ready / Cordoned" : "Ready" : "Not ready",
                    StatusTone = unknown ? "Warning" : healthy ? cordoned ? "Warning" : "Success" : "Danger",
                    Roles = roles.Length == 0 ? "node" : string.Join(", ", roles),
                    Version = Text(Property(state, "nodeInfo"), "kubeletVersion", "—"),
                    Taints = Elements(Property(spec, "taints")).Length.ToString(CultureInfo.InvariantCulture),
                    Ready = unknown ? "Unknown" : healthy ? "Ready" : "Not ready",
                };
            }
            var conditions = Elements(Property(state, "conditions"));
            var available = conditions.FirstOrDefault(item => Text(item, "type") is "Available" or "Ready" or "Complete");
            var resourceStatus = Text(state, "phase");
            if (resourceStatus.Length == 0 && available.ValueKind != JsonValueKind.Undefined)
            { resourceStatus = Text(available, "status") is "True" ? Text(available, "type") : "Not ready"; }
            if (resourceStatus.Length == 0) { resourceStatus = document.Reference.Resource is "services" ? Text(spec, "type", "ClusterIP") : "—"; }
            var replicas = Number(spec, "replicas");
            return row with
            {
                Ready = replicas > 0 ? $"{Number(state, "readyReplicas")}/{replicas}" : "—",
                Status = resourceStatus,
                StatusTone = resourceStatus is "Active" or "Bound" or "Available" or "Ready" or "Complete" ? "Success" : resourceStatus is "Failed" or "Not ready" ? "Danger" : "Muted",
            };
        }
        catch (JsonException) { return row; }
    }

    private static KubernetesResourceReference? OwnerReference(JsonElement owner, string? namespaceName,
        IReadOnlyList<KubernetesApiResource>? discovered)
    {
        var name = Text(owner, "name");
        var kind = Text(owner, "kind");
        var apiVersion = Text(owner, "apiVersion");
        if (name.Length == 0 || kind.Length == 0 || apiVersion.Length == 0 || discovered is null) { return null; }
        int slash = apiVersion.IndexOf('/', StringComparison.Ordinal);
        string group = slash < 0 ? "" : apiVersion[..slash];
        string version = slash < 0 ? apiVersion : apiVersion[(slash + 1)..];
        var api = discovered.FirstOrDefault(item => string.Equals(item.Group, group, StringComparison.Ordinal)
            && string.Equals(item.Version, version, StringComparison.Ordinal) && string.Equals(item.Kind, kind, StringComparison.Ordinal))
            ?? discovered.FirstOrDefault(item => string.Equals(item.Group, group, StringComparison.Ordinal)
                && string.Equals(item.Kind, kind, StringComparison.Ordinal));
        return api is null ? null : new(api.Group, api.Version, api.Resource, api.Namespaced ? namespaceName : null,
            name, Text(owner, "uid"), "");
    }

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var array = values.ToArray();
        try { return array.Length == 0 || array.Any(item => item is null) ? null : array.Sum(); }
        catch (OverflowException) { return null; }
    }
    private static string FormatBytes(decimal bytes) => bytes >= 1024 * 1024 * 1024
        ? (bytes / (1024 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GiB"
        : (bytes / (1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MiB";
    private static (double TotalSeconds, string Display) AgeOf(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)) { return (0, "—"); }
        var seconds = Math.Max(0, (DateTimeOffset.UtcNow - timestamp).TotalSeconds);
        return (seconds, seconds >= 86400 ? $"{(long)(seconds / 86400)}d" : seconds >= 3600 ? $"{(long)(seconds / 3600)}h" : seconds >= 60 ? $"{(long)(seconds / 60)}m" : $"{(long)seconds}s");
    }
    private static JsonElement Property(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static string Text(JsonElement element, string name, string fallback = "") => Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? fallback : fallback;
    private static long Number(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : 0;
    private static bool Boolean(JsonElement element, string name) => Property(element, name).ValueKind == JsonValueKind.True;
    private static JsonElement[] Elements(JsonElement element) => element.ValueKind == JsonValueKind.Array ? [.. element.EnumerateArray()] : [];
}
