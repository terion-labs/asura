using Asura.Application;
using Asura.Core;
using Asura.SecurityCampaign.Tests;

namespace Asura.SessionHost.Tests;

/// <summary>
/// The desktop policy gate of ADR 0061: Off authorizes nothing, Auto
/// authorizes observation only, and input to another application needs an
/// exact human approval or a confirmed run-local YOLO policy. The broker's
/// generic risk rules already give this; these tests pin it for the desktop
/// catalog entries and pin the host's independent check behind it.
/// </summary>
public sealed class AgentDesktopPolicyGateTests
{
    public static TheoryData<string> AllTools => [.. DesktopSamples.ToolNames];

    public static TheoryData<string> InputTools =>
    [
        .. DesktopSamples.ToolNames.Where(name => Capability(name) != AgentCapability.DesktopData),
    ];

    public static TheoryData<string> ObservationTools =>
    [
        .. DesktopSamples.ToolNames.Where(name => Capability(name) == AgentCapability.DesktopData),
    ];

    [Fact]
    public void DefaultPolicyTurnsEveryDesktopCapabilityOff()
    {
        Assert.All(
            AgentPolicy.DesktopCapabilities,
            capability => Assert.Equal(
                AgentPermission.Off,
                AgentPolicy.Default.GetPermission(capability)));
    }

    [Fact]
    public void CatalogRisksKeepAutoAwayFromInput()
    {
        foreach (var toolName in DesktopSamples.ToolNames)
        {
            var descriptor = Descriptor(toolName);
            var auto = AgentPolicyResolver.Evaluate(AgentPermission.Auto, descriptor.Risk);
            Assert.Equal(
                descriptor.Capability == AgentCapability.DesktopData
                    ? AgentPolicyDecision.AuthorizedByAuto
                    : AgentPolicyDecision.RequiresApproval,
                auto);
            Assert.Equal(
                AgentPolicyDecision.Denied,
                AgentPolicyResolver.Evaluate(AgentPermission.Off, descriptor.Risk));
        }
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task OffAuthorizesNothing(string toolName)
    {
        await using var fixture = await DesktopHostFixture.CreateWithDefaultPolicyAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        var requested = await fixture.Broker.RequestAsync(action.Proposal, default);
        var run = await fixture.Host.RunAgentDesktopActionAsync(
            AgentAuthorizationId.New(),
            action,
            default);

        var denied = Assert.IsType<AgentAuthorizationResult.Denied>(requested);
        Assert.Equal(AgentAuthorizationErrorCode.PolicyDenied, denied.Error.Code);
        Assert.Equal("invalid_request", run.Error().StableCode);
        Assert.False(run.Error().Retryable);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal([AuditOutcome.Requested, AuditOutcome.Denied], fixture.Outcomes(action));
    }

    [Theory]
    [MemberData(nameof(ObservationTools))]
    public async Task AutoAuthorizesObservation(string toolName)
    {
        await using var fixture = await AutoFixtureAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        var authorized = Assert.IsType<AgentAuthorizationResult.Authorized>(
            await fixture.Broker.RequestAsync(action.Proposal, default));
        var result = await fixture.Host.RunAgentDesktopActionAsync(
            authorized.Authorization.Id,
            action,
            default);

        Assert.Equal(AgentAuthorizationSource.AutoPolicy, authorized.Authorization.Source);
        Assert.IsType<HostResult<AgentDesktopResult>.Success>(result);
        Assert.Single(fixture.Engine.Executions);
    }

    [Theory]
    [MemberData(nameof(InputTools))]
    public async Task AutoEscalatesInputToAnExactHumanApproval(string toolName)
    {
        await using var fixture = await AutoFixtureAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        var requested = await fixture.Broker.RequestAsync(action.Proposal, default);

        var approval = Assert.IsType<AgentAuthorizationResult.ApprovalRequired>(requested);
        Assert.Equal(AgentPermission.Auto, approval.Approval.Permission);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal([AuditOutcome.Requested], fixture.Outcomes(action));
    }

    [Theory]
    [InlineData(AgentCapability.DesktopData)]
    [InlineData(AgentCapability.DesktopInteraction)]
    [InlineData(AgentCapability.DesktopControl)]
    public async Task UnconfirmedYoloRunIsNeverRegistered(AgentCapability capability)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        await using var broker = new AgentCapabilityBroker(
            BuiltInAgentTools.Catalog,
            new RecordingDesktopAuditStore(),
            clock);
        var policy = AgentPolicy.Default with
        {
            Permissions = AgentPolicy.Default.Permissions.SetItem(capability, AgentPermission.Yolo),
        };

        var error = await broker.RegisterRunAsync(
            new AgentRunRegistration(
                new AgentRunId("desktop-unconfirmed-run"),
                new ActorDescriptor(new ActorId("desktop-agent"), ActorKind.Agent, "Desktop agent"),
                new ClientId("desktop-client"),
                new AgentTarget.Workspace(
                    new WindowInstanceId("desktop-window"),
                    new WorkspaceInstanceId("desktop-workspace")),
                policy,
                policyGeneration: 1),
            default);

        Assert.Equal(AgentAuthorizationErrorCode.YoloConfirmationRequired, error?.Code);
    }

