using Asura.Application;
using Asura.ConnectionBackend;
using Asura.Core;

namespace Asura.Desktop;

internal sealed class WorkspaceKubernetesPanelSessionFactory(TimeProvider timeProvider) : IKubernetesHostedPanelSessionFactory
{
    private readonly WorkspaceSessionFactoryRegistry<IKubernetesPanelSessionFactory> _factories = new(
        "The workspace already has a Kubernetes factory.");

    public CapabilitySet Capabilities => KubernetesPanelSession.SupportedCapabilities;

    public IDisposable Register(WorkspaceInstanceId workspaceId, IKubernetesPanelSessionFactory factory) =>
        _factories.Register(workspaceId, factory);

    internal ValueTask<IKubernetesClientSession> OpenClientAsync(WorkspaceInstanceId workspaceId,
        KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
        _factories.Resolve(workspaceId).OpenAsync(profile, cancellationToken);

    public async ValueTask<IKubernetesPanelSession> CreateAsync(WorkspaceInstanceId workspaceId, SessionId sessionId,
        KubernetesSessionTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(target.BindingRevision);
        var client = await _factories.Resolve(workspaceId).OpenAsync(target.Profile, cancellationToken).ConfigureAwait(false);
        return new KubernetesPanelSession(sessionId, target.Binding, client, timeProvider);
    }
}
