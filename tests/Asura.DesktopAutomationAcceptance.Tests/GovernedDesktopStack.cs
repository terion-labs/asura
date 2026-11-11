using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;
using Asura.SessionHost;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// The objects <c>src/Asura.Desktop/DesktopComposition.cs</c> composes for
/// the desktop family, composed by hand because that composition also starts
/// Avalonia and CEF: a SQLite audit store in a database file of its own, the
/// real capability broker over it, the real <see cref="InMemorySessionHostClient"/>
/// with the real <see cref="AgentDesktopActionComposer"/>, and a real native
/// engine behind a recording pass-through. One workspace is registered, the
/// only target the family is offered for.
/// </summary>
internal sealed class GovernedDesktopStack : IAsyncDisposable
{
    public const string ProviderProfile = "evidence-provider";
    public const string ProviderModel = "evidence-model";

    private static readonly WindowInstanceId WindowId = new("evidence-window");
    private static readonly WorkspaceInstanceId WorkspaceId = new("evidence-workspace");
    private static readonly TabInstanceId TabId = new("evidence-tab");
    private static readonly PanelInstanceId PanelId = new("evidence-panel");

    private readonly List<GovernedAgentRuntime> _runtimes = [];

    private GovernedDesktopStack(IAgentDesktopAutomationEngine engine)
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "asura-desktop-evidence-audit",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, "audit.db");
        Database = new AsuraDatabase(new SqliteStorageOptions(DatabasePath), TimeProvider.System);
        Broker = new AgentCapabilityBroker(
            BuiltInAgentTools.Catalog,
            new SqliteAuditStore(Database),
            TimeProvider.System);
        Engine = new RecordingDesktopEngine(engine);
        Composer = new AgentDesktopActionComposer();
        Host = new InMemorySessionHostClient(
            new RefusingTerminalSessionFactory(),
            new DesktopLifecyclePolicy(),
            TimeProvider.System,
            agentAuthorizationConsumer: Broker,
            agentDesktopActionComposer: Composer,
            agentDesktopAutomationEngine: Engine);
    }

    public static AgentTarget.Workspace Target { get; } = new(WindowId, WorkspaceId);

    public static ActorDescriptor Human { get; } = new(
        new ActorId("evidence-client"),
        ActorKind.Human,
        "Evidence user",
        new ClientId("evidence-client"));

    public static ActorDescriptor Agent { get; } = new(
        new ActorId("evidence-agent"),
        ActorKind.Agent,
        "Evidence agent");

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public AsuraDatabase Database { get; }

    public AgentCapabilityBroker Broker { get; }

    public RecordingDesktopEngine Engine { get; }

    public AgentDesktopActionComposer Composer { get; }

    public InMemorySessionHostClient Host { get; }

    public static async Task<GovernedDesktopStack> CreateAsync(EvidenceFlavor flavor)
    {
        var stack = new GovernedDesktopStack(EvidenceLibraries.Engine(flavor));
        var registered = await stack.Host.RegisterWorkspaceGraphAsync(
            new RegisterWorkspaceGraphRequest(
                WindowId,
                new WorkspaceInstance(
                    WorkspaceId,
                    "Evidence workspace",
                    [
                        new TabInstance(
                            TabId,
                            "Evidence",
                            [new PanelInstance(PanelId, PanelKind.Statistics, "Statistics")],
                            PanelId),
                    ],
                    TabId)),
            new OperationContext(RequestId.New(), Human),
            CancellationToken.None);
        Assert.IsType<HostResult<WorkspaceGraphSnapshot>.Success>(registered);
        return stack;
    }

    public static AgentPolicy Policy(AgentPermission data, AgentPermission interaction, AgentPermission control) =>
        AgentPolicy.Default with
        {
            Permissions = AgentPolicy.Default.Permissions
                .SetItem(AgentCapability.DesktopData, data)
                .SetItem(AgentCapability.DesktopInteraction, interaction)
                .SetItem(AgentCapability.DesktopControl, control),
        };

    /// <summary>
    /// The governed runtime as <c>DesktopAgentWorkspaceRuntimeFactory</c>
    /// builds it, with every host role filled by the real session host.
    /// </summary>
    /// <param name="grantedAnswers">
    /// How many access queries <see cref="GrantReportingDesktopHost"/>
    /// answers with granted before the real answer; zero uses the real host
    /// unwrapped. Only for evidence on a host without the Accessibility grant.
    /// </param>
    public GovernedAgentRuntime CreateRuntime(ScriptedDesktopProvider provider, AgentPolicy policy, int grantedAnswers)
    {
        IAgentDesktopSessionHost desktopHost = grantedAnswers > 0
            ? new GrantReportingDesktopHost(Host, grantedAnswers)
            : Host;
        var runtime = new GovernedAgentRuntime(
            Host,
            Broker,
            Host,
            Host,
            Host,
            Host,
            new AgentTerminalActionComposer(),
            new AgentBrowserActionComposer(),
            new AgentFileActionComposer(),
            new AgentPanelActionComposer(),
            BuiltInAgentTools.Catalog,
            new FixedProviderResolver(provider),
            new EvidenceApprovalPrincipal(),
            TimeProvider.System,
            AgentPolicyResolver.Resolve(policy),
            agentDesktopHost: desktopHost,
            desktopComposer: Composer);
        _runtimes.Add(runtime);
        return runtime;
    }

    public static GovernedAgentPrompt Prompt(string message, AgentPolicy policy) =>
        new(
            new AiProviderProfileId(ProviderProfile),
            message,
            Target,
            AgentPolicyResolver.Resolve(policy).SelectPrimaryModel(ProviderProfile, ProviderModel));

    /// <summary>
    /// Registers a run directly with the broker, for evidence that hands the
    /// host a proposal the runtime could not build, such as a target no
    /// snapshot produced.
    /// </summary>
    public async Task<HostLevelRun> RegisterRunAsync(AgentPolicy policy, bool confirmYolo = false)
    {
        var runId = AgentRunId.New();
        var now = DateTimeOffset.UtcNow;
        var confirmation = confirmYolo
            ? new AgentYoloConfirmation(runId, Target, policyGeneration: 1, Human, now, now.AddMinutes(15))
            : null;
        Assert.Null(await Broker.RegisterRunAsync(
            new AgentRunRegistration(
                runId,
                Agent,
                Assert.IsType<ClientId>(Human.ClientId),
                Target,
                policy,
                policyGeneration: 1,
                confirmation),
            CancellationToken.None));
        return new HostLevelRun(this, runId);
    }

    /// <summary>Every desktop action the durable audit store holds for the run, read through the real reader.</summary>
    public async Task<IReadOnlyList<AgentRunAuditActionEntry>> ReadDesktopAuditAsync(AgentRunId runId)
    {
        var page = await new SqliteAgentRunAuditReader(Database).ReadAsync(
            new AgentRunAuditQuery(runId, pageSize: AgentRunAuditQuery.MaximumPageSize),
            CancellationToken.None);
        Assert.True(page.IsSuccess, page.Error?.Message);
        return
        [
            .. page.Value!.Entries
                .OfType<AgentRunAuditActionEntry>()
                .Where(entry => DesktopAgentToolNames.Owns(entry.ToolName))
                .Reverse(),
        ];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var runtime in _runtimes)
        {
            await runtime.DisposeAsync();
        }

        await Host.DisposeAsync();
        await Broker.DisposeAsync();
        await Database.DisposeAsync();
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still be closing a file; the directory is temporary.
        }
    }

    private sealed class RefusingTerminalSessionFactory : ITerminalSessionFactory
    {
        public CapabilitySet Capabilities => CapabilitySet.Empty;

        public ValueTask<ITerminalPanelSession> CreateAsync(
            SessionId sessionId,
            TerminalLaunchRequest launch,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The desktop evidence workspace has no terminals.");
    }

    private sealed class FixedProviderResolver(ScriptedDesktopProvider provider) : IAgentProviderResolver
    {
        // The title route names another profile; refusing it keeps every
        // scripted round a conversation round.
        public IAgentProviderBinding PinProvider(AiProviderProfileId profileId) =>
            profileId == new AiProviderProfileId(ProviderProfile)
                ? new FixedProviderBinding(provider)
                : throw new InvalidOperationException("Only the evidence provider profile is configured.");
    }

    private sealed class FixedProviderBinding(ScriptedDesktopProvider provider) : IAgentProviderBinding
    {
        public AiProviderProfileId ProfileId => new(ProviderProfile);

        public long Revision => 1;

        public string DefaultModel => ProviderModel;

        public bool IsCurrent => true;

        public Agent.IAgentProvider CreateProvider(string model) => provider;
    }

    private sealed class EvidenceApprovalPrincipal : IAgentApprovalPrincipal
    {
        public ActorDescriptor Actor => Human;
    }
}

