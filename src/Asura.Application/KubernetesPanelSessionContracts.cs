using Asura.Core;

namespace Asura.Application;

public sealed record KubernetesSessionBinding(KubernetesConnectionProfileId ProfileId, long BindingRevision, string ContextName);

public sealed record KubernetesSessionTarget(KubernetesConnectionProfile Profile, long BindingRevision)
{
    public KubernetesSessionBinding Binding => new(Profile.Id, BindingRevision, Profile.ContextName);
    public override string ToString() => $"Kubernetes session {Profile.Id.Value} revision {BindingRevision}";
}

public interface IKubernetesPanelSession : IPanelSession, IKubernetesClientSession
{
    KubernetesSessionBinding Binding { get; }
}

public interface IKubernetesHostedPanelSessionFactory
{
    CapabilitySet Capabilities { get; }
    ValueTask<IKubernetesPanelSession> CreateAsync(WorkspaceInstanceId workspaceId, SessionId sessionId,
        KubernetesSessionTarget target, CancellationToken cancellationToken);
}

public sealed record EnsureKubernetesSessionRequest(SessionId SessionId, SessionOwner Owner, string Title,
    KubernetesSessionTarget Target);
