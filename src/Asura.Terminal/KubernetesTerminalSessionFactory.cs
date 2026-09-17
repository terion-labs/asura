using Asura.Application;
using Asura.Core;

namespace Asura.Terminal;

/// <summary>Adapts an already authenticated remote TTY to the standard terminal renderer and session lifecycle.</summary>
public sealed class KubernetesTerminalSessionFactory
{
    public async ValueTask<ITerminalPanelSession> CreateAsync(
        SessionId sessionId,
        IKubernetesExecSession exec,
        TerminalLaunchRequest presentation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exec);
        ArgumentNullException.ThrowIfNull(presentation);
        KubernetesPtyConnection? transport = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (presentation.InitialCommand is not null || presentation.MultiplexerSession is not null)
            {
                throw new ArgumentException("A pod terminal cannot inject startup commands or a host multiplexer.", nameof(presentation));
            }

            var availability = GhosttyVt.GhosttyVtRuntimeProbe.Detect();
            if (!availability.IsAvailable)
            {
                throw new PlatformNotSupportedException(availability.Detail);
            }

            await exec.ResizeAsync(80, 24, cancellationToken).ConfigureAwait(false);
            transport = new KubernetesPtyConnection(exec);
            return new GhosttyVtTerminalSession(sessionId, presentation, transport, 80, 24, false);
        }
        catch
        {
            if (transport is null)
            {
                await exec.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                transport.Dispose();
                await transport.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }
}