/// <summary>
/// A run registered with the broker, driven the way
/// <c>GovernedAgentRuntime.ExecuteDesktopProposalAsync</c> drives a desktop
/// call: guard before authority, a card when the broker asks for one, and
/// one host dispatch for an authorization.
/// </summary>
internal sealed class HostLevelRun(GovernedDesktopStack stack, AgentRunId runId)
{
    public AgentRunId RunId { get; } = runId;

    public async Task<AgentDesktopAction> PrepareAsync(AgentDesktopRequest request)
    {
        var inspected = await stack.Host.InspectAgentContextAsync(
            new AgentContextRequest(GovernedDesktopStack.Target),
            new OperationContext(RequestId.New(), GovernedDesktopStack.Agent),
            CancellationToken.None);
        var context = Assert.IsType<HostResult<AgentContextSnapshot>.Success>(inspected).Value;
        var now = DateTimeOffset.UtcNow;
        return stack.Composer.Prepare(
            new AgentActionEnvelope(AgentActionId.New(), RunId, GovernedDesktopStack.Agent, 1, now, now.AddMinutes(2)),
            context,
            request);
    }

    /// <summary>
    /// Proposes one request. <paramref name="approve"/> answers a card if one
    /// is raised. <paramref name="skipGuard"/> leaves out the pre-authority
    /// guard, as a faulty runtime would, to reach the guard the host repeats.
    /// </summary>
    public async Task<HostLevelOutcome> ProposeAsync(
        AgentDesktopRequest request,
        bool approve,
        bool skipGuard = false)
    {
        var action = await PrepareAsync(request);
        if (!skipGuard
            && await stack.Host.CheckAgentDesktopTargetAsync(action, CancellationToken.None) is { } refusal)
        {
            return new HostLevelOutcome(action, refusal, CardRaised: false, Authorization: null, Dispatch: null);
        }

        var cardRaised = false;
        var authorization = await stack.Broker.RequestAsync(action.Proposal, CancellationToken.None);
        if (authorization is AgentAuthorizationResult.ApprovalRequired required)
        {
            cardRaised = true;
            authorization = await stack.Broker.DecideAsync(
                new AgentApprovalDecision(
                    required.Approval.Id,
                    GovernedDesktopStack.Human,
                    approve,
                    AgentApprovalDuration.Once,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        if (authorization is not AgentAuthorizationResult.Authorized authorized)
        {
            return new HostLevelOutcome(action, Refusal: null, cardRaised, authorization, Dispatch: null);
        }

        var dispatch = await stack.Host.RunAgentDesktopActionAsync(
            authorized.Authorization.Id,
            action,
            CancellationToken.None);
        return new HostLevelOutcome(action, Refusal: null, cardRaised, authorization, dispatch);
    }
}

internal sealed record HostLevelOutcome(
    AgentDesktopAction Action,
    AgentDesktopFailure? Refusal,
    bool CardRaised,
    AgentAuthorizationResult? Authorization,
    HostResult<AgentDesktopResult>? Dispatch)
{
    public string? DispatchStableCode =>
        (Dispatch as HostResult<AgentDesktopResult>.Failure)?.Error.StableCode;
}
