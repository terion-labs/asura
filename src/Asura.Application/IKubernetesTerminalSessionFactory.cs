using Asura.Core;

namespace Asura.Application;

/// <summary>Creates a hosted terminal whose transport belongs to the workspace's Kubernetes backend.</summary>
public interface IKubernetesTerminalSessionFactory
{
    ValueTask<ITerminalPanelSession> CreateAsync(
        WorkspaceInstanceId workspaceInstanceId,
        SessionId sessionId,
        KubernetesConnectionProfile profile,
        KubernetesExecRequest request,
        TerminalRenderProfileSnapshot? renderProfile,
        TerminalKeymapSnapshot? keymap,
        CancellationToken cancellationToken);
}
