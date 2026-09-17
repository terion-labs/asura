using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

internal sealed partial class KubernetesAgentReferences
{
    private sealed record Preview(AgentRunId RunId, KubernetesMutationRequest Mutation, string Description, DateTimeOffset ExpiresAt);

    public async ValueTask<AgentKubernetesControlRequest> PrepareControlAsync(IKubernetesPanelSession session,
        AgentRunId runId, AgentKubernetesControlIntent intent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (intent.IsCommit)
        {
            var preview = RequirePreview(intent.Reference, runId, now);
            return new(intent, preview.Mutation with { DryRun = false }, preview.Description);
        }
        var resource = Resolve<KubernetesResourceReference>(intent.Reference);
        if (string.IsNullOrEmpty(resource.Uid) || string.IsNullOrEmpty(resource.ResourceVersion)
            || resource.Group.Length == 0 && string.Equals(resource.Resource, "secrets", StringComparison.Ordinal))
        { throw new ArgumentException("This mutation requires a loaded non-Secret resource identity."); }
        var current = await session.InspectAsync(resource, cancellationToken).ConfigureAwait(false);
        RequireSameResource(resource, current.Reference);
        if (!string.Equals(resource.ResourceVersion, current.Reference.ResourceVersion, StringComparison.Ordinal))
        { throw new ArgumentException("The resource changed; inspect it again before previewing a mutation."); }
        var kind = KubernetesMutationKind.JsonPatch;
        string? json;
        string description;
        switch (intent.Operation)
        {
            case AgentKubernetesControlOperation.Apply:
                kind = KubernetesMutationKind.Apply;
                var manifest = JsonNode.Parse(intent.ManifestJson!) as JsonObject
                    ?? throw new ArgumentException("Apply requires one manifest.");
                var metadata = manifest["metadata"];
                var apiVersion = resource.Group.Length == 0 ? resource.Version : resource.Group + "/" + resource.Version;
                if (!string.Equals(manifest["kind"]?.GetValue<string>(), current.Kind, StringComparison.Ordinal)
                    || !string.Equals(manifest["apiVersion"]?.GetValue<string>(), apiVersion, StringComparison.Ordinal)
                    || !string.Equals(metadata?["name"]?.GetValue<string>(), resource.Name, StringComparison.Ordinal)
                    || !string.Equals(metadata?["namespace"]?.GetValue<string>(), resource.Namespace, StringComparison.Ordinal))
                { throw new ArgumentException("The manifest must match the reviewed resource."); }
                json = manifest.ToJsonString(); description = "Apply manifest";
                break;
            case AgentKubernetesControlOperation.Scale:
                if (!(resource.Group is "apps" && resource.Resource is "deployments" or "statefulsets" or "replicasets"
                    || resource.Group.Length == 0 && string.Equals(resource.Resource, "replicationcontrollers", StringComparison.Ordinal)))
                { throw new ArgumentException("The selected resource does not support this scale operation."); }
                json = new JsonArray(new JsonObject { ["op"] = "add", ["path"] = "/spec/replicas", ["value"] = intent.Replicas!.Value }).ToJsonString();
                description = "Scale to " + intent.Replicas.Value.ToString(CultureInfo.InvariantCulture) + " replicas";
                break;
            case AgentKubernetesControlOperation.Restart:
                if (resource.Group is not "apps" || resource.Resource is not ("deployments" or "statefulsets" or "daemonsets"))
                { throw new ArgumentException("The selected resource does not support rollout restart."); }
                using (var document = JsonDocument.Parse(current.Json))
                {
                    var patch = new JsonArray();
                    var template = document.RootElement.GetProperty("spec").GetProperty("template");
                    var hasMetadata = template.TryGetProperty("metadata", out var templateMetadata);
                    if (!hasMetadata) { patch.Add((JsonNode)new JsonObject { ["op"] = "add", ["path"] = "/spec/template/metadata", ["value"] = new JsonObject() }); }
                    if (!hasMetadata || !templateMetadata.TryGetProperty("annotations", out _))
                    { patch.Add((JsonNode)new JsonObject { ["op"] = "add", ["path"] = "/spec/template/metadata/annotations", ["value"] = new JsonObject() }); }
                    patch.Add((JsonNode)new JsonObject { ["op"] = "add", ["path"] = "/spec/template/metadata/annotations/kubectl.kubernetes.io~1restartedAt", ["value"] = now.ToString("O", CultureInfo.InvariantCulture) });
                    json = patch.ToJsonString(); description = "Restart rollout";
                }
                break;
            case AgentKubernetesControlOperation.Delete:
                kind = KubernetesMutationKind.Delete; json = null; description = "Delete resource";
                break;
            default: throw new ArgumentOutOfRangeException(nameof(intent));
        }
        return new(intent, new(resource, kind, json), description);
    }

    public void ValidateControl(AgentKubernetesControlRequest request, AgentRunId runId, DateTimeOffset now, bool consume)
    {
        if (request.Intent.IsCommit)
        {
            lock (_gate)
            {
                var preview = RequirePreview(request.Intent.Reference, runId, now);
                if (preview.Mutation with { DryRun = false } != request.Mutation
                    || !string.Equals(preview.Description, request.Description, StringComparison.Ordinal))
                { throw new ArgumentException("The mutation does not match the reviewed payload."); }
                if (consume) { _references.Remove(request.Intent.Reference); }
            }
        }
        else
        {
            var resource = Resolve<KubernetesResourceReference>(request.Intent.Reference);
            if (resource != request.Mutation.Resource || !request.Mutation.DryRun)
            { throw new ArgumentException("The mutation no longer matches its resource reference."); }
        }
    }

    public string SavePreview(AgentRunId runId, AgentKubernetesControlRequest request, DateTimeOffset now) =>
        Add(new Preview(runId, request.Mutation with { DryRun = true }, request.Description, now.AddMinutes(5)));

    private Preview RequirePreview(string reference, AgentRunId runId, DateTimeOffset now)
    {
        var preview = Resolve<Preview>(reference);
        if (preview.RunId != runId || preview.ExpiresAt <= now)
        { throw new ArgumentException("The mutation preview expired or belongs to another agent run."); }
        return preview;
    }
}
