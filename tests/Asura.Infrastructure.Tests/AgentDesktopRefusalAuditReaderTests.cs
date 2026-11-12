using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

/// <summary>
/// A refusal made before authority (ADR 0061) is a two-phase trail: requested,
/// then denied with the host's stable result code and no authorization error.
/// The run audit reader accepts an action only when every phase carries the
/// same decision, so both phases must say denied or the whole run's timeline
/// becomes unreadable.
/// </summary>
public sealed class AgentDesktopRefusalAuditReaderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BrokerRefusalIsStoredAndReadBackAsOneDeniedAction()
    {
        await using var temporary = TemporaryDatabase.Create();
        var proposal = Proposal("action-refused");
        await using (var broker = new AgentCapabilityBroker(
            BuiltInAgentTools.Catalog,
            new SqliteAuditStore(temporary.Database),
            new FixedTimeProvider(Now)))
        {
            Assert.Null(await broker.RegisterRunAsync(Registration(), CancellationToken.None));
            Assert.Null(await broker.RefuseAsync(
                proposal,
                AgentDesktopStableCodes.SelfTarget,
                CancellationToken.None));
        }

        var page = await new SqliteAgentRunAuditReader(temporary.Database).ReadAsync(
            new AgentRunAuditQuery(RunId()),
            CancellationToken.None);

        Assert.True(page.IsSuccess, page.Error?.Message);
        var entry = Assert.IsType<AgentRunAuditActionEntry>(Assert.Single(page.Value!.Entries));
        Assert.Equal(DesktopAgentToolNames.TypeText, entry.ToolName);
        Assert.Equal(AgentCapability.DesktopInteraction, entry.Capability);
        Assert.Equal(AgentActionRisk.Mutation, entry.Risk);
        Assert.Equal(AgentPermission.Ask, entry.Permission);
        Assert.Equal(AgentPolicyDecision.Denied, entry.Decision);
        Assert.Equal(AuditOutcome.Denied, entry.LatestOutcome);
        Assert.Equal("desktop_self_target", entry.ResultCode);
        Assert.Null(entry.ErrorCode);
        Assert.Null(entry.AuthorizationSource);
        Assert.Equal(proposal.TargetIdentity, entry.TargetIdentity);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied],
            entry.Phases.Select(phase => phase.Outcome));
    }

    [Fact]
    public async Task RefusalTrailShapeRoundTripsThroughTheStore()
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAuditStore(temporary.Database);
        var requested = RefusalPhase(
            "action-shape",
            AuditOutcome.Requested,
            AgentPolicyDecision.Denied,
            resultCode: null);
        Assert.Equal(
            AgentActionAuditClaimOutcome.Claimed,
            Success(await store.ClaimAgentActionAsync(requested, CancellationToken.None)));
        Assert.True((await store.AppendAgentActionPhaseAsync(
            RefusalPhase(
                "action-shape",
                AuditOutcome.Denied,
                AgentPolicyDecision.Denied,
                AgentDesktopStableCodes.SelfTarget),
            CancellationToken.None)).IsSuccess);

        var page = Success(await new SqliteAgentRunAuditReader(temporary.Database).ReadAsync(
            new AgentRunAuditQuery(RunId()),
            CancellationToken.None));

        var entry = Assert.IsType<AgentRunAuditActionEntry>(Assert.Single(page.Entries));
        Assert.Equal(AgentPolicyDecision.Denied, entry.Decision);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, entry.ResultCode);
        Assert.Null(entry.ErrorCode);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Denied],
            entry.Phases.Select(phase => phase.Outcome));
    }

    [Theory]
    [InlineData(AgentPolicyDecision.RequiresApproval)]
    [InlineData(AgentPolicyDecision.AuthorizedByAuto)]
    [InlineData(AgentPolicyDecision.AuthorizedByYolo)]
    public async Task RefusalWhosePhasesDisagreeOnTheDecisionFailsTheTimelineClosed(
        AgentPolicyDecision requestedDecision)
    {
        await using var temporary = TemporaryDatabase.Create();
        var store = new SqliteAuditStore(temporary.Database);
        Assert.Equal(
            AgentActionAuditClaimOutcome.Claimed,
            Success(await store.ClaimAgentActionAsync(
                RefusalPhase(
                    "action-disagrees",
                    AuditOutcome.Requested,
                    requestedDecision,
                    resultCode: null),
                CancellationToken.None)));
        Assert.True((await store.AppendAsync(
            RefusalPhase(
                "action-disagrees",
                AuditOutcome.Denied,
                AgentPolicyDecision.Denied,
                AgentDesktopStableCodes.SelfTarget),
            CancellationToken.None)).IsSuccess);

        var page = await new SqliteAgentRunAuditReader(temporary.Database).ReadAsync(
            new AgentRunAuditQuery(RunId()),
            CancellationToken.None);

        Assert.False(page.IsSuccess);
        Assert.Equal(AuditStoreErrorCode.StorageFailure, page.Error!.Code);
        Assert.Null(page.Value);
    }

    private static AuditEventRecord RefusalPhase(
        string actionId,
        AuditOutcome outcome,
        AgentPolicyDecision decision,
        string? resultCode) =>
        new(
            $"{actionId}:{outcome}",
            actionId,
            Agent(),
            DesktopAgentToolNames.TypeText,
            new AuditTarget(
                "agent-target-fingerprint",
                AgentActionDigest.FromUtf8($"fingerprint:{actionId}").Value),
            outcome,
            AuditDetails.ForAgentAction(
                RunId(),
                AgentCapability.DesktopInteraction,
                AgentActionRisk.Mutation,
                AgentPermission.Ask,
                decision,
                AgentActionDigest.FromUtf8($"arguments:{actionId}"),
                authorizationSource: null,
                errorCode: null,
                resultCode,
                new AgentActionAuditBinding(
                    policyGeneration: 1,
                    targetIdentity: AgentActionDigest.FromUtf8($"target:{actionId}"))),
            outcome == AuditOutcome.Requested ? Now : Now.AddSeconds(1));

    private static AgentRunRegistration Registration() =>
        new(
            RunId(),
            Agent(),
            new ClientId("client-1"),
            Target(),
            AgentPolicy.Default with
            {
                Permissions = AgentPolicy.InitialPermissions.SetItem(
                    AgentCapability.DesktopInteraction,
                    AgentPermission.Ask),
            },
            policyGeneration: 1);

    private static AgentActionProposal Proposal(string actionId) =>
        new(
            new AgentActionId(actionId),
            RunId(),
            Agent(),
            DesktopAgentToolNames.TypeText,
            Target(),
            AgentActionDigest.FromUtf8("workspace-revision-7"),
            AgentActionDigest.FromUtf8(actionId),
            new AgentApprovalPresentation("Workspace", "localhost", null),
            policyGeneration: 1,
            Now,
            Now.AddMinutes(1));

    private static AgentRunId RunId() => new("desktop-run");

    private static AgentTarget.Workspace Target() =>
        new(new WindowInstanceId("window-1"), new WorkspaceInstanceId("workspace-1"));

    private static ActorDescriptor Agent() =>
        new(new ActorId("agent-1"), ActorKind.Agent, "Asura agent");

    private static T Success<T>(AuditStoreResult<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
