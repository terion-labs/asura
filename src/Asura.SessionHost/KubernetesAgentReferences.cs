using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Asura.Application;

namespace Asura.SessionHost;

/// <summary>
/// Bounded references tied to one immutable hosted session. Replacing the session
/// invalidates every token. Weak ownership does not keep a closed engine alive.
/// </summary>
internal sealed partial class KubernetesAgentReferences
{
    private static readonly ConditionalWeakTable<IKubernetesPanelSession, KubernetesAgentReferences> Pools = [];
    private readonly Dictionary<string, object> _references = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly Queue<string> _order = new();
    private const int MaximumReferences = 2048;
    private const int MaximumResultBytes = 64 * 1024;

    public static KubernetesAgentReferences For(IKubernetesPanelSession session) =>
        Pools.GetValue(session, static _ => new());

    public async ValueTask<AgentKubernetesReadResult> ReadAsync(IKubernetesPanelSession session,
        AgentKubernetesReadRequest request, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("content_origin", "untrusted_kubernetes");
        writer.WriteString("panel_id", request.PanelId.Value);
        var count = 0;
        switch (request.Operation)
        {
            case AgentKubernetesReadOperation.Discover:
                DiscoveryCursor discovery;
                if (request.Continuation is { } discoveryContinuation)
                {
                    discovery = Resolve<DiscoveryCursor>(discoveryContinuation);
                }
                else
                {
                    var current = await session.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                    if (current.Resources.Count > 10_000) { throw new InvalidDataException("Kubernetes discovery exceeded its reference budget."); }
                    discovery = new([.. current.Resources], 0, current.UnavailableGroups.Count);
                }
                writer.WriteStartArray("resources");
                foreach (var kind in discovery.Resources.Skip(discovery.Offset).Take(100))
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind_ref", Add(kind));
                    writer.WriteString("group", Text(kind.Group, 253));
                    writer.WriteString("version", Text(kind.Version, 64));
                    writer.WriteString("resource", Text(kind.Resource, 253));
                    writer.WriteString("kind", Text(kind.Kind, 128));
                    writer.WriteBoolean("namespaced", kind.Namespaced);
                    writer.WriteEndObject();
                    count++;
                }
                writer.WriteEndArray();
                var nextOffset = discovery.Offset + count;
                writer.WriteBoolean("is_truncated", discovery.Resources.Count > nextOffset);
                if (discovery.Resources.Count > nextOffset)
                {
                    writer.WriteString("continuation", Add(discovery with { Offset = nextOffset }));
                }
                writer.WriteNumber("unavailable_group_count", discovery.UnavailableGroupCount);
                break;
            case AgentKubernetesReadOperation.List:
                var api = Resolve<KubernetesApiResource>(request.Reference!);
                string? continueToken = null;
                if (request.Continuation is { } listContinuation)
                {
                    var cursor = Resolve<ListCursor>(listContinuation);
                    if (cursor.Api != api || !string.Equals(cursor.Namespace, request.NamespaceName, StringComparison.Ordinal))
                    {
                        throw new ArgumentException("The continuation belongs to a different Kubernetes list.");
                    }
                    continueToken = cursor.Token;
                }
                var page = await session.ListAsync(new(api, request.NamespaceName, Limit: request.Limit, ContinueToken: continueToken), cancellationToken)
                    .ConfigureAwait(false);
                if (page.Items.Count > request.Limit) { throw new InvalidDataException("Kubernetes exceeded the authorized list bound."); }
                writer.WriteStartArray("items");
                foreach (var document in page.Items)
                {
                    WriteDocument(writer, document, includeManifest: false);
                    count++;
                }
                writer.WriteEndArray();
                writer.WriteBoolean("is_truncated", page.IsTruncated || page.ContinueToken is not null);
                if (page.ContinueToken is { Length: > 0 } token)
                {
                    if (token.Length > 16_384) { throw new InvalidDataException("Kubernetes continuation exceeded its budget."); }
                    writer.WriteString("continuation", Add(new ListCursor(api, request.NamespaceName, token)));
                }
                break;
            case AgentKubernetesReadOperation.Inspect:
                var reference = Resolve<KubernetesResourceReference>(request.Reference!);
                var inspected = await session.InspectAsync(reference, cancellationToken).ConfigureAwait(false);
                RequireSameResource(reference, inspected.Reference);
                writer.WritePropertyName("resource");
                WriteDocument(writer, inspected, includeManifest: true);
                count = 1;
                break;
            case AgentKubernetesReadOperation.Logs:
                var pod = Resolve<KubernetesResourceReference>(request.Reference!);
                if (!string.Equals(pod.Resource, "pods", StringComparison.Ordinal) || pod.Group.Length != 0)
                {
                    throw new ArgumentException("Logs require a discovered pod reference.");
                }
                var logs = await session.ReadLogsAsync(new(pod, request.Container, request.Limit, MaximumBytes: 8192), cancellationToken)
                    .ConfigureAwait(false);
                if (Encoding.UTF8.GetByteCount(logs.Text) > 8192) { throw new InvalidDataException("Kubernetes exceeded the authorized log bound."); }
                writer.WriteString("text", Text(logs.Text, 8192));
                writer.WriteBoolean("is_truncated", logs.IsTruncated);
                count = 1;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
        writer.WriteEndObject();
        writer.Flush();
        if (buffer.WrittenCount > MaximumResultBytes) { throw new InvalidDataException("Kubernetes observation exceeded its result budget."); }
        return new(Encoding.UTF8.GetString(buffer.WrittenSpan), count);
    }

