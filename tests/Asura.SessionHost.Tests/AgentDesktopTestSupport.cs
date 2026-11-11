using System.Collections;
using System.Globalization;
using System.Reflection;
using Asura.Application;
using Asura.Core;
using Asura.SessionHost;

namespace Asura.SessionHost.Tests;

/// <summary>
/// A real broker, a real host and a recording engine around one registered
/// Workspace run, so desktop tests exercise the same authority path the
/// desktop process composes.
/// </summary>
internal sealed class DesktopHostFixture : IAsyncDisposable
{
    private DesktopHostFixture(IAgentAuthorizationConsumer? consumer)
    {
        Clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        Audit = new RecordingDesktopAuditStore();
        Broker = new AgentCapabilityBroker(BuiltInAgentTools.Catalog, Audit, Clock);
        Engine = new RecordingDesktopEngine();
        Composer = new AgentDesktopActionComposer();
        Host = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(),
            new DesktopLifecyclePolicy(),
            Clock,
            agentAuthorizationConsumer: consumer ?? Broker,
            agentDesktopActionComposer: Composer,
            agentDesktopAutomationEngine: Engine);
    }

    public static TimeSpan YoloConfirmationLifetime { get; } = TimeSpan.FromMinutes(15);

    public ManualTimeProvider Clock { get; }

    public RecordingDesktopAuditStore Audit { get; }

    public AgentCapabilityBroker Broker { get; }

    public RecordingDesktopEngine Engine { get; }

    public AgentDesktopActionComposer Composer { get; }

    public InMemorySessionHostClient Host { get; }

    public WindowInstanceId WindowId { get; } = new("desktop-window");

    public WorkspaceInstanceId WorkspaceId { get; } = new("desktop-workspace");

    public TabInstanceId FirstTabId { get; } = new("desktop-tab-one");

    public TabInstanceId SecondTabId { get; } = new("desktop-tab-two");

    public AgentRunId RunId { get; } = new("desktop-run");

    public ActorDescriptor Human { get; } = new(
        new ActorId("desktop-client"),
        ActorKind.Human,
        "Desktop user",
        new ClientId("desktop-client"));

    public ActorDescriptor Agent { get; } = new(
        new ActorId("desktop-agent"),
        ActorKind.Agent,
        "Desktop agent");

    public AgentTarget.Workspace Target => new(WindowId, WorkspaceId);

    /// <summary>
    /// Registers the run with the three desktop permissions given. A YOLO
    /// permission needs <paramref name="confirmYolo"/>: the broker does not
    /// register a run whose YOLO policy lacks a confirmation.
    /// </summary>
    public static async ValueTask<DesktopHostFixture> CreateAsync(
        AgentPermission data = AgentPermission.Ask,
        AgentPermission interaction = AgentPermission.Ask,
        AgentPermission control = AgentPermission.Ask,
        bool confirmYolo = false,
        IAgentAuthorizationConsumer? consumer = null)
    {
        var fixture = new DesktopHostFixture(consumer);
        var policy = AgentPolicy.Default with
        {
            Permissions = AgentPolicy.Default.Permissions
                .SetItem(AgentCapability.DesktopData, data)
                .SetItem(AgentCapability.DesktopInteraction, interaction)
                .SetItem(AgentCapability.DesktopControl, control),
        };
        await fixture.RegisterAsync(policy, confirmYolo);
        return fixture;
    }

    /// <summary>Registers the run with the shipped default policy, untouched.</summary>
    public static async ValueTask<DesktopHostFixture> CreateWithDefaultPolicyAsync()
    {
        var fixture = new DesktopHostFixture(consumer: null);
        await fixture.RegisterAsync(AgentPolicy.Default, confirmYolo: false);
        return fixture;
    }

    public async ValueTask<AgentDesktopAction> PrepareAsync(AgentDesktopRequest request)
    {
        var context = (await Host.InspectAgentContextAsync(
            new AgentContextRequest(Target),
            new OperationContext(RequestId.New(), Agent),
            default)).Value();
        return Composer.Prepare(
            new AgentActionEnvelope(
                AgentActionId.New(),
                RunId,
                Agent,
                policyGeneration: 1,
                Clock.GetUtcNow(),
                Clock.GetUtcNow().AddMinutes(1)),
            context,
            request);
    }

    /// <summary>Requests authority and approves the card when the policy asks for one.</summary>
    public async ValueTask<AgentActionAuthorization> AuthorizeAsync(AgentDesktopAction action)
    {
        var requested = await Broker.RequestAsync(action.Proposal, default);
        if (requested is AgentAuthorizationResult.ApprovalRequired approval)
        {
            requested = await Broker.DecideAsync(
                new AgentApprovalDecision(
                    approval.Approval.Id,
                    Human,
                    approved: true,
                    AgentApprovalDuration.Once,
                    Clock.GetUtcNow()),
                default);
        }

        return Assert.IsType<AgentAuthorizationResult.Authorized>(requested).Authorization;
    }

    public async ValueTask<(AgentDesktopAction Action, HostResult<AgentDesktopResult> Result)> RunAsync(
        AgentDesktopRequest request,
        CancellationToken cancellationToken = default)
    {
        var action = await PrepareAsync(request);
        var authorization = await AuthorizeAsync(action);
        var result = await Host.RunAgentDesktopActionAsync(
            authorization.Id,
            action,
            cancellationToken);
        return (action, result);
    }

    /// <summary>Changes the workspace graph, which changes the run's binding fingerprint.</summary>
    public async ValueTask ChangeWorkspaceGraphAsync()
    {
        var graph = (await Host.GetWorkspaceGraphAsync(
            WorkspaceId,
            new OperationContext(RequestId.New(), Human),
            default)).Value();
        _ = (await Host.ActivateWorkspaceTabAsync(
            new ActivateWorkspaceTabRequest(
                WorkspaceId,
                graph.Workspace.ActiveTabId == FirstTabId ? SecondTabId : FirstTabId),
            new OperationContext(RequestId.New(), Human, graph.Revision),
            default)).Value();
    }

    public IReadOnlyList<AuditEventRecord> Trail(AgentDesktopAction action) =>
    [
        .. Audit.Events.Where(item => string.Equals(
            item.CorrelationId,
            action.Proposal.Id.Value,
            StringComparison.Ordinal)),
    ];

    public IReadOnlyList<AuditOutcome> Outcomes(AgentDesktopAction action) =>
        [.. Trail(action).Select(item => item.Outcome)];

    public AuditDetails.AgentActionDetails LastDetails(AgentDesktopAction action) =>
        Assert.IsType<AuditDetails.AgentActionDetails>(Trail(action)[^1].Details);

    /// <summary>
    /// Holds the action's trail to the rules of the durable audit store and
    /// the run audit reader, which the recording store does not enforce: the
    /// phases form a legal transition sequence, and every phase names the same
    /// action identity and never contradicts a value an earlier phase gave. A
    /// trail that breaks them is stored but cannot be shown as one entry.
    /// </summary>
    public void AssertTrailIsOneReadableEntry(AgentDesktopAction action)
    {
        var trail = Trail(action);
        Assert.Equal(AuditOutcome.Requested, trail[0].Outcome);
        Assert.All(
            trail.Zip(trail.Skip(1)),
            step => Assert.True(
                CanFollow(step.First.Outcome, step.Second.Outcome),
                $"{step.First.Outcome} cannot be followed by {step.Second.Outcome}."));
        _ = Assert.Single(trail.Select(item => (item.Action, item.Target)).Distinct());
        AuditDetails.AgentActionDetails[] details =
        [
            .. trail.Select(item => Assert.IsType<AuditDetails.AgentActionDetails>(item.Details)),
        ];
        _ = Assert.Single(details
            .Select(item => (
                item.RunId,
                item.Capability,
                item.Risk,
                item.Permission,
                item.Decision,
                item.ArgumentDigest))
            .Distinct());
        AssertNeverContradicted(details.Select(item => item.AuthorizationSource?.ToString()));
        AssertNeverContradicted(details.Select(item => item.ErrorCode?.ToString()));
        AssertNeverContradicted(details.Select(item => item.ResultCode));
        AssertNeverContradicted(details.Select(item => item.Binding.TargetIdentity?.Value));
        AssertNeverContradicted(details.Select(item => Invariant(item.Binding.PolicyGeneration)));
        AssertNeverContradicted(details.Select(item => Invariant(item.Binding.ResultCount)));
        AssertNeverContradicted(
            details.Select(item => Invariant(item.Binding.ExecutionDurationMilliseconds)));
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Broker.DisposeAsync();
    }

    private static bool CanFollow(AuditOutcome current, AuditOutcome next) => current switch
    {
        AuditOutcome.Requested => next is AuditOutcome.Approved or AuditOutcome.Denied,
        AuditOutcome.Approved => next is AuditOutcome.Started or AuditOutcome.Denied,
        AuditOutcome.Started => next is AuditOutcome.Succeeded
            or AuditOutcome.Failed
            or AuditOutcome.Cancelled,
        _ => false,
    };

    private static void AssertNeverContradicted(IEnumerable<string?> values) =>
        Assert.True(
            values.Where(value => value is not null).Distinct(StringComparer.Ordinal).Count() <= 1,
            "Two audit phases of one action disagree on a recorded value.");

    private static string? Invariant<T>(T? value)
        where T : struct, IFormattable =>
        value?.ToString(format: null, CultureInfo.InvariantCulture);

    private async ValueTask RegisterAsync(AgentPolicy policy, bool confirmYolo)
    {
        var confirmation = confirmYolo
            ? new AgentYoloConfirmation(
                RunId,
                Target,
                policyGeneration: 1,
                Human,
                Clock.GetUtcNow(),
                Clock.GetUtcNow() + YoloConfirmationLifetime)
            : null;
        Assert.Null(await Broker.RegisterRunAsync(
            new AgentRunRegistration(
                RunId,
                Agent,
                Assert.IsType<ClientId>(Human.ClientId),
                Target,
                policy,
                policyGeneration: 1,
                confirmation),
            default));
        _ = (await Host.RegisterWorkspaceGraphAsync(
            new RegisterWorkspaceGraphRequest(WindowId, Workspace()),
            new OperationContext(RequestId.New(), Human),
            default)).Value();
    }

    private WorkspaceInstance Workspace()
    {
        var firstPanel = new PanelInstanceId("desktop-panel-one");
        var secondPanel = new PanelInstanceId("desktop-panel-two");
        return new WorkspaceInstance(
            WorkspaceId,
            "Desktop workspace",
            [
                new TabInstance(
                    FirstTabId,
                    "One",
                    [new PanelInstance(firstPanel, PanelKind.Statistics, "One")],
                    firstPanel),
                new TabInstance(
                    SecondTabId,
                    "Two",
                    [new PanelInstance(secondPanel, PanelKind.Statistics, "Two")],
                    secondPanel),
            ],
            FirstTabId);
    }
}

