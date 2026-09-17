using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Asura.Application;
using Asura.Core;
using Asura.Protocol;

namespace Asura.SessionHost.Tests;

public sealed class AgentKubernetesSessionHostTests
{
    private static readonly WindowInstanceId WindowId = new("kube-window");
    private static readonly WorkspaceInstanceId WorkspaceId = new("kube-workspace");
    private static readonly TabInstanceId TabId = new("kube-tab");
    private static readonly PanelInstanceId PanelId = new("kube-panel");
    private static readonly SessionId SessionId = new("kube-session");

    [Fact]
    public async Task ReadReceiptsAreSingleUseAndOpaqueReferencesCannotCrossSessions()
    {
        await using var first = await Harness.OpenAsync();
        var discovery = await first.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Discover));
        var kind = Token(discovery.Value().Content, "resources", "kind_ref");
        using var discoveryJson = JsonDocument.Parse(discovery.Value().Content);
        Assert.Equal("untrusted_kubernetes", discoveryJson.RootElement.GetProperty("content_origin").GetString());
        var replay = await first.Host.RunAgentKubernetesReadAsync(first.LastAuthorization, first.LastAction!, CancellationToken.None);
        Assert.IsType<HostResult<AgentKubernetesReadResult>.Failure>(replay);
        Assert.Equal(1, first.Factory.Session!.DiscoverCount);
        Assert.Equal(AgentActionOutcome.Succeeded, Assert.Single(first.Authorization.Completions).Outcome);
        await using var other = await Harness.OpenAsync();
        var rejected = await other.ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, kind));
        Assert.IsType<HostResult<AgentKubernetesReadResult>.Failure>(rejected);
        Assert.Equal(0, other.Factory.Session!.ListCount);
        Assert.Equal(AgentActionOutcome.Failed, Assert.Single(other.Authorization.Completions).Outcome);
    }

    [Fact]
    public async Task SecretBodiesAreNeverReturnedAndReplacedResourceIsRejected()
    {
        await using var harness = await Harness.OpenAsync();
        var discovery = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Discover))).Value();
        var kind = Token(discovery.Content, "resources", "kind_ref");
        var list = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, kind))).Value();
        var resource = Token(list.Content, "items", "resource_ref");
        var inspection = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Inspect, resource))).Value();
        Assert.DoesNotContain("sentinel-secret-value", inspection.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest_json", inspection.Content, StringComparison.Ordinal);
        harness.Factory.Session!.ReplaceUid = true;
        var changed = await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Inspect, resource));
        Assert.IsType<HostResult<AgentKubernetesReadResult>.Failure>(changed);
    }

    [Fact]
    public async Task AuthorizationBindsNamespaceBeforeProviderDispatch()
    {
        await using var harness = await Harness.OpenAsync();
        var discovery = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Discover))).Value();
        var kind = Token(discovery.Content, "resources", "kind_ref");
        _ = await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, kind, "production"));
        var changed = harness.LastAction! with { Request = new(PanelId, AgentKubernetesReadOperation.List, kind, "other") };
        var result = await harness.Host.RunAgentKubernetesReadAsync(harness.Authorization.Arm(harness.LastAction!), changed, CancellationToken.None);
        Assert.IsType<HostResult<AgentKubernetesReadResult>.Failure>(result);
        Assert.Equal(1, harness.Factory.Session!.ListCount);
    }

    [Fact]
    public async Task ContinuationsPageDiscoveryAndKeepListScopeAndServerTokensPrivate()
    {
        await using var harness = await Harness.OpenAsync();
        harness.Factory.Session!.DiscoveryResourceCount = 130;
        var first = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Discover))).Value();
        using var firstJson = JsonDocument.Parse(first.Content);
        Assert.Equal(100, first.ResultCount);
        var continuation = firstJson.RootElement.GetProperty("continuation").GetString();
        var next = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.Discover, continuation: continuation))).Value();
        Assert.Equal(30, next.ResultCount);
        Assert.Equal(1, harness.Factory.Session.DiscoverCount);
        var kind = Token(first.Content, "resources", "kind_ref");
        harness.Factory.Session.ListContinuation = "raw-server-token-do-not-expose";
        var page = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, kind, "production"))).Value();
        using var pageJson = JsonDocument.Parse(page.Content);
        var cursor = pageJson.RootElement.GetProperty("continuation").GetString();
        Assert.DoesNotContain("raw-server-token-do-not-expose", page.Content, StringComparison.Ordinal);
        var rejected = await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, kind, "other", continuation: cursor));
        Assert.IsType<HostResult<AgentKubernetesReadResult>.Failure>(rejected);
        Assert.Equal(1, harness.Factory.Session.ListCount);
        _ = (await harness.ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, kind, "production", continuation: cursor))).Value();
        Assert.Equal(2, harness.Factory.Session.ListCount);
        Assert.Equal("raw-server-token-do-not-expose", harness.Factory.Session.LastContinueToken);
    }

    [Theory]
    [InlineData(AgentKubernetesControlOperation.Apply)]
    [InlineData(AgentKubernetesControlOperation.Restart)]
    [InlineData(AgentKubernetesControlOperation.Delete)]
    public async Task MutationPreviewsKeepExactIdentityAndExpire(AgentKubernetesControlOperation operation)
    {
        await using var harness = await Harness.OpenAsync();
        var resource = await harness.MutableResourceAsync();
        var body = operation == AgentKubernetesControlOperation.Apply
            ? """{"apiVersion":"apps/v1","kind":"Deployment","metadata":{"name":"fixture","namespace":"default"},"spec":{"replicas":3}}"""
            : null;
        var preview = await harness.PrepareControlAsync(new(PanelId, operation, resource, body));
        Assert.Equal("original", preview.Request.Mutation.Resource.Uid);
        Assert.Equal("1", preview.Request.Mutation.Resource.ResourceVersion);
        var result = (await harness.ControlAsync(preview)).Value();
        using var json = JsonDocument.Parse(result.Content);
        var commit = await harness.PrepareControlAsync(new(PanelId, AgentKubernetesControlOperation.Commit,
            json.RootElement.GetProperty("preview_ref").GetString()!));
        harness.Advance(TimeSpan.FromMinutes(6));
        Assert.IsType<HostResult<AgentKubernetesControlResult>.Failure>(await harness.ControlAsync(commit, AgentAuthorizationSource.HumanApproval));
        Assert.Single(harness.Factory.Session!.Mutations);
    }

    [Fact]
    public async Task MutationCommitRequiresSeparateExactApprovalAndConsumesPreview()
    {
        await using var harness = await Harness.OpenAsync();
        var resource = await harness.MutableResourceAsync();
        var preview = await harness.PrepareControlAsync(new(PanelId, AgentKubernetesControlOperation.Scale, resource, replicas: 3));
        var result = (await harness.ControlAsync(preview)).Value();
        Assert.Equal(KubernetesMutationOutcome.DryRun, result.Outcome);
        using var json = JsonDocument.Parse(result.Content);
        var token = json.RootElement.GetProperty("preview_ref").GetString()!;
        var commit = await harness.PrepareControlAsync(new(PanelId, AgentKubernetesControlOperation.Commit, token));
        Assert.IsType<HostResult<AgentKubernetesControlResult>.Failure>(await harness.ControlAsync(commit));
        Assert.Single(harness.Factory.Session!.Mutations);
        var tampered = commit with { Request = commit.Request with { Mutation = commit.Request.Mutation with { Json = "[]" } } };
        var denied = await harness.Host.RunAgentKubernetesControlAsync(harness.Authorization.Arm(commit.Proposal, AgentAuthorizationSource.HumanApproval), tampered, CancellationToken.None);
        Assert.IsType<HostResult<AgentKubernetesControlResult>.Failure>(denied);
        Assert.Equal(KubernetesMutationOutcome.Applied, (await harness.ControlAsync(commit, AgentAuthorizationSource.HumanApproval)).Value().Outcome);
        Assert.Equal(2, harness.Factory.Session.Mutations.Count);
        Assert.True(harness.Factory.Session.Mutations[0].DryRun);
        Assert.False(harness.Factory.Session.Mutations[1].DryRun);
        Assert.Equal(harness.Factory.Session.Mutations[0].Json, harness.Factory.Session.Mutations[1].Json);
        Assert.IsType<HostResult<AgentKubernetesControlResult>.Failure>(await harness.ControlAsync(commit, AgentAuthorizationSource.HumanApproval));
        Assert.Equal(2, harness.Factory.Session.Mutations.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleVersionRejectsCommitAndUncertainDispatchCannotReplay(bool failAfterDispatch)
    {
        await using var harness = await Harness.OpenAsync();
        var resource = await harness.MutableResourceAsync();
        var preview = await harness.PrepareControlAsync(new(PanelId, AgentKubernetesControlOperation.Delete, resource));
        using var json = JsonDocument.Parse((await harness.ControlAsync(preview)).Value().Content);
        var commit = await harness.PrepareControlAsync(new(PanelId, AgentKubernetesControlOperation.Commit,
            json.RootElement.GetProperty("preview_ref").GetString()!));
        harness.Factory.Session!.ReplaceVersion = !failAfterDispatch;
        harness.Factory.Session.FailCommit = failAfterDispatch;
        var result = await harness.ControlAsync(commit, AgentAuthorizationSource.HumanApproval);
        if (failAfterDispatch)
        {
            Assert.Equal(KubernetesMutationOutcome.OutcomeUnknown, result.Value().Outcome);
            Assert.Equal("kubernetes_mutation_outcome_unknown", result.Value().StableCode);
        }
        else { Assert.IsType<HostResult<AgentKubernetesControlResult>.Failure>(result); }
        Assert.IsType<HostResult<AgentKubernetesControlResult>.Failure>(await harness.ControlAsync(commit, AgentAuthorizationSource.HumanApproval));
        Assert.Equal(failAfterDispatch ? 2 : 1, harness.Factory.Session.Mutations.Count);
    }

    private static string Token(string json, string array, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(array)[0].GetProperty(property).GetString()!;
    }

    private static OperationContext Context() => new(RequestId.New(),
        new ActorDescriptor(new("kube-user"), ActorKind.Human, "User", new ClientId("kube-client")),
        CancellationId: CancellationId.New());

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ManualTimeProvider _clock = new(DateTimeOffset.UnixEpoch);
        private readonly AgentKubernetesReadActionComposer _composer = new();
        public Factory Factory { get; } = new();
        public KubernetesAuthorizationConsumer Authorization { get; }
        public InMemorySessionHostClient Host { get; }
        public AgentAuthorizationId LastAuthorization { get; private set; }
        public AgentKubernetesReadAction? LastAction { get; private set; }
        private Harness()
        {
            Authorization = new(_clock, new("kube-client"));
            Host = new(new FakeTerminalSessionFactory(), new DesktopLifecyclePolicy(), _clock,
                kubernetesPanelFactory: Factory, agentAuthorizationConsumer: Authorization,
                agentKubernetesReadActionComposer: _composer);
        }
        public static async Task<Harness> OpenAsync()
        {
            var harness = new Harness();
            var panel = new PanelInstance(PanelId, PanelKind.Kubernetes, "Kubernetes");
            var tab = new TabInstance(TabId, "Kubernetes", [panel], PanelId);
            _ = (await harness.Host.RegisterWorkspaceGraphAsync(new(WindowId,
                new(WorkspaceId, "Kubernetes", [tab], TabId)), Context(), CancellationToken.None)).Value();
            _ = (await harness.Host.EnsureKubernetesSessionAsync(new(SessionId,
                new(HostMode.Desktop, WindowId, WorkspaceId, TabId, PanelId), "Kubernetes",
                new(new(new("profile"), 1, "Cluster", "/tmp/fixture-only-config", "explicit"), 1)),
                Context(), CancellationToken.None)).Value();
            return harness;
        }
        public async Task<HostResult<AgentKubernetesReadResult>> ReadAsync(AgentKubernetesReadRequest request)
        {
            var actor = new ActorDescriptor(new("kube-agent"), ActorKind.Agent, "Agent");
            var context = (await Host.InspectAgentContextAsync(new(new AgentTarget.Workspace(WindowId, WorkspaceId)),
                new(RequestId.New(), actor, CancellationId: CancellationId.New()), CancellationToken.None)).Value();
            var now = _clock.GetUtcNow();
            LastAction = _composer.Prepare(new(AgentActionId.New(), new("kube-run"), actor, 0, now, now.AddMinutes(1)), context, request);
            LastAuthorization = Authorization.Arm(LastAction);
            return await Host.RunAgentKubernetesReadAsync(LastAuthorization, LastAction, CancellationToken.None);
        }
        public async Task<string> MutableResourceAsync()
        {
            Factory.Session!.Mutable = true;
            var discovery = (await ReadAsync(new(PanelId, AgentKubernetesReadOperation.Discover))).Value();
            var list = (await ReadAsync(new(PanelId, AgentKubernetesReadOperation.List, Token(discovery.Content, "resources", "kind_ref")))).Value();
            return Token(list.Content, "items", "resource_ref");
        }
        public async Task<AgentKubernetesControlAction> PrepareControlAsync(AgentKubernetesControlIntent intent)
        {
            var actor = new ActorDescriptor(new("kube-agent"), ActorKind.Agent, "Agent");
            var context = (await Host.InspectAgentContextAsync(new(new AgentTarget.Workspace(WindowId, WorkspaceId)),
                new(RequestId.New(), actor, CancellationId: CancellationId.New()), CancellationToken.None)).Value();
            var now = _clock.GetUtcNow();
            return (await Host.PrepareAgentKubernetesControlAsync(new(AgentActionId.New(), new("kube-run"), actor, 0, now, now.AddMinutes(1)), context, intent, CancellationToken.None)).Value();
        }
        public ValueTask<HostResult<AgentKubernetesControlResult>> ControlAsync(AgentKubernetesControlAction action,
            AgentAuthorizationSource source = AgentAuthorizationSource.AutoPolicy) =>
            Host.RunAgentKubernetesControlAsync(Authorization.Arm(action.Proposal, source), action, CancellationToken.None);
        public void Advance(TimeSpan duration) => _clock.Advance(duration);
        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private sealed class Factory : IKubernetesHostedPanelSessionFactory
    {
        public CapabilitySet Capabilities { get; } = new([SessionCapabilities.AttachRead,
            SessionCapabilities.KubernetesDiscover, SessionCapabilities.KubernetesList, SessionCapabilities.KubernetesInspect,
            SessionCapabilities.KubernetesPreview, SessionCapabilities.KubernetesCommit]);
        public FakeSession? Session { get; private set; }
        public ValueTask<IKubernetesPanelSession> CreateAsync(WorkspaceInstanceId workspaceId, SessionId sessionId,
            KubernetesSessionTarget target, CancellationToken cancellationToken)
        {
            Session = new(sessionId, target.Binding, Capabilities);
            return ValueTask.FromResult<IKubernetesPanelSession>(Session);
        }
    }
    private sealed class FakeSession(SessionId id, KubernetesSessionBinding binding, CapabilitySet capabilities) : IKubernetesPanelSession
    {
        public SessionId Id => id;
        public PanelKind Kind => PanelKind.Kubernetes;
        public KubernetesSessionBinding Binding => binding;
        public CapabilitySet Capabilities => capabilities;
        public int DiscoverCount { get; private set; }
        public int ListCount { get; private set; }
        public int DiscoveryResourceCount { get; set; } = 1;
        public string? ListContinuation { get; set; }
        public string? LastContinueToken { get; private set; }
        public bool ReplaceUid { get; set; }
        public bool Mutable { get; set; }
        public bool ReplaceVersion { get; set; }
        public bool FailCommit { get; set; }
        public KubernetesSessionFeatures Features => KubernetesSessionFeatures.Mutations;
        public List<KubernetesMutationRequest> Mutations { get; } = [];
        private string ResourceKind => Mutable ? "Deployment" : "Secret";
        private KubernetesResourceReference Reference => Mutable
            ? new("apps", "v1", "deployments", "default", "fixture", "original", "1")
            : new("", "v1", "secrets", "default", "fixture", "original", "1");
        public ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken)
        {
            DiscoverCount++;
            return ValueTask.FromResult(new KubernetesDiscovery([.. Enumerable.Range(0, DiscoveryResourceCount).Select(_ => new KubernetesApiResource(Reference.Group, "v1", Reference.Resource, ResourceKind, true, ["list", "get", "patch", "delete"]))], []));
        }
        public ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken)
        {
            ListCount++;
            LastContinueToken = request.ContinueToken;
            return ValueTask.FromResult(new KubernetesResourcePage([new(Reference, ResourceKind, "metadata", "{}")], "1",
                request.ContinueToken is null ? ListContinuation : null, false));
        }
        public ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new KubernetesResourceDocument(ReplaceUid ? Reference with { Uid = "replacement" } : ReplaceVersion ? Reference with { ResourceVersion = "2" } : Reference,
                ResourceKind, "metadata", Mutable ? "{\"spec\":{\"replicas\":1,\"template\":{}}}" : "sentinel-secret-value"));
        public ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken)
        {
            Mutations.Add(request);
            if (!request.DryRun && FailCommit) { throw new IOException("uncertain dispatch"); }
            return ValueTask.FromResult(new KubernetesMutationResult(request.DryRun ? KubernetesMutationOutcome.DryRun : KubernetesMutationOutcome.Applied, null));
        }
        public ValueTask<PanelSessionSnapshot> SnapshotAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PanelSessionSnapshot(SessionLifecycle.Active, SessionHealth.Healthy, false, "Ready"));
        public async IAsyncEnumerable<PanelSessionEvent> WatchAsync(long afterSequence, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
        public ValueTask<PanelCloseOutcome> CloseAsync(PanelCloseMode mode, CancellationToken cancellationToken) => ValueTask.FromResult(PanelCloseOutcome.GracefullyClosed);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class KubernetesAuthorizationConsumer(
        TimeProvider timeProvider,
        ClientId clientId) : IAgentAuthorizationConsumer
    {
        private readonly ConcurrentQueue<AgentActionCompletion> _completions = new();
        private AgentActionProposal? _action;
        private AgentAuthorizationSource _source;
        private AgentAuthorizationId _authorizationId;
        private int _consumed;

        public IReadOnlyList<AgentActionCompletion> Completions =>
            [.. _completions];

        public AgentAuthorizationId Arm(AgentKubernetesReadAction action) => Arm(action.Proposal);
        public AgentAuthorizationId Arm(AgentActionProposal action, AgentAuthorizationSource source = AgentAuthorizationSource.AutoPolicy)
        {
            _action = action;
            _source = source;
            _authorizationId = AgentAuthorizationId.New();
            Volatile.Write(ref _consumed, 0);
            return _authorizationId;
        }

        public ValueTask<AgentPermitResult> ConsumeAsync(
            AgentAuthorizationId authorizationId,
            AgentActionExecutionBinding currentBinding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = _action
                ?? throw new InvalidOperationException("An action must be armed.");
            var expected = AgentActionExecutionBinding.FromProposal(action);
            if (authorizationId != _authorizationId
                || !BindingsMatch(expected, currentBinding)
                || Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
            {
                return ValueTask.FromResult<AgentPermitResult>(
                    new AgentPermitResult.Denied(
                        new AgentAuthorizationError(
                            AgentAuthorizationErrorCode.AuthorizationMismatch,
                            "The Kubernetes execution binding changed.")));
            }

            Assert.True(BuiltInAgentTools.Catalog.TryGet(
                action.ToolName,
                out var tool));
            var now = timeProvider.GetUtcNow();
            return ValueTask.FromResult<AgentPermitResult>(
                new AgentPermitResult.Granted(
                    new AgentActionPermit(
                        new AgentActionAuthorization(
                            authorizationId,
                            action,
                            tool!,
                            _source,
                            clientId,
                            now.AddMinutes(1)),
                        now,
                        CancellationToken.None)));
        }

        public ValueTask<AgentAuthorizationError?> CompleteAsync(
            AgentActionPermit permit,
            AgentActionCompletion completion,
            CancellationToken cancellationToken)
        {
            _ = permit;
            cancellationToken.ThrowIfCancellationRequested();
            _completions.Enqueue(completion);
            return ValueTask.FromResult<AgentAuthorizationError?>(null);
        }

        private static bool BindingsMatch(
            AgentActionExecutionBinding left,
            AgentActionExecutionBinding right) =>
            left.ActionId == right.ActionId
            && left.RunId == right.RunId
            && left.ActorId == right.ActorId
            && string.Equals(left.ToolName, right.ToolName, StringComparison.Ordinal)
            && left.Target == right.Target
            && left.TargetIdentity == right.TargetIdentity
            && left.TargetFingerprint == right.TargetFingerprint
            && left.ArgumentDigest == right.ArgumentDigest
            && left.PolicyGeneration == right.PolicyGeneration;
    }

}
