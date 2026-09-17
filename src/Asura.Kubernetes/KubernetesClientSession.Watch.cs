using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public async IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(
        KubernetesWatchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var query = new List<KeyValuePair<string, string>>
        {
            new("watch", "true"),
            new("allowWatchBookmarks", "true"),
            new("timeoutSeconds", "60"),
        };
        AddQuery(query, "resourceVersion", request.ResourceVersion);
        AddQuery(query, "labelSelector", request.LabelSelector);
        AddQuery(query, "fieldSelector", request.FieldSelector);
        string path = ResourcePath(request.ApiResource.Group, request.ApiResource.Version, request.ApiResource.Resource,
            request.ApiResource.Namespaced ? request.Namespace : null);
        HttpResponseMessage? response = null;
        bool expired = false;
        try
        {
            response = await GetAsync(WithQuery(path, query), linked.Token).ConfigureAwait(false);
        }
        catch (KubernetesRequestException exception) when (exception.Code == KubernetesErrorCode.ResourceExpired)
        {
            expired = true;
        }

        if (expired)
        {
            yield return new KubernetesWatchEvent(KubernetesWatchEventKind.ResyncRequired, request.ResourceVersion);
            yield break;
        }

        using (response)
        {
            await using Stream stream = await response!.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await foreach (string line in ReadLinesAsync(stream, linked.Token).ConfigureAwait(false))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                KubernetesWatchEvent result = ParseWatchEvent(line, request.ApiResource);
                yield return result;
                if (result.Kind == KubernetesWatchEventKind.ResyncRequired)
                {
                    yield break;
                }
            }
        }
    }

    private static KubernetesWatchEvent ParseWatchEvent(string line, KubernetesApiResource resource)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            JsonElement value = Property(root, "object");
            string type = Text(root, "type");
            if (type is "ERROR")
            {
                int code = Property(value, "code") is { ValueKind: JsonValueKind.Number } codeElement && codeElement.TryGetInt32(out int status) ? status : 500;
                if (code == 410)
                {
                    return new KubernetesWatchEvent(KubernetesWatchEventKind.ResyncRequired, "");
                }

                throw Failure((System.Net.HttpStatusCode)code);
            }

            string version = Text(Property(value, "metadata"), "resourceVersion");
            if (type is "BOOKMARK")
            {
                return new KubernetesWatchEvent(KubernetesWatchEventKind.Bookmark, version);
            }

            KubernetesWatchEventKind kind = type switch
            {
                "ADDED" => KubernetesWatchEventKind.Added,
                "MODIFIED" => KubernetesWatchEventKind.Modified,
                "DELETED" => KubernetesWatchEventKind.Deleted,
                _ => throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes watch event type is invalid."),
            };
            return new KubernetesWatchEvent(kind, version, Project(value, resource.Group, resource.Version, resource.Resource));
        }
        catch (JsonException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes watch returned invalid JSON.");
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken, int maximumLineBytes = MaximumResponseBytes)
    {
        byte[] buffer = new byte[8192];
        using var line = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] == (byte)'\n')
                {
                    yield return Encoding.UTF8.GetString(line.GetBuffer(), 0, checked((int)line.Length)).TrimEnd('\r');
                    line.SetLength(0);
                }
                else
                {
                    if (line.Length >= maximumLineBytes)
                    {
                        throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "A Kubernetes watch event exceeds the size limit.");
                    }

                    line.WriteByte(buffer[index]);
                }
            }
        }

        if (line.Length > 0)
        {
            yield return Encoding.UTF8.GetString(line.GetBuffer(), 0, checked((int)line.Length));
        }
    }
}
