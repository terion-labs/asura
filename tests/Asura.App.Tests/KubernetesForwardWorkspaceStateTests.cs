using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesForwardWorkspaceStateTests
{
    [Fact]
    public async Task PanelDisposalKeepsWorkspaceForwardAliveAndWorkspaceDisposalStopsIt()
    {
        var handle = new ForwardHandle();
        var factory = new ForwardFactory(handle);
        var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context");
        await using var state = new KubernetesForwardWorkspaceState(factory);
        await state.StartAsync(profile, KubernetesUiSession.Pod.Reference, 8080);
        var forward = Assert.Single(state.Forwards);
        Assert.True(factory.SessionDisposed);
        using (var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile, null) { ForwardState = state })
        {
            Assert.Same(state, panel.ForwardState);
        }
        Assert.False(handle.Disposed);
        Assert.True(forward.IsActive);
        await state.DisposeAsync();
        Assert.True(handle.Disposed);
        Assert.False(forward.IsActive);
    }

    [Fact]
    public async Task WorkspaceDisposalWaitsForPendingOpenAndDoesNotStartAForwardAfterClose()
    {
        var factory = new PendingFactory();
        var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context");
        await using var state = new KubernetesForwardWorkspaceState(factory);
        var start = state.StartAsync(profile, KubernetesUiSession.Pod.Reference, 8080);
        var shutdown = state.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        factory.Opened.SetResult(new ForwardSession(new ForwardHandle(), () => factory.SessionDisposed = true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await shutdown;
        Assert.True(factory.SessionDisposed);
        Assert.Empty(state.Forwards);
    }

    private sealed class PendingFactory : IKubernetesPanelSessionFactory
    {
        internal TaskCompletionSource<IKubernetesClientSession> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SessionDisposed { get; set; }
        public ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) => new(Opened.Task);
    }

    private sealed class ForwardFactory(ForwardHandle handle) : IKubernetesPanelSessionFactory
    {
        public bool SessionDisposed { get; private set; }
        public ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IKubernetesClientSession>(new ForwardSession(handle, () => SessionDisposed = true));
    }

    private sealed class ForwardSession(ForwardHandle handle, Action onDispose) : IKubernetesClientSession
    {
        public ValueTask<IKubernetesPortForward> StartPortForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<IKubernetesPortForward>(handle);
        public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { onDispose(); return ValueTask.CompletedTask; }
    }

    private sealed class ForwardHandle : IKubernetesPortForward
    {
        private readonly TaskCompletionSource _completion = new();
        public int LocalPort => 32123;
        public int RemotePort => 8080;
        public Task Completion => _completion.Task;
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; _completion.TrySetResult(); return ValueTask.CompletedTask; }
    }
}
