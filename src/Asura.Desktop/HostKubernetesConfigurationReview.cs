using Asura.Application;
using Asura.ConnectionBackend;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Desktop;

/// <summary>Home/settings may inspect local configuration before a workspace exists. This factory cannot connect.</summary>
internal sealed class HostKubernetesConfigurationReview(
    IDefinitionCatalog catalog,
    ISecretVault vault,
    Func<DatabaseValueContentStore> stores) : IKubernetesPanelSessionFactory
{
    public ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
        ValueTask.FromException<IKubernetesClientSession>(new NotSupportedException("Open a workspace to connect to Kubernetes."));

    public async ValueTask<KubernetesConfigurationReview> ReviewAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken)
    {
        if (profile.TunnelConnectionId is not null)
        {
            throw new NotSupportedException("Review an SSH-scoped Kubernetes profile from its workspace.");
        }
        await using var backend = new HostConnectionBackend(SelfReentryLaunch.Detect(), stores,
            CancellationToken.None, CancellationToken.None);
        var reviewer = new KubernetesWorkspaceSessionFactory((_, token) => backend.PlanAsync("kubernetes", token), catalog, vault);
        return await reviewer.ReviewAsync(profile, cancellationToken).ConfigureAwait(false);
    }
}
