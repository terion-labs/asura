using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public async ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        string body = PrepareMutation(request);
        if (request.Resource.Uid.Length > 0)
        {
            KubernetesResourceDocument current = await InspectAsync(request.Resource, linked.Token).ConfigureAwait(false);
            if (!string.Equals(current.Reference.ResourceVersion, request.Resource.ResourceVersion, StringComparison.Ordinal))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The resource changed after the operation was prepared.");
            }
        }

        string contentType = request.Kind switch
        {
            KubernetesMutationKind.JsonPatch => "application/json-patch+json",
            KubernetesMutationKind.Apply => "application/apply-patch+yaml",
            KubernetesMutationKind.Delete => "application/json",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        var query = new List<KeyValuePair<string, string>>();
        if (request.DryRun)
        {
            query.Add(new("dryRun", "All"));
        }

        if (request.Kind == KubernetesMutationKind.Apply)
        {
            AddQuery(query, "fieldManager", request.FieldManager);
            query.Add(new("force", request.ForceOwnership ? "true" : "false"));
        }

        using var message = new HttpRequestMessage(request.Kind == KubernetesMutationKind.Delete ? HttpMethod.Delete : HttpMethod.Patch,
            new Uri(_client.BaseUri, WithQuery(ResourcePath(request.Resource), query)))
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        linked.Token.ThrowIfCancellationRequested();
        try
        {
            using HttpResponseMessage response = await SendAsync(message, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                KubernetesMutationOutcome outcome = !request.DryRun && ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                    ? KubernetesMutationOutcome.OutcomeUnknown : KubernetesMutationOutcome.NotDispatched;
                return new KubernetesMutationResult(outcome, null, Failure(response.StatusCode).Code.ToString());
            }

            byte[] bytes = await ReadBoundedAsync(response, MaximumResponseBytes, linked.Token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            KubernetesResourceDocument? result = Text(document.RootElement, "kind") is "Status" ? null
                : Project(document.RootElement, request.Resource.Group, request.Resource.Version, request.Resource.Resource);
            return new KubernetesMutationResult(request.DryRun ? KubernetesMutationOutcome.DryRun : KubernetesMutationOutcome.Applied, result);
        }
        catch (Exception exception) when (exception is KubernetesRequestException or OperationCanceledException or JsonException or IOException)
        {
            if (request.DryRun)
            {
                throw;
            }

            // Any transport/read failure after send may follow a committed write. Never replay.
            return new KubernetesMutationResult(KubernetesMutationOutcome.OutcomeUnknown, null, "kubernetes_mutation_outcome_unknown");
        }
    }

    private static string PrepareMutation(KubernetesMutationRequest request)
    {
        if (request.Json?.Length > MaximumResponseBytes || string.IsNullOrWhiteSpace(request.FieldManager) || request.FieldManager.Length > 128)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The mutation exceeds its payload limits.");
        }

        if (request.Kind is KubernetesMutationKind.JsonPatch or KubernetesMutationKind.Delete
            && (string.IsNullOrEmpty(request.Resource.Uid) || string.IsNullOrEmpty(request.Resource.ResourceVersion)))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The mutation requires a loaded resource identity and version.");
        }

        if (request.Kind == KubernetesMutationKind.Delete)
        {
            return new JsonObject
            {
                ["apiVersion"] = "v1",
                ["kind"] = "DeleteOptions",
                ["propagationPolicy"] = "Background",
                ["preconditions"] = new JsonObject
                {
                    ["uid"] = request.Resource.Uid,
                    ["resourceVersion"] = request.Resource.ResourceVersion,
                },
            }.ToJsonString();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(request.Json ?? "", documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The mutation body is not valid JSON.");
        }

        if (request.Kind == KubernetesMutationKind.JsonPatch && node is JsonArray patch)
        {
            patch.Insert(0, Test("/metadata/resourceVersion", request.Resource.ResourceVersion));
            patch.Insert(0, Test("/metadata/uid", request.Resource.Uid));
            return patch.ToJsonString();
        }

        if (request.Kind == KubernetesMutationKind.Apply && node is JsonObject resource)
        {
            if (resource["metadata"] is not JsonObject metadata || !string.Equals(metadata["name"]?.GetValue<string>(), request.Resource.Name, StringComparison.Ordinal)
                || !string.Equals(metadata["namespace"]?.GetValue<string>(), request.Resource.Namespace, StringComparison.Ordinal))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The manifest does not match the selected resource identity.");
            }

            string apiVersion = request.Resource.Group.Length == 0 ? request.Resource.Version : request.Resource.Group + "/" + request.Resource.Version;
            if (!string.Equals(resource["apiVersion"]?.GetValue<string>(), apiVersion, StringComparison.Ordinal))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The manifest API version does not match the selected resource.");
            }

            if (request.Resource.Uid.Length > 0)
            {
                metadata["uid"] = request.Resource.Uid;
                metadata["resourceVersion"] = request.Resource.ResourceVersion;
            }

            return resource.ToJsonString();
        }

        throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The mutation body does not match its operation.");
    }

    private static JsonObject Test(string path, string value) => new()
    {
        ["op"] = "test",
        ["path"] = path,
        ["value"] = value,
    };
}
