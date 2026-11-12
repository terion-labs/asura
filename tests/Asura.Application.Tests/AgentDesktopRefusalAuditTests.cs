using System.Reflection;
using Asura.Core;

namespace Asura.Application.Tests;

/// <summary>
/// The self-operation guard refuses a desktop action before any authority is
/// requested (ADR 0061). The broker still has to put that refusal on the
/// audit trail as <c>denied</c>, close the action for good, and record
/// nothing that was read from the other application.
/// </summary>
public sealed class AgentDesktopRefusalAuditTests
{
    private const string ObservedApplication = "Zeta Ledger";
    private const string ObservedWindow = "Quarterly numbers";
    private const string ObservedBundleIdentifier = "com.example.zetaledger";
    private const string ObservedWindowIdentifier = "w-zeta-31";
    private const string ObservedElement = "Wire transfer";
    private const string TypedText = "pay vendor 7781";

    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefusalIsClaimedAndClosedAsDeniedWithTheHostsStableCode()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var proposal = TypeTextProposal();

        var error = await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied],
            audit.Events.Select(item => item.Outcome));
        Assert.All(audit.Events, item =>
        {
            Assert.Equal(DesktopAgentToolNames.TypeText, item.Action);
            Assert.Equal(proposal.Id.Value, item.CorrelationId);
            Assert.Equal(proposal.TargetFingerprint.Value, item.Target?.Id);
        });
        var requested = Assert.IsType<AuditDetails.AgentActionDetails>(audit.Events[0].Details);
        var denied = Assert.IsType<AuditDetails.AgentActionDetails>(audit.Events[1].Details);
        Assert.Null(requested.ResultCode);
        Assert.Equal("desktop_self_target", denied.ResultCode);
        Assert.Equal(AgentPolicyDecision.Denied, denied.Decision);

        // The guard runs before policy: the broker rejected nothing itself and
        // no approval or permission produced the outcome.
        Assert.Null(denied.ErrorCode);
        Assert.Null(denied.AuthorizationSource);
        AuditDetails.AgentActionDetails[] phases = [requested, denied];
        Assert.All(phases, details =>
        {
            Assert.Equal(proposal.RunId, details.RunId);
            Assert.Equal(AgentCapability.DesktopInteraction, details.Capability);
            Assert.Equal(AgentActionRisk.Mutation, details.Risk);
            Assert.Equal(AgentPermission.Ask, details.Permission);
            Assert.Equal(AgentPolicyDecision.Denied, details.Decision);
            Assert.Null(details.AuthorizationSource);
            Assert.Equal(proposal.ArgumentDigest, details.ArgumentDigest);
            Assert.Equal(proposal.TargetIdentity, details.Binding.TargetIdentity);
            Assert.Equal(proposal.PolicyGeneration, details.Binding.PolicyGeneration);
        });
    }

    [Fact]
    public async Task RefusedProposalCanNeverReachAnApprovalAfterwards()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var proposal = TypeTextProposal();
        Assert.Null(await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None));

        // Under Ask the same proposal would otherwise open an approval card.
        var requested = await broker.RequestAsync(proposal, CancellationToken.None);
        var refusedAgain = await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);

        Assert.Equal(
            AgentAuthorizationErrorCode.DuplicateAction,
            Assert.IsType<AgentAuthorizationResult.Denied>(requested).Error.Code);
        Assert.Equal(AgentAuthorizationErrorCode.DuplicateAction, refusedAgain?.Code);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.Denied],
            audit.Events.Select(item => item.Outcome));
    }

    [Fact]
    public async Task RefusedProposalIsNotAuthorizedByAConfirmedYoloPolicy()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(
            audit,
            AgentPermission.Yolo,
            new AgentYoloConfirmation(
                RunId(),
                Target(),
                policyGeneration: 1,
                Human(),
                Now,
                Now.AddMinutes(15)));

        // The confirmation is live: an action nobody refused is authorized
        // without a card.
        var unrefused = Assert.IsType<AgentAuthorizationResult.Authorized>(
            await broker.RequestAsync(TypeTextProposal(), CancellationToken.None));
        Assert.Equal(AgentAuthorizationSource.YoloPolicy, unrefused.Authorization.Source);

        var refused = TypeTextProposal();
        Assert.Null(await broker.RefuseAsync(
            refused,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None));
        var requested = await broker.RequestAsync(refused, CancellationToken.None);

        Assert.Equal(
            AgentAuthorizationErrorCode.DuplicateAction,
            Assert.IsType<AgentAuthorizationResult.Denied>(requested).Error.Code);
        Assert.DoesNotContain(
            audit.Events,
            item => string.Equals(item.CorrelationId, refused.Id.Value, StringComparison.Ordinal)
                && item.Outcome is not (AuditOutcome.Requested or AuditOutcome.Denied));
        Assert.DoesNotContain(
            audit.Events,
            item => string.Equals(item.CorrelationId, refused.Id.Value, StringComparison.Ordinal)
                && item.Details is AuditDetails.AgentActionDetails { AuthorizationSource: not null });
    }

    [Fact]
    public async Task RefusalForACancelledRunFailsClosedAndIsStillAudited()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        Assert.Null(await broker.CancelRunAsync(
            new AgentRunCancellation(RunId(), Human(), "user_stop", Now),
            CancellationToken.None));
        var proposal = TypeTextProposal();

        var error = await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);
        var requested = await broker.RequestAsync(proposal, CancellationToken.None);

        Assert.Equal(AgentAuthorizationErrorCode.RunCancelled, error?.Code);
        Assert.IsType<AgentAuthorizationResult.Denied>(requested);
        var trail = audit.Events
            .Where(item => string.Equals(item.CorrelationId, proposal.Id.Value, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied],
            trail.Take(2).Select(item => item.Outcome));
        Assert.All(trail, item => Assert.NotEqual(AuditOutcome.Started, item.Outcome));
    }

    [Theory]
    [InlineData("Desktop_Self_Target")]
    [InlineData("desktop self target")]
    [InlineData("desktop.self_target")]
    [InlineData("d\u00e9sktop_self_target")]
    [InlineData("desktop_self_target\n")]
    public async Task RefusalWithACodeTheAuditTrailRejectsLeavesNoTrail(string stableCode)
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var proposal = TypeTextProposal();

        var thrown = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await broker.RefuseAsync(proposal, stableCode, CancellationToken.None));

        Assert.Equal("stableCode", thrown.ParamName);
        Assert.Empty(audit.Events);

        // Nothing was claimed either: the action is still an ordinary request.
        Assert.IsType<AgentAuthorizationResult.ApprovalRequired>(
            await broker.RequestAsync(proposal, CancellationToken.None));
    }

    [Fact]
    public async Task RefusalCodeIsBoundedToTheAuditResultCodeLength()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);

        Assert.Null(await broker.RefuseAsync(
            TypeTextProposal(),
            new string('a', 128),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await broker.RefuseAsync(
                TypeTextProposal(),
                new string('a', 129),
                CancellationToken.None));

        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied],
            audit.Events.Select(item => item.Outcome));
    }

    [Fact]
    public async Task ActionThatAlreadyAskedForAuthorityCannotBeRefusedIntoASecondTrail()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var proposal = TypeTextProposal();
        Assert.IsType<AgentAuthorizationResult.ApprovalRequired>(
            await broker.RequestAsync(proposal, CancellationToken.None));

        var error = await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);

        Assert.Equal(AgentAuthorizationErrorCode.DuplicateAction, error?.Code);
        Assert.DoesNotContain(
            audit.Events,
            item => item.Details is AuditDetails.AgentActionDetails { ResultCode: not null });
    }

    [Fact]
    public async Task RefusalAuditHoldsNothingReadFromTheOtherApplication()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var proposal = TypeTextProposal();
        string[] observed =
        [
            ObservedApplication, ObservedBundleIdentifier, ObservedWindow, ObservedElement, TypedText,
        ];

        // The approval presentation does carry these; the audit trail must not.
        var shown = string.Join(
            '|',
            proposal.Presentation.Arguments
                .Select(argument => argument.DisplayValue)
                .Append(proposal.Presentation.Host));
        Assert.All(observed, text => Assert.Contains(text, shown, StringComparison.Ordinal));

        Assert.Null(await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None));
        Assert.IsType<AgentAuthorizationResult.Denied>(
            await broker.RequestAsync(proposal, CancellationToken.None));

        // The printed record includes its details, so the scan below covers
        // every recorded field.
        Assert.Contains(AgentDesktopStableCodes.SelfTarget, audit.Events[1].ToString(), StringComparison.Ordinal);
        Assert.All(audit.Events, item =>
            Assert.All([.. observed, ObservedWindowIdentifier], text =>
                Assert.DoesNotContain(text, item.ToString(), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task RefusalForAnUnknownRunOrToolFailsClosedAndIsStillAudited()
    {
        var audit = new RecordingAuditStore();
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var otherRun = TypeTextProposal(runId: "run-never-registered");
        var deferredTool = TypeTextProposal(toolName: "desktop.screenshot");

        var runError = await broker.RefuseAsync(
            otherRun,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);
        var toolError = await broker.RefuseAsync(
            deferredTool,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);

        Assert.Equal(AgentAuthorizationErrorCode.RunNotFound, runError?.Code);
        Assert.Equal(AgentAuthorizationErrorCode.UnknownTool, toolError?.Code);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied, AuditOutcome.Requested, AuditOutcome.Denied],
            audit.Events.Select(item => item.Outcome));
        Assert.All(audit.Events, item => Assert.Same(AuditDetails.None, item.Details));
    }

    [Theory]
    [InlineData(AuditOutcome.Requested)]
    [InlineData(AuditOutcome.Denied)]
    public async Task RefusalThatCouldNotBeRecordedIsReportedToTheHost(AuditOutcome failingPhase)
    {
        var storeDown = true;
        var audit = new RecordingAuditStore
        {
            Unavailable = item => storeDown && item.Outcome == failingPhase,
        };
        await using var broker = await RegisteredBrokerAsync(audit, AgentPermission.Ask);
        var proposal = TypeTextProposal();

        var error = await broker.RefuseAsync(
            proposal,
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);

        Assert.Equal(AgentAuthorizationErrorCode.AuditUnavailable, error?.Code);
        Assert.DoesNotContain(audit.Events, item => item.Outcome == failingPhase);

        // The host refused the action whatever the store said, so once the
        // store recovers the proposal still cannot open an approval card.
        storeDown = false;
        var requested = await broker.RequestAsync(proposal, CancellationToken.None);
        Assert.Equal(
            AgentAuthorizationErrorCode.DuplicateAction,
            Assert.IsType<AgentAuthorizationResult.Denied>(requested).Error.Code);
        Assert.DoesNotContain(
            audit.Events,
            item => string.Equals(item.CorrelationId, proposal.Id.Value, StringComparison.Ordinal)
                && item.Outcome is not (AuditOutcome.Requested or AuditOutcome.Denied));
    }

    [Fact]
    public async Task RefusalRequiresAProposalAndAStableCode()
    {
        await using var broker = await RegisteredBrokerAsync(new RecordingAuditStore(), AgentPermission.Ask);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await broker.RefuseAsync(null!, AgentDesktopStableCodes.SelfTarget, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await broker.RefuseAsync(TypeTextProposal(), null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await broker.RefuseAsync(TypeTextProposal(), " ", CancellationToken.None));
    }

    [Fact]
    public async Task ConsumerWithoutAnAuditTrailSaysTheRefusalWasNotRecorded()
    {
        IAgentAuthorizationConsumer consumer = new ConsumerWithoutAuditTrail();

        var error = await consumer.RefuseAsync(
            TypeTextProposal(),
            AgentDesktopStableCodes.SelfTarget,
            CancellationToken.None);

        Assert.Equal(AgentAuthorizationErrorCode.AuditUnavailable, error?.Code);
    }

    [Fact]
    public void EveryDesktopStableCodeIsAcceptedAsAnAuditResultCode()
    {
        var proposal = TypeTextProposal();
        var codes = typeof(AgentDesktopStableCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(codes);
        Assert.All(codes, code =>
        {
            var details = Assert.IsType<AuditDetails.AgentActionDetails>(AuditDetails.ForAgentAction(
                proposal.RunId,
                AgentCapability.DesktopInteraction,
                AgentActionRisk.Mutation,
                AgentPermission.Ask,
                AgentPolicyDecision.Denied,
                proposal.ArgumentDigest,
                resultCode: code));

            Assert.Equal(code, details.ResultCode);
        });
    }

    private static async Task<AgentCapabilityBroker> RegisteredBrokerAsync(
        RecordingAuditStore audit,
        AgentPermission desktopInteraction,
        AgentYoloConfirmation? yoloConfirmation = null)
    {
        var broker = new AgentCapabilityBroker(
            BuiltInAgentTools.Catalog,
            audit,
            new FixedTimeProvider(Now));
        var policy = AgentPolicy.Default with
        {
            Permissions = AgentPolicy.InitialPermissions.SetItem(
                AgentCapability.DesktopInteraction,
                desktopInteraction),
        };
        Assert.Null(await broker.RegisterRunAsync(
            new AgentRunRegistration(
                RunId(),
                Agent(),
                new ClientId("client-1"),
                Target(),
                policy,
                policyGeneration: 1,
                yoloConfirmation),
            CancellationToken.None));
        return broker;
    }

    private static AgentActionProposal TypeTextProposal(
        string runId = "desktop-run",
        string? toolName = null)
    {
        var context = Context();
        var prepared = new AgentDesktopActionComposer().Prepare(
            new AgentActionEnvelope(
                AgentActionId.New(),
                new AgentRunId(runId),
                Agent(),
                policyGeneration: 1,
                Now,
                Now.AddMinutes(1)),
            context,
            new AgentDesktopRequest.TypeText(
                new AgentDesktopElementTarget(
                    new AgentDesktopElementRef("@s8f3k2p9:e5"),
                    AgentDesktopDisplayText.FromUntrusted(ObservedApplication, "Application"),
                    new AgentDesktopApplicationIdentity(
                        AgentDesktopApplicationIdentityKind.BundleIdentifier,
                        ObservedBundleIdentifier),
                    4242,
                    "generation-7",
                    ObservedWindowIdentifier,
                    AgentDesktopDisplayText.FromUntrusted(ObservedWindow),
                    Now,
                    AgentDesktopDisplayText.FromUntrusted("text field", "element"),
                    AgentDesktopDisplayText.FromUntrusted(ObservedElement),
                    isSecure: false),
                new AgentDesktopInputText(TypedText))).Proposal;
        return toolName is null
            ? prepared
            : AgentActionProposal.FromContext(
                prepared.Id,
                prepared.RunId,
                prepared.Actor,
                toolName,
                context,
                prepared.ArgumentDigest,
                prepared.Presentation,
                prepared.PolicyGeneration,
                prepared.CreatedAtUtc,
                prepared.DeadlineUtc);
    }

    private static AgentContextSnapshot Context()
    {
        var tab = new TabInstanceId("desktop-tab");
        var panel = new PanelInstanceId("desktop-panel");
        var graph = new WorkspaceGraphSnapshot(
            Target().WindowId,
            new WorkspaceInstance(
                Target().WorkspaceId,
                "Workspace",
                [new TabInstance(
                    tab,
                    "Tab",
                    [new PanelInstance(panel, PanelKind.Statistics, "Panel")],
                    panel)],
                tab),
            revision: 3,
            lastSequence: 3);
        return new AgentContextSnapshot(
            Target(),
            [AgentContextPanel.ForGraphPanel(graph, tab, panel, null)],
            Now);
    }

    private static AgentRunId RunId() => new("desktop-run");

    private static AgentTarget.Workspace Target() =>
        new(new WindowInstanceId("desktop-window"), new WorkspaceInstanceId("desktop-workspace"));

    private static ActorDescriptor Agent() =>
        new(new ActorId("desktop-agent"), ActorKind.Agent, "Asura agent");

    private static ActorDescriptor Human() =>
        new(new ActorId("client-1"), ActorKind.Human, "Local user", new ClientId("client-1"));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ConsumerWithoutAuditTrail : IAgentAuthorizationConsumer
    {
        public ValueTask<AgentPermitResult> ConsumeAsync(
            AgentAuthorizationId authorizationId,
            AgentActionExecutionBinding currentBinding,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentAuthorizationError?> CompleteAsync(
            AgentActionPermit permit,
            AgentActionCompletion completion,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuditStore : IAuditStore
    {
        public List<AuditEventRecord> Events { get; } = [];

        public Func<AuditEventRecord, bool>? Unavailable { get; init; }

        public ValueTask<AuditStoreResult<Unit>> AppendAsync(
            AuditEventRecord auditEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Unavailable?.Invoke(auditEvent) == true)
            {
                return ValueTask.FromResult(AuditStoreResult<Unit>.Failure(
                    new AuditStoreError(AuditStoreErrorCode.StorageUnavailable, "Unavailable.")));
            }

            Events.Add(auditEvent);
            return ValueTask.FromResult(AuditStoreResult<Unit>.Success(Unit.Value));
        }

        public ValueTask<AuditStoreResult<IReadOnlyList<AuditEventRecord>>> ListByCorrelationAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(AuditStoreResult<IReadOnlyList<AuditEventRecord>>.Success(
                [.. Events.Where(item => string.Equals(item.CorrelationId, correlationId, StringComparison.Ordinal))]));
        }
    }
}
