using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private HostedPanelSessionLink? _hostedSession;
    private Task _hostInitialization = Task.CompletedTask;

    public SessionId? HostedSessionId => _hostedSession?.SessionId;
    public CapabilitySet HostedCapabilities => _hostedSession?.Capabilities ?? CapabilitySet.Empty;
    public bool HasHostedSession => _hostedSession?.IsLinked == true;

    public Task StartHostingAsync(ISessionHostClient sessionClient, ClientId clientId, SessionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(sessionClient);
        ArgumentNullException.ThrowIfNull(owner);
        if (_disposed || Profile is null) { return Task.CompletedTask; }
        if (_hostedSession is not null) { return _hostInitialization; }
        _hostedSession = new(sessionClient, clientId, owner, PanelKind.Kubernetes);
        _hostInitialization = InitializeHostedSessionAsync(sessionClient);
        return _hostInitialization;
    }

    private async Task InitializeHostedSessionAsync(ISessionHostClient sessionClient)
    {
        await Initialization;
        if (_disposed || _session is null || Profile is null || _hostedSession is not { } hosted) { return; }
        await hosted.EnsureAsync((id, context, token) => sessionClient.EnsureKubernetesSessionAsync(
            new(id, hosted.Owner, Title, new(Profile, BindingRevision)), context, token), _lifetime.Token);
    }
}
