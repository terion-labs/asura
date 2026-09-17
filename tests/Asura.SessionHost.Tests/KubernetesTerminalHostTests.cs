using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost.Tests;

public sealed class KubernetesTerminalHostTests
{
    [Fact]
    public async Task TypedPodLaunchNeverFallsBackToOrdinaryTerminalFactory()
    {
        var ordinary = new FakeTerminalSessionFactory();
        await using var host = new InMemorySessionHostClient(ordinary, new DesktopLifecyclePolicy());
        var result = await host.EnsureTerminalSessionAsync(Request("uid"), Human(), CancellationToken.None);
        Assert.IsType<HostResult<SessionSnapshot>.Failure>(result);
        Assert.Equal(0, ordinary.CreateCount);
    }

    [Fact]
    public async Task PodLaunchUsesWorkspaceFactoryAndRejectsSameSessionForRecreatedPod()
    {
        var ordinary = new FakeTerminalSessionFactory();
        var remote = new RecordingFactory();
        await using var host = new InMemorySessionHostClient(ordinary, new DesktopLifecyclePolicy(), kubernetesTerminalFactory: remote);
        var request = Request("original-uid");
        var result = await host.EnsureTerminalSessionAsync(request, Human(), CancellationToken.None);
        var opened = Assert.IsType<HostResult<SessionSnapshot>.Success>(result).Value;
        Assert.Equal(request.Owner.WorkspaceId, remote.WorkspaceId);
        Assert.Equal(request.Launch.KubernetesTarget!.BindingFingerprint, opened.Descriptor.TerminalMetadata!.KubernetesBindingFingerprint);
        Assert.Equal(1, remote.Calls);
        Assert.Equal(0, ordinary.CreateCount);
        var repeated = await host.EnsureTerminalSessionAsync(request, Human(), CancellationToken.None);
        Assert.IsType<HostResult<SessionSnapshot>.Success>(repeated);
        Assert.Equal(1, remote.Calls);
        var recreated = Request("different-uid") with { SessionId = request.SessionId };
        var rejected = await host.EnsureTerminalSessionAsync(recreated, Human(), CancellationToken.None);
        Assert.IsType<HostResult<SessionSnapshot>.Failure>(rejected);
        Assert.Equal(1, remote.Calls);
    }

    [Fact]
    public void PresentationCopiesKeepRemoteTargetAndHostLaunchCannotBeCombined()
    {
        var launch = Request("uid").Launch;
        Assert.Same(launch.KubernetesTarget, launch.WithPresentationProfiles(null, null).KubernetesTarget);
        Assert.Same(launch.KubernetesTarget, launch.WithShellActivityFallback(TerminalShellActivityFallback.None).KubernetesTarget);
        Assert.Throws<ArgumentException>(() => new TerminalLaunchRequest(null, executable: "/bin/sh", kubernetesTarget: launch.KubernetesTarget));
        Assert.Throws<ArgumentException>(() => new TerminalLaunchRequest(null, initialCommand: "echo unexpected", kubernetesTarget: launch.KubernetesTarget));
    }

    private static OperationContext Human() => OperationContext.ForHuman(new("kube-terminal-test"), idempotencyKey: IdempotencyKey.New());

    private static EnsureTerminalSessionRequest Request(string uid) => new(new("pod-terminal"),
        new(HostMode.Desktop, new("window"), new("workspace"), new("tab"), new("panel")), "Pod shell",
        new(null, connectionMetadata: new("Kubernetes test pod", null), kubernetesTarget: new(
            new(new("profile"), 1, "Test", "/unused/config", "test"),
            new(new("", "v1", "pods", "test", "pod", uid, "1"), "app", ["/bin/sh"]))));

    private sealed class RecordingFactory : IKubernetesTerminalSessionFactory
    {
        internal WorkspaceInstanceId WorkspaceId { get; private set; }
        internal int Calls { get; private set; }
        public ValueTask<ITerminalPanelSession> CreateAsync(WorkspaceInstanceId workspaceInstanceId, SessionId sessionId,
            KubernetesConnectionProfile profile, KubernetesExecRequest request, TerminalRenderProfileSnapshot? renderProfile,
            TerminalKeymapSnapshot? keymap, CancellationToken cancellationToken)
        {
            WorkspaceId = workspaceInstanceId;
            Calls++;
            return ValueTask.FromResult<ITerminalPanelSession>(new FakeTerminalSession(sessionId,
                new(null, kubernetesTarget: new(profile, request)), false, null, false));
        }
    }
}