    private void WriteDocument(Utf8JsonWriter writer, KubernetesResourceDocument document, bool includeManifest)
    {
        writer.WriteStartObject();
        writer.WriteString("resource_ref", Add(document.Reference));
        writer.WriteString("name", Text(document.Reference.Name, 253));
        writer.WriteString("namespace", Text(document.Reference.Namespace ?? string.Empty, 63));
        writer.WriteString("kind", Text(document.Kind, 128));
        writer.WriteString("summary", Text(document.Summary, 128));
        // Secrets remain metadata-only even if an invalid provider supplies a body.
        if (includeManifest && !(document.Reference.Group.Length == 0
            && string.Equals(document.Reference.Resource, "secrets", StringComparison.Ordinal)))
        {
            writer.WriteString("manifest_json", Text(document.Json, 8192));
            writer.WriteBoolean("manifest_truncated", document.Json.Length > 8192);
        }
        writer.WriteEndObject();
    }

    private sealed record DiscoveryCursor(IReadOnlyList<KubernetesApiResource> Resources, int Offset, int UnavailableGroupCount);
    private sealed record ListCursor(KubernetesApiResource Api, string? Namespace, string Token);

    private string Add(object reference)
    {
        lock (_gate)
        {
            while (_order.Count >= MaximumReferences) { _references.Remove(_order.Dequeue()); }
            var token = Guid.NewGuid().ToString("N");
            _references.Add(token, reference);
            _order.Enqueue(token);
            return token;
        }
    }

    private T Resolve<T>(string token)
    {
        lock (_gate)
        {
            return _references.TryGetValue(token, out var value) && value is T typed
                ? typed : throw new ArgumentException("The Kubernetes reference is expired or belongs to another session.");
        }
    }

    private static void RequireSameResource(KubernetesResourceReference expected, KubernetesResourceReference actual)
    {
        if (!string.Equals(expected.Group, actual.Group, StringComparison.Ordinal)
            || !string.Equals(expected.Version, actual.Version, StringComparison.Ordinal)
            || !string.Equals(expected.Resource, actual.Resource, StringComparison.Ordinal)
            || !string.Equals(expected.Namespace, actual.Namespace, StringComparison.Ordinal)
            || !string.Equals(expected.Name, actual.Name, StringComparison.Ordinal)
            || !string.Equals(expected.Uid, actual.Uid, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Kubernetes resource identity changed.");
        }
    }

    private static string Text(string value, int maximumCharacters)
    {
        if (AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(value)) { return "[REDACTED SECRET VALUE]"; }
        return value.Length <= maximumCharacters ? value : value[..maximumCharacters];
    }
}