/// <summary>
/// The platform seam as tests see it. It records every call in order,
/// refuses the process identifiers it is told belong to Asura, and answers
/// one configurable result per execution.
/// </summary>
internal sealed class RecordingDesktopEngine : IAgentDesktopAutomationEngine
{
    private readonly object _gate = new();
    private readonly List<string> _calls = [];
    private readonly List<AgentDesktopRequest> _executions = [];
    private readonly List<AgentRunId> _releasedRuns = [];

    public AgentDesktopAccessState AccessState { get; set; } = AgentDesktopAccessState.Granted;

    /// <summary>Processes the guard classifies as Asura: the desktop process or one of its helpers.</summary>
    public HashSet<uint> AsuraProcessIds { get; } = [];

    /// <summary>Replaces the guard's answer; null keeps the process-identifier rule.</summary>
    public Func<AgentDesktopRequest, AgentDesktopFailure?>? Guard { get; set; }

    public Func<AgentDesktopRequest, CancellationToken, ValueTask<AgentDesktopExecutionResult>> Execute
    { get; set; } = (request, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
        new AgentDesktopExecutionResult.Succeeded(DesktopSamples.Success(request)));

    /// <summary>Every call in order, as <c>check:tool</c>, <c>execute:tool</c> or <c>release:run</c>.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<AgentDesktopRequest> Executions
    {
        get
        {
            lock (_gate)
            {
                return [.. _executions];
            }
        }
    }

