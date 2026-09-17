using Asura.Application;

namespace Asura.ConnectionBackend;

internal sealed partial class KubernetesWorkspaceSession
{
    public async ValueTask<KubernetesMutationResult> SetNodeSchedulableAsync(KubernetesNodeSchedulingRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.NodeScheduling, NodeScheduling: request), cancellationToken).ConfigureAwait(false)).Mutation
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesDrainReview> ReviewNodeDrainAsync(KubernetesNodeDrainRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.NodeDrainReview, NodeDrain: request), cancellationToken).ConfigureAwait(false)).NodeDrainReview
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesDrainResult> ExecuteNodeDrainAsync(string reviewToken, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.NodeDrainExecute, ReviewToken: reviewToken), cancellationToken).ConfigureAwait(false)).NodeDrainResult
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesHelmChangeReview> ReviewHelmChangeAsync(KubernetesHelmChangeRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.HelmChangeReview, HelmChange: request), cancellationToken).ConfigureAwait(false)).HelmChangeReview
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesHelmChangeResult> ExecuteHelmChangeAsync(string reviewToken, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.HelmChangeExecute, ReviewToken: reviewToken), cancellationToken).ConfigureAwait(false)).HelmChangeResult
        ?? throw InvalidResponse();
}
