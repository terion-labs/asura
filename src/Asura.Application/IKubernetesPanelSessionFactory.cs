using Asura.Core;

namespace Asura.Application;

/// <summary>Opens a profile through the owning workspace's connection backend.</summary>
public interface IKubernetesPanelSessionFactory
{
    ValueTask<KubernetesConfigurationReview> ReviewAsync(
        KubernetesConnectionProfile profile,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<KubernetesConfigurationReview>(new NotSupportedException("Configuration review is unavailable."));

    ValueTask<IKubernetesClientSession> OpenAsync(
        KubernetesConnectionProfile profile,
        CancellationToken cancellationToken);
}
