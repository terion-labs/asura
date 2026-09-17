using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed record KubernetesDetailProperty(string Label, string Value,
    KubernetesResourceReference? Target = null, ICommand? NavigateCommand = null)
{
    public bool CanNavigate => NavigateCommand is not null;
}

public sealed record KubernetesDetailCondition(string Type, string Status, string Reason, string Message, string ChangedAt);
public sealed record KubernetesDetailContainer(string Name, string Image, string Ready, string Restarts, string State, string Ports);
public sealed record KubernetesDetailEvent(string Type, string Reason, string Message, string Count, string LastSeen, DateTimeOffset? ObservedAt = null);

/// <summary>Detached, bounded inspector fields. Resource bodies never become arbitrary property rows.</summary>
public sealed record KubernetesResourceDetails(
    IReadOnlyList<KubernetesDetailProperty> Properties,
    IReadOnlyList<KubernetesDetailProperty> Labels,
    IReadOnlyList<KubernetesDetailProperty> Annotations,
    IReadOnlyList<KubernetesDetailCondition> Conditions,
    IReadOnlyList<KubernetesDetailContainer> Containers,
    string Status)
{
    public static KubernetesResourceDetails Empty { get; } = new([], [], [], [], [], "Select a resource to inspect its properties.");

    public static KubernetesResourceDetails Project(KubernetesResourceDocument resource, DateTimeOffset now,
        IReadOnlyList<KubernetesApiResource> discovered)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(discovered);
        List<KubernetesDetailProperty> properties = [new("Name", resource.Reference.Name), new("Kind", resource.Kind)];
        if (resource.Reference.Namespace is { Length: > 0 } ns)
        { properties.Add(new("Namespace", ns, new("", "v1", "namespaces", null, ns, "", ""))); }
        if (resource.Json.Length > 2 * 1024 * 1024)
        { return new(properties, [], [], [], [], "Resource is too large for the properties view."); }
        try
        {
            using var document = JsonDocument.Parse(resource.Json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { throw new JsonException(); }
            var metadata = At(root, "metadata");
            Add(properties, "Created", Timestamp(At(metadata, "creationTimestamp"), now));
            Add(properties, "Status", Text(At(root, "status", "phase")) is { Length: > 0 } phase ? phase : resource.Summary);
            Add(properties, "UID", resource.Reference.Uid);
            Add(properties, "Resource version", resource.Reference.ResourceVersion);
            foreach (var owner in Items(At(metadata, "ownerReferences")))
            {
                var ownerKind = Text(At(owner, "kind"));
                var ownerName = Text(At(owner, "name"));
                var version = Text(At(owner, "apiVersion"));
                if (ownerName.Length == 0 || ownerKind.Length == 0) { continue; }
                var group = version.Contains('/', StringComparison.Ordinal) ? version[..version.IndexOf('/', StringComparison.Ordinal)] : string.Empty;
                var api = discovered.FirstOrDefault(item => string.Equals(item.Kind, ownerKind, StringComparison.Ordinal)
                    && string.Equals(item.Group.Length == 0 ? item.Version : item.Group + "/" + item.Version, version, StringComparison.Ordinal))
                    ?? discovered.FirstOrDefault(item => string.Equals(item.Kind, ownerKind, StringComparison.Ordinal) && string.Equals(item.Group, group, StringComparison.Ordinal));
                var target = api is null ? null : new KubernetesResourceReference(api.Group, api.Version, api.Resource,
                    api.Namespaced ? resource.Reference.Namespace : null, ownerName, Text(At(owner, "uid")), "");
                properties.Add(new(Boolean(At(owner, "controller")) == true ? "Controlled by" : "Owner", $"{ownerKind} {ownerName}", target));
            }
            var spec = At(root, "spec");
            var status = At(root, "status");
            switch (resource.Kind)
            {
                case "Pod": Pod(properties, spec, status, resource.Reference.Namespace); break;
                case "Node": Node(properties, spec, status); break;
                case "Deployment":
                case "StatefulSet":
                case "ReplicaSet":
                case "DaemonSet": Workload(properties, spec, status); break;
                case "Service": Service(properties, spec, status); break;
            }
            var conditions = Items(At(status, "conditions")).Select(condition => new KubernetesDetailCondition(
                Text(At(condition, "type")), Text(At(condition, "status")), Text(At(condition, "reason")),
                Text(At(condition, "message")), Timestamp(At(condition, "lastTransitionTime"), now))).ToArray();
            var secret = resource.Reference.Group.Length == 0 && resource.Reference.Resource is "secrets";
            return new(properties, Pairs(At(metadata, "labels")), secret ? [] : Pairs(At(metadata, "annotations"), annotations: true),
                conditions, secret ? [] : ProjectContainers(spec, status), secret ? "Secret values are hidden." : string.Empty);
        }
        catch (JsonException)
        { return new(properties, [], [], [], [], "Structured properties are unavailable for this resource."); }
    }

    public static KubernetesDetailEvent? ProjectEvent(KubernetesResourceDocument resource, string expectedUid, DateTimeOffset now)
    {
        if (resource.Json.Length > 2 * 1024 * 1024) { return null; }
        try
        {
            using var document = JsonDocument.Parse(resource.Json, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (!string.Equals(Text(At(root, "involvedObject", "uid")), expectedUid, StringComparison.Ordinal) && !string.Equals(Text(At(root, "regarding", "uid")), expectedUid, StringComparison.Ordinal))
            { return null; }
            var last = At(root, "series", "lastObservedTime");
            if (last.ValueKind != JsonValueKind.String) { last = At(root, "lastTimestamp"); }
            if (last.ValueKind != JsonValueKind.String) { last = At(root, "eventTime"); }
            if (last.ValueKind != JsonValueKind.String) { last = At(root, "metadata", "creationTimestamp"); }
            var count = Text(At(root, "series", "count"));
            if (count.Length == 0) { count = Text(At(root, "count")); }
            var message = Text(At(root, "message"));
            if (message.Length == 0) { message = Text(At(root, "note")); }
            return new(Text(At(root, "type")), Text(At(root, "reason")), message, count.Length == 0 ? "—" : count,
                Timestamp(last, now) is { Length: > 0 } lastSeen ? lastSeen : "Not reported",
                DateTimeOffset.TryParse(Text(last), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var observed) ? observed : null);
        }
        catch (JsonException) { return null; }
    }

    private static void Pod(List<KubernetesDetailProperty> properties, JsonElement spec, JsonElement status, string? ns)
    {
        var node = Text(At(spec, "nodeName"));
        if (node.Length > 0) { properties.Add(new("Node", node, new("", "v1", "nodes", null, node, "", ""))); }
        var account = Text(At(spec, "serviceAccountName"));
        if (account.Length > 0) { properties.Add(new("Service account", account, new("", "v1", "serviceaccounts", ns, account, "", ""))); }
        Add(properties, "Pod IP", Text(At(status, "podIP")));
        Add(properties, "Pod IPs", string.Join(", ", Items(At(status, "podIPs")).Select(item => Text(At(item, "ip")))));
        var ready = Items(At(status, "conditions")).FirstOrDefault(item => Text(At(item, "type")) is "Ready");
        Add(properties, "Ready", Text(At(ready, "status")) is { Length: > 0 } readiness ? readiness : "Unknown");
        Add(properties, "Host IP", Text(At(status, "hostIP")));
        Add(properties, "QoS class", Text(At(status, "qosClass")));
        Add(properties, "Priority", Text(At(spec, "priority")));
        Add(properties, "Restart policy", Text(At(spec, "restartPolicy")));
        Add(properties, "Node selector", JoinPairs(At(spec, "nodeSelector")));
        var tolerations = Items(At(spec, "tolerations")).ToArray();
        if (tolerations.Length > 0) { Add(properties, "Tolerations", tolerations.Length.ToString(CultureInfo.InvariantCulture)); }
    }

    private static void Node(List<KubernetesDetailProperty> properties, JsonElement spec, JsonElement status)
    {
        Add(properties, "Scheduling", Boolean(At(spec, "unschedulable")) == true ? "Cordoned" : "Schedulable");
        var ready = Items(At(status, "conditions")).FirstOrDefault(item => Text(At(item, "type")) is "Ready");
        Add(properties, "Ready", Text(At(ready, "status")) is { Length: > 0 } readiness ? readiness : "Unknown");
        foreach (var address in Items(At(status, "addresses")))
        { Add(properties, Text(At(address, "type")), Text(At(address, "address"))); }
        foreach (var (label, key) in new[] { ("Kubelet", "kubeletVersion"), ("Operating system", "osImage"), ("Kernel", "kernelVersion"), ("Container runtime", "containerRuntimeVersion"), ("Architecture", "architecture") })
        { Add(properties, label, Text(At(status, "nodeInfo", key))); }
        foreach (var key in new[] { "cpu", "memory", "pods", "ephemeral-storage" })
        {
            Add(properties, $"Capacity · {key}", Text(At(status, "capacity", key)));
            Add(properties, $"Allocatable · {key}", Text(At(status, "allocatable", key)));
        }
        Add(properties, "Pod CIDR", Text(At(spec, "podCIDR")));
        var taints = Items(At(spec, "taints")).Select(taint => $"{Text(At(taint, "key"))}={Text(At(taint, "value"))}:{Text(At(taint, "effect"))}");
        Add(properties, "Taints", string.Join(", ", taints));
    }

    private static void Workload(List<KubernetesDetailProperty> properties, JsonElement spec, JsonElement status)
    {
        Add(properties, "Desired replicas", Text(At(spec, "replicas")));
        Add(properties, "Ready replicas", Text(At(status, "readyReplicas")));
        Add(properties, "Available replicas", Text(At(status, "availableReplicas")));
        Add(properties, "Updated replicas", Text(At(status, "updatedReplicas")));
        Add(properties, "Desired scheduled", Text(At(status, "desiredNumberScheduled")));
        Add(properties, "Ready", Text(At(status, "numberReady")));
        Add(properties, "Strategy", Text(At(spec, "strategy", "type")));
        Add(properties, "Selector", JoinPairs(At(spec, "selector", "matchLabels")));
    }

    private static void Service(List<KubernetesDetailProperty> properties, JsonElement spec, JsonElement status)
    {
        Add(properties, "Type", Text(At(spec, "type")));
        Add(properties, "Cluster IP", Text(At(spec, "clusterIP")));
        Add(properties, "External IPs", string.Join(", ", Items(At(spec, "externalIPs")).Select(Text)));
        Add(properties, "External name", Text(At(spec, "externalName")));
        Add(properties, "Selector", JoinPairs(At(spec, "selector")));
        Add(properties, "Session affinity", Text(At(spec, "sessionAffinity")));
        Add(properties, "External traffic policy", Text(At(spec, "externalTrafficPolicy")));
        Add(properties, "Ports", string.Join(", ", Items(At(spec, "ports")).Select(port =>
            $"{Text(At(port, "port"))} → {Text(At(port, "targetPort"))}/{Text(At(port, "protocol"))}{(Text(At(port, "nodePort")) is { Length: > 0 } nodePort ? $" · Node {nodePort}" : "")}")));
        Add(properties, "Load balancer", string.Join(", ", Items(At(status, "loadBalancer", "ingress"))
            .Select(item => Text(At(item, "ip")) is { Length: > 0 } ip ? ip : Text(At(item, "hostname")))));
    }

    private static IReadOnlyList<KubernetesDetailContainer> ProjectContainers(JsonElement spec, JsonElement status)
    {
        var containerSpec = At(spec, "template", "spec");
        if (containerSpec.ValueKind != JsonValueKind.Object) { containerSpec = spec; }
        List<KubernetesDetailContainer> result = [];
        foreach (var (field, statusField, category) in new[] { ("containers", "containerStatuses", ""), ("initContainers", "initContainerStatuses", "Init · "), ("ephemeralContainers", "ephemeralContainerStatuses", "Ephemeral · ") })
        {
            var states = Items(At(status, statusField)).ToArray();
            foreach (var container in Items(At(containerSpec, field)))
            {
                var name = Text(At(container, "name"));
                var state = states.FirstOrDefault(item => string.Equals(Text(At(item, "name")), name, StringComparison.Ordinal));
                var current = At(state, "state");
                var stateName = "Not reported";
                foreach (var key in new[] { "waiting", "running", "terminated" })
                {
                    if (At(current, key).ValueKind != JsonValueKind.Object) { continue; }
                    var reason = Text(At(current, key, "reason"));
                    stateName = reason.Length > 0 ? reason : char.ToUpperInvariant(key[0]) + key[1..];
                    if (key is "terminated" && Text(At(current, key, "exitCode")) is { Length: > 0 } exitCode)
                    { stateName += $" · Exit {exitCode}"; }
                    break;
                }
                result.Add(new(category + name, Text(At(container, "image")), Boolean(At(state, "ready")) switch { true => "Ready", false => "Not ready", _ => "Not reported" },
                    Text(At(state, "restartCount")) is { Length: > 0 } restarts ? restarts : "—", stateName,
                    string.Join(", ", Items(At(container, "ports")).Select(port => $"{Text(At(port, "containerPort"))}/{Text(At(port, "protocol"))}"))));
            }
        }
        return result;
    }

    private static IReadOnlyList<KubernetesDetailProperty> Pairs(JsonElement value, bool annotations = false) =>
        value.ValueKind != JsonValueKind.Object ? [] : [.. value.EnumerateObject()
            .Where(item => !annotations || item.Name is not "kubectl.kubernetes.io/last-applied-configuration")
            .Take(128).Select(item => new KubernetesDetailProperty(item.Name, Text(item.Value)))];
    private static string JoinPairs(JsonElement value) => string.Join(", ", Pairs(value).Select(item => $"{item.Label}={item.Value}"));
    private static void Add(List<KubernetesDetailProperty> rows, string label, string value)
    { if (value.Length > 0) { rows.Add(new(label, value.Length > 2048 ? value[..2048] + "…" : value)); } }
    private static IEnumerable<JsonElement> Items(JsonElement element) => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Take(128) : [];
    private static bool? Boolean(JsonElement value) => value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    private static JsonElement At(JsonElement value, params string[] path)
    {
        foreach (var key in path)
        { if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value)) { return default; } }
        return value;
    }
    private static string Text(JsonElement value)
    {
        var text = value.ValueKind switch { JsonValueKind.String => value.GetString() ?? "", JsonValueKind.Number => value.ToString(), JsonValueKind.True => "Yes", JsonValueKind.False => "No", _ => "" };
        if (AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(text)) { return "[redacted]"; }
        return text.Length > 1024 ? text[..1024] + "…" : text;
    }
    private static string Timestamp(JsonElement value, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)) { return string.Empty; }
        var elapsed = now - timestamp;
        var age = elapsed.TotalDays >= 1 ? $"{(int)elapsed.TotalDays}d {(int)elapsed.Hours}h"
            : elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
            : elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes}m" : "just now";
        return $"{age}{(age is "just now" ? "" : " ago")} · {timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}";
    }
}
