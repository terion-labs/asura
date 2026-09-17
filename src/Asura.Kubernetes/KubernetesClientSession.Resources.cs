using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public async ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        var resources = new List<KubernetesApiResource>();
        var unavailable = new List<string>();
        await DiscoverVersionAsync("", "v1", resources, unavailable, cancellationToken).ConfigureAwait(false);
        JsonDocument groupDocument;
        try
        {
            groupDocument = await ReadJsonAsync("apis", cancellationToken).ConfigureAwait(false);
        }
        catch (KubernetesRequestException exception) when (exception.Code is KubernetesErrorCode.Forbidden or KubernetesErrorCode.NotFound or KubernetesErrorCode.ServerUnavailable)
        {
            unavailable.Add("apis");
            return new KubernetesDiscovery(resources, unavailable);
        }

        using JsonDocument groups = groupDocument;
        int versionRequests = 0;
        if (groups.RootElement.TryGetProperty("groups", out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement group in array.EnumerateArray())
            {
                if (++versionRequests > 256 || resources.Count >= 4096 || unavailable.Count >= 256)
                {
                    throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "API discovery exceeds the resource limit.");
                }

                string name = Text(group, "name");
                var versions = new HashSet<string>(StringComparer.Ordinal);
                if (group.TryGetProperty("preferredVersion", out JsonElement preferred))
                {
                    string version = Text(preferred, "version");
                    versions.Add(version);
                    await DiscoverVersionAsync(name, version, resources, unavailable, cancellationToken).ConfigureAwait(false);
                }

                if (Property(group, "versions") is { ValueKind: JsonValueKind.Array } served)
                {
                    foreach (JsonElement item in served.EnumerateArray())
                    {
                        string version = Text(item, "version");
                        if (versions.Add(version))
                        {
                            if (++versionRequests > 256)
                            {
                                throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "API discovery exceeds the version request limit.");
                            }

                            await DiscoverVersionAsync(name, version, resources, unavailable, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        return new KubernetesDiscovery(resources, unavailable);
    }

    public async ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A resource page contains between 1 and 1000 objects.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var query = new List<KeyValuePair<string, string>>
        {
            new("limit", request.Limit.ToString(CultureInfo.InvariantCulture)),
        };
        AddQuery(query, "continue", request.ContinueToken);
        AddQuery(query, "labelSelector", request.LabelSelector);
        AddQuery(query, "fieldSelector", request.FieldSelector);
        string path = ResourcePath(request.ApiResource.Group, request.ApiResource.Version, request.ApiResource.Resource,
            request.ApiResource.Namespaced ? request.Namespace : null);
        using JsonDocument document = await ReadJsonAsync(WithQuery(path, query), linked.Token).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes list response contains no items array.");
        }

        var results = new List<KubernetesResourceDocument>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (results.Count >= request.Limit)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "The Kubernetes API exceeded the requested page limit.");
            }

            results.Add(Project(item, request.ApiResource.Group, request.ApiResource.Version, request.ApiResource.Resource));
        }

        JsonElement metadata = Property(root, "metadata");
        string continuation = Text(metadata, "continue");
        return new KubernetesResourcePage(results, Text(metadata, "resourceVersion"),
            string.IsNullOrEmpty(continuation) ? null : continuation, !string.IsNullOrEmpty(continuation));
    }

    public async ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using JsonDocument document = await ReadJsonAsync(ResourcePath(resource), linked.Token).ConfigureAwait(false);
        KubernetesResourceDocument result = Project(document.RootElement, resource.Group, resource.Version, resource.Resource);
        ValidateUid(resource, result.Reference);
        return result;
    }

    public async ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePod(request.Pod);
        if (request.MaximumBytes is < 1 or > 4 * 1024 * 1024 || request.TailLines is < 1 or > 10000 || request.SinceSeconds is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await InspectAsync(request.Pod, linked.Token).ConfigureAwait(false);
        var query = new List<KeyValuePair<string, string>>
        {
            new("tailLines", request.TailLines.ToString(CultureInfo.InvariantCulture)),
            new("limitBytes", request.MaximumBytes.ToString(CultureInfo.InvariantCulture)),
            new("previous", request.Previous ? "true" : "false"),
            new("timestamps", request.Timestamps ? "true" : "false"),
        };
        AddQuery(query, "container", request.Container);
        AddQuery(query, "sinceSeconds", request.SinceSeconds?.ToString(CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await GetAsync(WithQuery(ResourcePath(request.Pod) + "/log", query), linked.Token).ConfigureAwait(false);
        byte[] bytes = await ReadBoundedAsync(response, request.MaximumBytes, linked.Token).ConfigureAwait(false);
        return new KubernetesLogPage(Encoding.UTF8.GetString(bytes), bytes.Length >= request.MaximumBytes);
    }

    private async ValueTask DiscoverVersionAsync(
        string group,
        string version,
        List<KubernetesApiResource> resources,
        List<string> unavailable,
        CancellationToken cancellationToken)
    {
        try
        {
            string path = ApiPath(group, version);
            using JsonDocument document = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
            JsonElement entries = Property(document.RootElement, "resources");
            if (entries.ValueKind != JsonValueKind.Array)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "API discovery returned no resource collection.");
            }

            foreach (JsonElement entry in entries.EnumerateArray())
            {
                string name = Text(entry, "name");
                if (name.Contains('/', StringComparison.Ordinal))
                {
                    continue;
                }

                ValidateSegment(name);
                JsonElement verbs = Property(entry, "verbs");
                string[] availableVerbs = verbs.ValueKind == JsonValueKind.Array
                    ? [.. verbs.EnumerateArray().Where(static value => value.ValueKind == JsonValueKind.String).Select(static value => value.GetString()!)]
                    : [];
                resources.Add(new KubernetesApiResource(group, version, name, Text(entry, "kind"),
                    Property(entry, "namespaced").ValueKind == JsonValueKind.True, availableVerbs));
                if (resources.Count > 4096)
                {
                    throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "API discovery exceeds the resource limit.");
                }
            }
        }
        catch (KubernetesRequestException exception) when (exception.Code is KubernetesErrorCode.Forbidden or KubernetesErrorCode.NotFound or KubernetesErrorCode.ServerUnavailable)
        {
            unavailable.Add(string.IsNullOrEmpty(group) ? version : group + "/" + version);
        }
    }

    private static KubernetesResourceDocument Project(JsonElement value, string group, string version, string resource)
    {
        JsonElement metadata = Property(value, "metadata");
        string name = Text(metadata, "name");
        if (string.IsNullOrEmpty(name))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes resource has no name.");
        }

        string ns = Text(metadata, "namespace");
        var reference = new KubernetesResourceReference(group, version, resource, ns.Length == 0 ? null : ns,
            name, Text(metadata, "uid"), Text(metadata, "resourceVersion"));
        string kind = Text(value, "kind");
        string summary = Text(Property(value, "status"), "phase");
        if (summary.Length == 0 && Property(Property(value, "status"), "conditions") is { ValueKind: JsonValueKind.Array } conditions)
        {
            summary = string.Join(", ", conditions.EnumerateArray().Where(static condition => Text(condition, "status") is "True")
                .Select(static condition => Text(condition, "type")));
        }

        string json = value.GetRawText();
        if (string.IsNullOrEmpty(group) && resource is "secrets")
        {
            JsonObject sanitized = JsonNode.Parse(json)!.AsObject();
            SanitizeSecret(sanitized, "data");
            SanitizeSecret(sanitized, "stringData");
            // The kubectl last-applied annotation can contain a complete plaintext Secret.
            if (sanitized["metadata"] is JsonObject secretMetadata)
            {
                secretMetadata.Remove("annotations");
                secretMetadata.Remove("managedFields");
            }

            json = sanitized.ToJsonString();
        }

        return new KubernetesResourceDocument(reference, kind, summary, json);
    }

    private static void SanitizeSecret(JsonObject resource, string field)
    {
        if (resource[field] is JsonObject values)
        {
            foreach (string key in values.Select(static item => item.Key).ToArray())
            {
                values[key] = "[redacted]";
            }
        }
        else
        {
            resource.Remove(field);
        }
    }

    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement result) ? result : default;

    private static string Text(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } result ? result.GetString()! : string.Empty;

    private static string ResourcePath(KubernetesResourceReference resource) =>
        ResourcePath(resource.Group, resource.Version, resource.Resource, resource.Namespace) + "/" + ValidateSegment(resource.Name);

    private static string ResourcePath(string group, string version, string resource, string? ns) =>
        ApiPath(group, version) + (string.IsNullOrEmpty(ns) ? "" : "/namespaces/" + ValidateSegment(ns)) + "/" + ValidateSegment(resource);

    private static string ApiPath(string group, string version) =>
        string.IsNullOrEmpty(group) ? "api/" + ValidateSegment(version) : "apis/" + ValidateSegment(group) + "/" + ValidateSegment(version);

    private static string ValidateSegment(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 253 || value is "." or ".."
            || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A Kubernetes resource path contains an invalid identifier.");
        }

        return value;
    }

    private static void AddQuery(List<KeyValuePair<string, string>> query, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            if (value.Length > 8192)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A Kubernetes query value exceeds its size limit.");
            }

            query.Add(new(key, value));
        }
    }

    private static string WithQuery(string path, List<KeyValuePair<string, string>> query) =>
        path + "?" + string.Join("&", query.Select(static pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

    private static void ValidateUid(KubernetesResourceReference expected, KubernetesResourceReference actual)
    {
        if (expected.Uid.Length > 0 && !string.Equals(expected.Uid, actual.Uid, StringComparison.Ordinal))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.TargetChanged, "The Kubernetes resource was replaced. Select it again before continuing.");
        }
    }

    private static void ValidatePod(KubernetesResourceReference pod)
    {
        if (pod.Group.Length != 0 || pod.Version is not "v1" || pod.Resource is not "pods" || string.IsNullOrEmpty(pod.Namespace) || string.IsNullOrEmpty(pod.Uid))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A pod operation requires an exact namespace and pod identity.");
        }
    }
}