    public IReadOnlyList<AgentRunId> ReleasedRuns
    {
        get
        {
            lock (_gate)
            {
                return [.. _releasedRuns];
            }
        }
    }

    public ValueTask<AgentDesktopAccessState> GetAccessStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AccessState);
    }

    /// <summary>
    /// Runs inside the guard after it has seen the caller's token, standing in
    /// for a revocation that arrives while the guard is still answering.
    /// </summary>
    public Func<ValueTask>? DuringCheck { get; set; }

    public async ValueTask<AgentDesktopFailure?> CheckTargetAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken)
    {
        _ = runId;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _calls.Add("check:" + request.ToolName);
        }

        if (DuringCheck is { } duringCheck)
        {
            await duringCheck();
        }

        return Guard is { } guard ? guard(request) : RefuseAsura(request);
    }

    public ValueTask<AgentDesktopExecutionResult> ExecuteAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken)
    {
        _ = runId;
        lock (_gate)
        {
            _calls.Add("execute:" + request.ToolName);
            _executions.Add(request);
        }

        return Execute(request, cancellationToken);
    }

    public void ReleaseRun(AgentRunId runId)
    {
        lock (_gate)
        {
            _calls.Add("release:" + runId.Value);
            _releasedRuns.Add(runId);
        }
    }

    private AgentDesktopFailure? RefuseAsura(AgentDesktopRequest request)
    {
        uint? processId = request switch
        {
            AgentDesktopRequest.ElementAction element => element.Target.ProcessId,
            AgentDesktopRequest.FocusWindow focus => focus.Window.ProcessId,
            _ => null,
        };
        return processId is { } value && AsuraProcessIds.Contains(value)
            ? new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget)
            : null;
    }
}