    [Theory]
    [MemberData(nameof(InputTools))]
    public async Task ExpiredYoloConfirmationAuthorizesNothing(string toolName)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync(
            interaction: AgentPermission.Yolo,
            control: AgentPermission.Yolo,
            confirmYolo: true);
        fixture.Clock.Advance(DesktopHostFixture.YoloConfirmationLifetime);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        var denied = Assert.IsType<AgentAuthorizationResult.Denied>(
            await fixture.Broker.RequestAsync(action.Proposal, default));

        Assert.Equal(AgentAuthorizationErrorCode.YoloConfirmationRequired, denied.Error.Code);
        Assert.Empty(fixture.Engine.Executions);
    }

    [Theory]
    [MemberData(nameof(InputTools))]
    public async Task ConfirmedYoloAuthorizesInput(string toolName)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync(
            interaction: AgentPermission.Yolo,
            control: AgentPermission.Yolo,
            confirmYolo: true);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        var authorized = Assert.IsType<AgentAuthorizationResult.Authorized>(
            await fixture.Broker.RequestAsync(action.Proposal, default));
        var result = await fixture.Host.RunAgentDesktopActionAsync(
            authorized.Authorization.Id,
            action,
            default);

        Assert.Equal(AgentAuthorizationSource.YoloPolicy, authorized.Authorization.Source);
        Assert.IsType<HostResult<AgentDesktopResult>.Success>(result);
        Assert.Single(fixture.Engine.Executions);
    }

    [Theory]
    [MemberData(nameof(InputTools))]
    public async Task HostRefusesAnAutoPermitForInput(string toolName)
    {
        var consumer = new ForgedPermitConsumer(AgentAuthorizationSource.AutoPolicy);
        await using var fixture = await DesktopHostFixture.CreateAsync(consumer: consumer);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(toolName));

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            consumer.Arm(action, fixture),
            action,
            default);

        Assert.Equal(HostErrorCode.InvalidRequest, result.Error().Code);
        Assert.Equal("invalid_request", result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Empty(fixture.Engine.Calls);
        var completion = Assert.Single(consumer.Completions);
        Assert.Equal(AgentActionOutcome.Failed, completion.Outcome);
        Assert.Equal("invalid_request", completion.StableCode);
    }

    [Theory]
    [InlineData(AgentAuthorizationSource.HumanApproval)]
    [InlineData(AgentAuthorizationSource.YoloPolicy)]
    public async Task HostAcceptsApprovedAndConfirmedPermitsForInput(AgentAuthorizationSource source)
    {
        var consumer = new ForgedPermitConsumer(source);
        await using var fixture = await DesktopHostFixture.CreateAsync(consumer: consumer);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.PressKey));

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            consumer.Arm(action, fixture),
            action,
            default);

        Assert.IsType<HostResult<AgentDesktopResult>.Success>(result);
        Assert.Single(fixture.Engine.Executions);
        Assert.Equal(AgentActionOutcome.Succeeded, Assert.Single(consumer.Completions).Outcome);
    }

    [Fact]
    public async Task AuditTrailNeverCarriesUntrustedDesktopText()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var planted = new List<string>();
        foreach (var canary in SecurityCampaignCanaries.Values)
        {
            fixture.Engine.Execute = (request, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
                new AgentDesktopExecutionResult.Succeeded(DesktopSamples.Success(request, canary)));
            foreach (var toolName in DesktopSamples.ToolNames)
            {
                var request = DesktopSamples.Request(toolName, text: canary);
                var (action, result) = await fixture.RunAsync(request);
                AssertEveryObservedFieldCarries(canary, ObservedTarget(request));
                AssertEveryObservedFieldCarries(canary, result.Value());
                planted.Add(DesktopAuditText.Flatten(action.Proposal.Presentation));
                planted.Add(DesktopAuditText.Flatten(result.Value()));
            }

            // Failures, an unknown outcome and a refusal before authority
            // travel different audit paths than a success.
            fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(AgentDesktopFailureCode.StaleRef);
            var failed = await fixture.RunAsync(
                DesktopSamples.Request(DesktopAgentToolNames.TypeText, text: canary));
            fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
                new AgentDesktopExecutionResult.Succeeded(
                    DesktopSamples.Delivered(AgentDesktopDelivery.Uncertain, canary)));
            var unknown = await fixture.RunAsync(
                DesktopSamples.Request(DesktopAgentToolNames.Select, text: canary));
            fixture.Engine.Execute = (_, _) => throw new InvalidOperationException(canary);
            var escaped = await fixture.RunAsync(
                DesktopSamples.Request(DesktopAgentToolNames.Clear, text: canary));
            var refused = await fixture.PrepareAsync(
                DesktopSamples.Request(
                    DesktopAgentToolNames.SetValue,
                    (uint)Environment.ProcessId,
                    canary));
            fixture.Engine.AsuraProcessIds.Add((uint)Environment.ProcessId);
            Assert.NotNull(await fixture.Host.CheckAgentDesktopTargetAsync(refused, default));
            fixture.Engine.AsuraProcessIds.Clear();

            Assert.DoesNotContain(canary, DesktopAuditText.Flatten(failed.Result.Error()), StringComparison.Ordinal);
            Assert.DoesNotContain(canary, DesktopAuditText.Flatten(unknown.Result.Error()), StringComparison.Ordinal);
            Assert.DoesNotContain(canary, DesktopAuditText.Flatten(escaped.Result.Error()), StringComparison.Ordinal);
            Assert.Equal(AuditOutcome.Denied, fixture.Outcomes(refused)[^1]);
        }

        // The canaries did reach every untrusted field the host handled.
        Assert.All(
            SecurityCampaignCanaries.Values,
            canary => Assert.Contains(planted, text => text.Contains(canary, StringComparison.Ordinal)));
        var trail = DesktopAuditText.Flatten(fixture.Audit.Events);
        Assert.All(
            SecurityCampaignCanaries.Values,
            canary => Assert.DoesNotContain(canary, trail, StringComparison.Ordinal));

        // And the flattened trail is not vacuous: it carries what ADR 0061
        // allows, namely the run, the action kind and the stable codes.
        Assert.Contains(fixture.RunId.Value, trail, StringComparison.Ordinal);
        Assert.Contains(DesktopAgentToolNames.TypeText, trail, StringComparison.Ordinal);
        Assert.Contains(AgentDesktopStableCodes.StaleRef, trail, StringComparison.Ordinal);
        Assert.Contains(AgentDesktopStableCodes.ActionOutcomeUnknown, trail, StringComparison.Ordinal);
        Assert.Contains(AgentDesktopStableCodes.SelfTarget, trail, StringComparison.Ordinal);
        Assert.Contains(nameof(AgentCapability.DesktopControl), trail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The target or result really carries the canary in every text field it
    /// has, so a field added later is covered or this fails. Snapshot ids and
    /// element refs are minted by the library in a fixed grammar and are not
    /// application text.
    /// </summary>
    private static void AssertEveryObservedFieldCarries(string canary, object? observed)
    {
        var fields = DesktopAuditText.Strings(observed)
            .Where(text => !text.Contains(DesktopSamples.SnapshotId, StringComparison.Ordinal))
            .ToArray();
        Assert.All(fields, text => Assert.Contains(canary, text, StringComparison.Ordinal));
    }

    private static object? ObservedTarget(AgentDesktopRequest request) => request switch
    {
        AgentDesktopRequest.ElementAction element => element.Target,
        AgentDesktopRequest.FocusWindow focus => focus.Window,
        _ => null,
    };

    private static ValueTask<DesktopHostFixture> AutoFixtureAsync() =>
        DesktopHostFixture.CreateAsync(
            AgentPermission.Auto,
            AgentPermission.Auto,
            AgentPermission.Auto);

    private static AgentToolDescriptor Descriptor(string toolName) =>
        Assert.Single(
            BuiltInAgentTools.Catalog.Tools,
            tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));

    private static AgentCapability Capability(string toolName) => Descriptor(toolName).Capability;

    /// <summary>
    /// A consumer that grants whatever source it is told to, standing in for
    /// a broker whose risk rules have been weakened. The host must still
    /// refuse an Auto permit for input.
    /// </summary>
    private sealed class ForgedPermitConsumer(AgentAuthorizationSource source)
        : IAgentAuthorizationConsumer
    {
        private AgentActionPermit? _permit;

        public List<AgentActionCompletion> Completions { get; } = [];

        public AgentAuthorizationId Arm(AgentDesktopAction action, DesktopHostFixture fixture)
        {
            Assert.True(BuiltInAgentTools.Catalog.TryGet(action.Proposal.ToolName, out var tool));
            var now = fixture.Clock.GetUtcNow();
            _permit = new AgentActionPermit(
                new AgentActionAuthorization(
                    AgentAuthorizationId.New(),
                    action.Proposal,
                    tool!,
                    source,
                    Assert.IsType<ClientId>(fixture.Human.ClientId),
                    now.AddMinutes(1)),
                now,
                CancellationToken.None);
            return _permit.Authorization.Id;
        }

        public ValueTask<AgentPermitResult> ConsumeAsync(
            AgentAuthorizationId authorizationId,
            AgentActionExecutionBinding currentBinding,
            CancellationToken cancellationToken)
        {
            _ = currentBinding;
            cancellationToken.ThrowIfCancellationRequested();
            var permit = _permit ?? throw new InvalidOperationException("No action is armed.");
            Assert.Equal(permit.Authorization.Id, authorizationId);
            _permit = null;
            return ValueTask.FromResult<AgentPermitResult>(new AgentPermitResult.Granted(permit));
        }

        public ValueTask<AgentAuthorizationError?> CompleteAsync(
            AgentActionPermit permit,
            AgentActionCompletion completion,
            CancellationToken cancellationToken)
        {
            _ = permit;
            cancellationToken.ThrowIfCancellationRequested();
            Completions.Add(completion);
            return ValueTask.FromResult<AgentAuthorizationError?>(null);
        }
    }
}
