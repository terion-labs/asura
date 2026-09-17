using Asura.Application;
using Asura.Core;
using Asura.Terminal;

namespace Asura.Desktop;

internal sealed class DesktopKubernetesTerminalSessionFactory(
    WorkspaceKubernetesPanelSessionFactory clients,
    KubernetesTerminalSessionFactory terminals) : IKubernetesTerminalSessionFactory
{
    public async ValueTask<ITerminalPanelSession> CreateAsync(WorkspaceInstanceId workspaceInstanceId, SessionId sessionId,
        KubernetesConnectionProfile profile, KubernetesExecRequest request, TerminalRenderProfileSnapshot? renderProfile,
        TerminalKeymapSnapshot? keymap, CancellationToken cancellationToken)
    {
        var client = await clients.OpenClientAsync(workspaceInstanceId, profile, cancellationToken).ConfigureAwait(false);
        OwnedExecSession? owned = null;
        try
        {
            var exec = await client.OpenExecAsync(request, cancellationToken).ConfigureAwait(false);
            owned = new(client, exec);
            return await terminals.CreateAsync(sessionId, owned,
                new TerminalLaunchRequest(null, renderProfile: renderProfile, keymap: keymap,
                    connectionMetadata: new("Kubernetes pod terminal", null),
                    kubernetesTarget: new(profile, request)), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (owned is not null) { await owned.DisposeAsync().ConfigureAwait(false); }
            else { await client.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private sealed class OwnedExecSession(IKubernetesClientSession client, IKubernetesExecSession exec) : IKubernetesExecSession
    {
        private readonly object _gate = new();
        private Task? _close;
        public Stream StandardInput => exec.StandardInput;
        public Stream StandardOutput => exec.StandardOutput;
        public Stream StandardError => exec.StandardError;
        public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken) => exec.ResizeAsync(columns, rows, cancellationToken);
        public ValueTask CompleteInputAsync(CancellationToken cancellationToken) => exec.CompleteInputAsync(cancellationToken);
        public ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken) => exec.WaitForExitAsync(cancellationToken);
        public ValueTask DisposeAsync()
        {
            lock (_gate) { return new(_close ??= CloseAsync()); }
        }
        private async Task CloseAsync()
        {
            try { await exec.DisposeAsync().ConfigureAwait(false); }
            finally { await client.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