internal sealed class RecordingDesktopAuditStore : IAuditStore
{
    private readonly object _gate = new();
    private readonly List<AuditEventRecord> _events = [];

    public IReadOnlyList<AuditEventRecord> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>When set, every write fails as the store being unreachable.</summary>
    public bool Unavailable { get; set; }

    public ValueTask<AuditStoreResult<Unit>> AppendAsync(
        AuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Unavailable)
        {
            return ValueTask.FromResult(
                AuditStoreResult<Unit>.Failure(
                    new AuditStoreError(
                        AuditStoreErrorCode.StorageUnavailable,
                        "The audit store is unavailable.")));
        }

        lock (_gate)
        {
            _events.Add(auditEvent);
        }

        return ValueTask.FromResult(AuditStoreResult<Unit>.Success(Unit.Value));
    }

    public ValueTask<AuditStoreResult<IReadOnlyList<AuditEventRecord>>> ListByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AuditEventRecord> matches =
        [
            .. Events.Where(item => string.Equals(
                item.CorrelationId,
                correlationId,
                StringComparison.Ordinal)),
        ];
        return ValueTask.FromResult(
            AuditStoreResult<IReadOnlyList<AuditEventRecord>>.Success(matches));
    }
}

/// <summary>
/// One typed request and one plausible engine answer per desktop tool. The
/// <c>text</c> argument lands in every field that carries untrusted or
/// model-supplied text, which is how the secrecy test plants its canaries.
/// </summary>
internal static class DesktopSamples
{
    public const uint ExternalProcessId = 4_242;
    public const string SnapshotId = "s8f3k2p9";

    /// <summary>When the sample targets were observed; no test here reads the age.</summary>
    public static readonly DateTimeOffset ObservedAt = DateTimeOffset.UnixEpoch;

    public static IReadOnlyList<string> ToolNames { get; } =
    [
        DesktopAgentToolNames.ListApps,
        DesktopAgentToolNames.ListWindows,
        DesktopAgentToolNames.Snapshot,
        DesktopAgentToolNames.Wait,
        DesktopAgentToolNames.Click,
        DesktopAgentToolNames.SetValue,
        DesktopAgentToolNames.TypeText,
        DesktopAgentToolNames.Clear,
        DesktopAgentToolNames.Select,
        DesktopAgentToolNames.Toggle,
        DesktopAgentToolNames.Expand,
        DesktopAgentToolNames.Scroll,
        DesktopAgentToolNames.PressKey,
        DesktopAgentToolNames.FocusWindow,
        DesktopAgentToolNames.LaunchApp,
        DesktopAgentToolNames.CloseApp,
    ];

    public static AgentDesktopDisplayText Display(string text) =>
        AgentDesktopDisplayText.FromUntrusted(text)
        ?? throw new ArgumentException("Sample display text cannot be empty.", nameof(text));

    public static AgentDesktopElementTarget Element(
        uint processId = ExternalProcessId,
        string text = "Notes") =>
        new(
            new AgentDesktopElementRef($"@{SnapshotId}:e5"),
            Display(text),
            Identity(text),
            processId,
            text,
            text,
            Display(text),
            ObservedAt,
            Display(text),
            Display(text),
            isSecure: false);

    /// <summary>
    /// The identity the engine reports for a sample application. It is built
    /// from the same text as every other field so a leak test that plants a
    /// marker finds it here too.
    /// </summary>
    public static AgentDesktopApplicationIdentity Identity(string text) =>
        new(AgentDesktopApplicationIdentityKind.BundleIdentifier, text);

    public static AgentDesktopRequest Request(
        string toolName,
        uint processId = ExternalProcessId,
        string text = "Notes")
    {
        var application = new AgentDesktopApplicationId(text);
        var element = Element(processId, text);
        return toolName switch
        {
            DesktopAgentToolNames.ListApps => new AgentDesktopRequest.ListApps(),
            DesktopAgentToolNames.ListWindows => new AgentDesktopRequest.ListWindows(application),
            DesktopAgentToolNames.Snapshot => new AgentDesktopRequest.Snapshot(application),
            DesktopAgentToolNames.Wait => new AgentDesktopRequest.Wait(
                new AgentDesktopWaitCondition.Text(application, text),
                timeoutMilliseconds: 1_000),
            DesktopAgentToolNames.Click => new AgentDesktopRequest.Click(element),
            DesktopAgentToolNames.SetValue => new AgentDesktopRequest.SetValue(
                element,
                new AgentDesktopInputText(text)),
            DesktopAgentToolNames.TypeText => new AgentDesktopRequest.TypeText(
                element,
                new AgentDesktopInputText(text)),
            DesktopAgentToolNames.Clear => new AgentDesktopRequest.Clear(element),
            DesktopAgentToolNames.Select => new AgentDesktopRequest.Select(
                element,
                new AgentDesktopInputText(text)),
            DesktopAgentToolNames.Toggle => new AgentDesktopRequest.Toggle(element),
            DesktopAgentToolNames.Expand => new AgentDesktopRequest.Expand(element),
            DesktopAgentToolNames.Scroll => new AgentDesktopRequest.Scroll(
                element,
                AgentDesktopScrollDirection.Down,
                amount: 3),
            DesktopAgentToolNames.PressKey => new AgentDesktopRequest.PressKey(
                element,
                new AgentDesktopKeyPress("return")),
            DesktopAgentToolNames.FocusWindow => new AgentDesktopRequest.FocusWindow(
                new AgentDesktopWindowTarget(
                    application,
                    Identity(text),
                    text,
                    Display(text),
                    processId,
                    text,
                    ObservedAt)),
            DesktopAgentToolNames.LaunchApp => new AgentDesktopRequest.LaunchApp(application),
            DesktopAgentToolNames.CloseApp => new AgentDesktopRequest.CloseApp(
                new AgentDesktopApplicationTarget(
                    Display(text),
                    Identity(text),
                    processId,
                    text,
                    ObservedAt)),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName)),
        };
    }

    /// <summary>What the engine answers when the request did what it asked.</summary>
    public static AgentDesktopResult Success(AgentDesktopRequest request, string text = "Notes") =>
        request switch
        {
            AgentDesktopRequest.ListApps => new AgentDesktopResult.AppList(
                [new AgentDesktopApplicationEntry(Display(text), ExternalProcessId, Display(text))],
                truncated: false),
            AgentDesktopRequest.ListWindows => new AgentDesktopResult.WindowList(
                [
                    new AgentDesktopWindowEntry(
                        text,
                        Display(text),
                        Display(text),
                        Identity(text),
                        ExternalProcessId,
                        text,
                        IsFocused: true),
                ],
                truncated: false),
            AgentDesktopRequest.Snapshot => new AgentDesktopResult.Snapshot(
                SnapshotId,
                Display(text),
                Identity(text),
                ExternalProcessId,
                text,
                text,
                Display(text),
                complete: true,
                identityUnproven: false,
                [
                    new AgentDesktopSnapshotNode(
                        depth: 0,
                        Display(text),
                        Display(text),
                        Display(text),
                        new AgentDesktopElementRef($"@{SnapshotId}:e5"),
                        [text],
                        subtreeTruncated: false),
                ],
                nodesTruncated: false,
                refCount: 1),
            AgentDesktopRequest.Wait => new AgentDesktopResult.WaitCompleted(
                Satisfied: true,
                ElapsedMilliseconds: 10),
            AgentDesktopRequest.ElementAction => Delivered(
                AgentDesktopDelivery.DeliveredVerified,
                text),
            AgentDesktopRequest.FocusWindow => Delivered(
                AgentDesktopDelivery.DeliveredUnverified,
                text),
            AgentDesktopRequest.LaunchApp => new AgentDesktopResult.AppLaunched(
                ExternalProcessId,
                text,
                Display(text)),
            AgentDesktopRequest.CloseApp => new AgentDesktopResult.AppCloseRequested(
                AgentDesktopDelivery.DeliveredUnverified),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    public static AgentDesktopResult.ActionDelivered Delivered(
        AgentDesktopDelivery delivery,
        string text = "Notes") =>
        new(delivery, Display(text), Display(text), [text]);

    public static ValueTask<AgentDesktopExecutionResult> Failed(
        AgentDesktopFailureCode code,
        AgentDesktopDelivery delivery = AgentDesktopDelivery.NotDelivered) =>
        ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Failed(new AgentDesktopFailure(code, delivery)));
}

/// <summary>
/// Flattens an object graph into every string, number and enum name it can
/// reach through public properties. Record <c>ToString</c> would do most of
/// this, but a custom override anywhere in the graph could hide a field; the
/// secrecy test must not depend on that.
/// </summary>
internal static class DesktopAuditText
{
    private const int MaximumDepth = 12;

    public static string Flatten(object? value) =>
        string.Join('\n', Leaves(value, depth: 0).Select(Format));

    /// <summary>
    /// Every string the graph holds, without the numbers and enum names
    /// <see cref="Flatten"/> adds: the text fields a leak could come from.
    /// </summary>
    public static IReadOnlyList<string> Strings(object? value) =>
        [.. Leaves(value, depth: 0).OfType<string>()];

    private static string Format(object leaf) => leaf switch
    {
        string text => text,
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(leaf, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static IEnumerable<object> Leaves(object? value, int depth)
    {
        if (value is null || depth > MaximumDepth)
        {
            yield break;
        }

        if (value is string or Enum or bool or DateTimeOffset or TimeSpan or Guid
            || value.GetType().IsPrimitive)
        {
            yield return value;
            yield break;
        }

        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                foreach (var leaf in Leaves(item, depth + 1))
                {
                    yield return leaf;
                }
            }

            yield break;
        }

        foreach (var property in value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0))
        {
            foreach (var leaf in Leaves(property.GetValue(value), depth + 1))
            {
                yield return leaf;
            }
        }
    }
}
