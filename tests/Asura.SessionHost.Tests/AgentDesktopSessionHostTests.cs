using Asura.Application;
using Asura.Core;
using Asura.SessionHost;

namespace Asura.SessionHost.Tests;

public sealed class AgentDesktopSessionHostTests
{
    public static TheoryData<AgentDesktopAccessState> AccessStates =>
        [.. Enum.GetValues<AgentDesktopAccessState>()];

    public static TheoryData<string, AgentDesktopFailureCode> FailuresBeforeDelivery
    {
        get
        {
            var data = new TheoryData<string, AgentDesktopFailureCode>();
            foreach (var toolName in new[] { DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click })
            {
                foreach (var code in Enum.GetValues<AgentDesktopFailureCode>())
                {
                    data.Add(toolName, code);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string> EscapingExceptions
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var toolName in DesktopSamples.ToolNames)
            {
                foreach (var exception in new[]
                         {
                             nameof(InvalidOperationException),
                             nameof(ArgumentException),
                             nameof(IOException),
                             nameof(KeyNotFoundException),
                             nameof(OperationCanceledException),
                         })
                {
                    data.Add(toolName, exception);
                }
            }

            return data;
        }
    }

    public static TheoryData<AgentAuthorizationErrorCode, HostErrorCode, string> AuthorizationRefusals
    {
        get
        {
            var data = new TheoryData<AgentAuthorizationErrorCode, HostErrorCode, string>();
            foreach (var code in Enum.GetValues<AgentAuthorizationErrorCode>())
            {
                var (hostCode, stableCode) = code switch
                {
                    AgentAuthorizationErrorCode.AuthorizationExpired =>
                        (HostErrorCode.DeadlineExceeded, "authorization_expired"),
                    AgentAuthorizationErrorCode.ApprovalExpired =>
                        (HostErrorCode.DeadlineExceeded, "approval_expired"),
                    AgentAuthorizationErrorCode.Cancelled or AgentAuthorizationErrorCode.RunCancelled =>
                        (HostErrorCode.Cancelled, AgentDesktopStableCodes.Cancelled),
                    AgentAuthorizationErrorCode.AuditUnavailable =>
                        (HostErrorCode.EngineFailed, AgentDesktopStableCodes.Unavailable),
                    _ => (HostErrorCode.InvalidRequest, "invalid_request"),
                };
                data.Add(code, hostCode, stableCode);
            }

            return data;
        }
    }

    /// <summary>
    /// The closed mapping of ADR 0061, written out so that a change to the
    /// shared code table shows up here as a decision and not as a silent pass:
    /// what the model is told for each engine failure that delivered nothing,
    /// and whether it may try again.
    /// </summary>
    private static readonly Dictionary<AgentDesktopFailureCode, (string StableCode, bool Retryable)>
        ClosedCodes = new()
        {
            [AgentDesktopFailureCode.Unavailable] = ("desktop_unavailable", false),
            [AgentDesktopFailureCode.PermissionDenied] = ("desktop_permission_denied", true),
            [AgentDesktopFailureCode.SelfTarget] = ("desktop_self_target", false),
            [AgentDesktopFailureCode.AppNotFound] = ("desktop_app_not_found", true),
            [AgentDesktopFailureCode.WindowNotFound] = ("desktop_window_not_found", true),
            [AgentDesktopFailureCode.ElementNotFound] = ("desktop_stale_ref", true),
            [AgentDesktopFailureCode.StaleRef] = ("desktop_stale_ref", true),
            [AgentDesktopFailureCode.AmbiguousTarget] = ("desktop_ambiguous_target", true),
            [AgentDesktopFailureCode.SnapshotNotFound] = ("desktop_stale_ref", true),
            [AgentDesktopFailureCode.Timeout] = ("desktop_timeout_before_delivery", true),
            [AgentDesktopFailureCode.PolicyDenied] = ("desktop_policy_denied", true),
            [AgentDesktopFailureCode.ActionFailed] = ("desktop_action_failed", false),
            [AgentDesktopFailureCode.ActionNotSupported] = ("desktop_not_supported", true),
            [AgentDesktopFailureCode.InvalidArguments] = ("desktop_invalid_arguments", false),
            [AgentDesktopFailureCode.SecureField] = ("desktop_secure_field", false),
            [AgentDesktopFailureCode.OutcomeUnknown] = ("desktop_action_outcome_unknown", false),
            [AgentDesktopFailureCode.EngineFaulted] = ("desktop_adapter_faulted", false),
            [AgentDesktopFailureCode.Cancelled] = ("desktop_cancelled", false),
        };

    public static TheoryData<AgentDesktopFailureCode, string, bool> ClosedCodeRows
    {
        get
        {
            var data = new TheoryData<AgentDesktopFailureCode, string, bool>();
            foreach (var (code, expected) in ClosedCodes)
            {
                data.Add(code, expected.StableCode, expected.Retryable);
            }

            return data;
        }
    }

    [Fact]
    public void ClosedCodeTableNamesEveryEngineFailure()
    {
        Assert.Equal(
            Enum.GetValues<AgentDesktopFailureCode>().Order(),
            ClosedCodes.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(ClosedCodeRows))]
    public async Task UndeliveredFailureReportsItsClosedCode(
        AgentDesktopFailureCode code,
        string stableCode,
        bool retryable)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(code);

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.Click));

        Assert.Equal(stableCode, result.Error().StableCode);
        Assert.Equal(retryable, result.Error().Retryable);
        Assert.Equal(stableCode, fixture.LastDetails(action).ResultCode);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task AccessIsUnavailableWithoutAnEngine()
    {
        await using var host = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(),
            new DesktopLifecyclePolicy());

        Assert.Equal(
            AgentDesktopAccessState.Unavailable,
            await host.GetAgentDesktopAccessAsync(default));
    }

    [Theory]
    [MemberData(nameof(AccessStates))]
    public async Task AccessReportsTheEngineState(AgentDesktopAccessState state)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.AccessState = state;

        Assert.Equal(state, await fixture.Host.GetAgentDesktopAccessAsync(default));
    }

    [Fact]
    public async Task BridgeWithoutAnEngineRefusesAndRunsNothing()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        await using var host = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(),
            new DesktopLifecyclePolicy(),
            fixture.Clock,
            agentAuthorizationConsumer: fixture.Broker,
            agentDesktopActionComposer: fixture.Composer);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));

        var refusal = await host.CheckAgentDesktopTargetAsync(action, default);
        var run = await host.RunAgentDesktopActionAsync(AgentAuthorizationId.New(), action, default);

        Assert.Equal(AgentDesktopFailureCode.Unavailable, refusal?.Code);
        Assert.Equal(HostErrorCode.CapabilityNotSupported, run.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.Unavailable, run.Error().StableCode);
        Assert.Equal(AgentDesktopStableCodes.Unavailable, fixture.LastDetails(action).ResultCode);
        Assert.Equal([AuditOutcome.Requested, AuditOutcome.Denied], fixture.Outcomes(action));
    }

    [Fact]
    public async Task PermitIsConsumedBeforeTheSingleDispatch()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);
        IReadOnlyList<AuditOutcome>? seenAtDispatch = null;
        var delivered = DesktopSamples.Delivered(AgentDesktopDelivery.DeliveredVerified);
        fixture.Engine.Execute = (_, _) =>
        {
            seenAtDispatch = fixture.Outcomes(action);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
            return ValueTask.FromResult<AgentDesktopExecutionResult>(
                new AgentDesktopExecutionResult.Succeeded(delivered));
        };

        var result = (await fixture.Host.RunAgentDesktopActionAsync(
            authorization.Id,
            action,
            default)).Value();

        Assert.Same(delivered, result);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Started],
            seenAtDispatch);
        Assert.Single(fixture.Engine.Executions);
        var completed = fixture.LastDetails(action);
        Assert.Equal("desktop_completed", completed.ResultCode);
        Assert.Equal(250, completed.Binding.ExecutionDurationMilliseconds);
        Assert.Equal(fixture.RunId, completed.RunId);
        Assert.Equal(AgentCapability.DesktopInteraction, completed.Capability);
        Assert.Equal(AgentActionRisk.Mutation, completed.Risk);
        Assert.Equal(AgentPolicyDecision.RequiresApproval, completed.Decision);
        Assert.Equal(action.Proposal.TargetIdentity, completed.Binding.TargetIdentity);
        Assert.Equal(action.Proposal.ArgumentDigest, completed.ArgumentDigest);
    }

    [Theory]
    [MemberData(nameof(FailuresBeforeDelivery))]
    public async Task EngineFailureMapsToItsStableCodeAndIsNeverRetried(
        string toolName,
        AgentDesktopFailureCode code)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var failure = new AgentDesktopFailure(code);
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(code);

        var (action, result) = await fixture.RunAsync(DesktopSamples.Request(toolName));

        var error = result.Error();
        Assert.Equal(AgentDesktopStableCodes.For(failure), error.StableCode);
        Assert.Equal(failure.Retryable, error.Retryable);
        Assert.Single(fixture.Engine.Executions);
        Assert.Equal(
            code == AgentDesktopFailureCode.Cancelled ? AuditOutcome.Cancelled : AuditOutcome.Failed,
            fixture.Outcomes(action)[^1]);
        Assert.Equal(error.StableCode, fixture.LastDetails(action).ResultCode);
        fixture.AssertTrailIsOneReadableEntry(action);
        if (AgentDesktopStableCodes.RequiresReconciliation(error.StableCode))
        {
            Assert.False(error.Retryable);
            Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        }
    }

    [Fact]
    public async Task AdapterFaultReleasesTheRunsDesktopSession()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(
            AgentDesktopFailureCode.EngineFaulted,
            AgentDesktopDelivery.Unknown);

        var (_, result) = await fixture.RunAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));

        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Equal([fixture.RunId], fixture.Engine.ReleasedRuns);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task OrdinaryFailureKeepsTheRunsDesktopSession()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(AgentDesktopFailureCode.StaleRef);

        _ = await fixture.RunAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));

        Assert.Empty(fixture.Engine.ReleasedRuns);
    }

    /// <summary>
    /// Finding D15, host side. Whatever escapes the engine after it was handed
    /// a mutation, a cancellation included, says nothing about whether input
    /// reached the application: the outcome is unknown, audited failed, never
    /// cancelled and never repeated, and the run's adapter is no longer
    /// trusted. An observation that throws is an adapter fault.
    /// </summary>
    [Theory]
    [MemberData(nameof(EscapingExceptions))]
    public async Task EngineExceptionIsNeverACleanOutcome(string toolName, string exceptionName)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => throw Escaping(exceptionName);
        var request = DesktopSamples.Request(toolName);
        var (action, result) = await fixture.RunAsync(request);

        var error = result.Error();
        if (request.IsMutation)
        {
            Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, error.StableCode);
            Assert.Equal(HostErrorCode.EngineFailed, error.Code);
            Assert.Equal([fixture.RunId], fixture.Engine.ReleasedRuns);
            Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        }
        else if (exceptionName == nameof(OperationCanceledException))
        {
            Assert.Equal(AgentDesktopStableCodes.Cancelled, error.StableCode);
            Assert.Empty(fixture.Engine.ReleasedRuns);
            Assert.Equal(AuditOutcome.Cancelled, fixture.Outcomes(action)[^1]);
        }
        else
        {
            Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, error.StableCode);
            Assert.Equal([fixture.RunId], fixture.Engine.ReleasedRuns);
            Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        }

        Assert.False(error.Retryable);
        Assert.Equal(error.StableCode, fixture.LastDetails(action).ResultCode);
        Assert.Single(fixture.Engine.Executions);
        Assert.Single(fixture.Engine.Calls, call => call.StartsWith("execute:", StringComparison.Ordinal));
        fixture.AssertTrailIsOneReadableEntry(action);
    }

    [Fact]
    public async Task EngineExceptionDuringAMutationIsNotRetriedOnTheNextRequest()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => throw new InvalidOperationException("engine fault");

        var (first, failed) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.TypeText));
        fixture.Engine.Execute = (request, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Succeeded(DesktopSamples.Success(request)));
        var replay = await fixture.Host.RunAgentDesktopActionAsync(
            AgentAuthorizationId.New(),
            first,
            default);

        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, failed.Error().StableCode);
        Assert.Equal(HostErrorCode.InvalidRequest, replay.Error().Code);
        Assert.Single(fixture.Engine.Executions);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Started, AuditOutcome.Failed],
            fixture.Outcomes(first));
    }

    [Theory]
    [InlineData(AgentDesktopFailureCode.Timeout, AgentDesktopDelivery.Uncertain)]
    [InlineData(AgentDesktopFailureCode.Cancelled, AgentDesktopDelivery.Unknown)]
    [InlineData(AgentDesktopFailureCode.ActionFailed, AgentDesktopDelivery.DeliveredUnverified)]
    [InlineData(AgentDesktopFailureCode.StaleRef, AgentDesktopDelivery.DeliveredVerified)]
    public async Task MutationFailureAfterPossibleDeliveryIsOutcomeUnknown(
        AgentDesktopFailureCode code,
        AgentDesktopDelivery delivery)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(code, delivery);

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.SetValue));

        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task ALaunchThatStartedAsuraIsAFailedSelfTargetThatIsNeverRetried()
    {
        // The engine classifies the launched process after the fact; the
        // launch already happened, so the self-target is not retryable.
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(
            AgentDesktopFailureCode.SelfTarget,
            AgentDesktopDelivery.DeliveredUnverified);

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.LaunchApp));

        Assert.Equal(AgentDesktopStableCodes.SelfTargetLaunched, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        Assert.Equal(AgentDesktopStableCodes.SelfTargetLaunched, fixture.LastDetails(action).ResultCode);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task ObservationFailureStaysWhatTheEngineNamed()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(AgentDesktopFailureCode.Timeout);

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.Wait));

        Assert.Equal(HostErrorCode.DeadlineExceeded, result.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.TimeoutBeforeDelivery, result.Error().StableCode);
        Assert.True(result.Error().Retryable);
        Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
    }

    [Theory]
    [InlineData(AgentDesktopDelivery.Uncertain)]
    [InlineData(AgentDesktopDelivery.Unknown)]
    public async Task ElementActionWhoseDeliveryIsUncertainIsOutcomeUnknown(
        AgentDesktopDelivery delivery)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Succeeded(DesktopSamples.Delivered(delivery)));

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.Toggle));

        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task ElementActionDeliveredWithoutAnObservedEffectSucceedsAsUnverified()
    {
        // A plain button press is delivered but never verifiable: the live
        // fixture run showed every click answered delivered_unverified.
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var delivered = DesktopSamples.Delivered(AgentDesktopDelivery.DeliveredUnverified);
        fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Succeeded(delivered));

        var (action, result) = await fixture.RunAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));

        Assert.Same(delivered, result.Value());
        Assert.Equal(AuditOutcome.Succeeded, fixture.Outcomes(action)[^1]);
        Assert.Equal("desktop_delivered_unverified", fixture.LastDetails(action).ResultCode);
        Assert.Single(fixture.Engine.Executions);
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.Toggle)]
    [InlineData(DesktopAgentToolNames.Expand)]
    [InlineData(DesktopAgentToolNames.Select)]
    [InlineData(DesktopAgentToolNames.Scroll)]
    public async Task ElementActionTheLibraryFoundAlreadySatisfiedSucceedsWithoutDeliveredInput(string toolName)
    {
        // agent-desktop answers ok with a not_delivered disposition when the
        // element is already in the requested state (ActionResult::
        // satisfied_without_delivery): nothing reached the application.
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var satisfied = DesktopSamples.Delivered(AgentDesktopDelivery.NotDelivered);
        fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Succeeded(satisfied));

        var (action, result) = await fixture.RunAsync(DesktopSamples.Request(toolName));

        Assert.Same(satisfied, result.Value());
        Assert.Equal(AuditOutcome.Succeeded, fixture.Outcomes(action)[^1]);
        Assert.Equal("desktop_already_satisfied", fixture.LastDetails(action).ResultCode);
        Assert.Single(fixture.Engine.Executions);
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.FocusWindow)]
    [InlineData(DesktopAgentToolNames.CloseApp)]
    public async Task StatusOnlyEntrypointSucceedsAsRequested(string toolName)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();

        var (action, result) = await fixture.RunAsync(DesktopSamples.Request(toolName));

        Assert.IsType<HostResult<AgentDesktopResult>.Success>(result);
        Assert.Equal(AuditOutcome.Succeeded, fixture.Outcomes(action)[^1]);
        Assert.Equal("desktop_requested", fixture.LastDetails(action).ResultCode);
    }

    [Fact]
    public async Task CloseApprovedForOneObservedProcessIsNeverDispatchedForAnother()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var approved = (AgentDesktopRequest.CloseApp)DesktopSamples.Request(DesktopAgentToolNames.CloseApp);
        var action = await fixture.PrepareAsync(approved);
        var authorization = await fixture.AuthorizeAsync(action);
        var observed = approved.Target;
        AgentDesktopApplicationTarget[] substitutes =
        [
            new(observed.Application, observed.ApplicationIdentity, observed.ProcessId + 1, observed.ProcessInstance, observed.ObservedAt),
            new(observed.Application, observed.ApplicationIdentity, observed.ProcessId, "another-generation", observed.ObservedAt),
            new(observed.Application, observed.ApplicationIdentity, observed.ProcessId, processInstance: null, observed.ObservedAt),
            new(
                observed.Application,
                new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.BundleIdentifier, "com.example.lookalike"),
                observed.ProcessId,
                observed.ProcessInstance,
                observed.ObservedAt),
        ];

        foreach (var substitute in substitutes)
        {
            var forged = new AgentDesktopAction(new AgentDesktopRequest.CloseApp(substitute), action.Proposal);

            var refused = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, forged, default);

            Assert.Equal(AgentDesktopStableCodes.InvalidArguments, refused.Error().StableCode);
        }

        // The permit was never consumed, so the approved close still runs,
        // and the engine receives exactly the observed process.
        var result = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.IsType<HostResult<AgentDesktopResult>.Success>(result);
        Assert.Same(approved, Assert.Single(fixture.Engine.Executions));
    }

    [Fact]
    public async Task CloseOfAProcessThatChangedSinceItWasObservedIsAStaleRefNothingReceived()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Failed(new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef)));

        var (action, result) = await fixture.RunAsync(DesktopSamples.Request(DesktopAgentToolNames.CloseApp));

        Assert.Equal(AgentDesktopStableCodes.StaleRef, result.Error().StableCode);
        Assert.True(result.Error().Retryable);
        Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task LaunchSucceedsWithWhatTheEngineObserved()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var launched = new AgentDesktopResult.AppLaunched(77, WindowId: null, WindowTitle: null);
        fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Succeeded(launched));

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.LaunchApp));

        Assert.Same(launched, result.Value());
        Assert.Equal("desktop_completed", fixture.LastDetails(action).ResultCode);
    }

    [Fact]
    public async Task ObservationRecordsItsResultCount()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();

        var (action, result) = await fixture.RunAsync(
            DesktopSamples.Request(DesktopAgentToolNames.Snapshot));

        Assert.IsType<AgentDesktopResult.Snapshot>(result.Value());
        Assert.Equal(1, fixture.LastDetails(action).Binding.ResultCount);
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.Snapshot, AgentDesktopStableCodes.ActionFailed)]
    [InlineData(DesktopAgentToolNames.Click, AgentDesktopStableCodes.ActionOutcomeUnknown)]
    [InlineData(DesktopAgentToolNames.CloseApp, AgentDesktopStableCodes.ActionOutcomeUnknown)]
    public async Task EngineAnswerOfTheWrongShapeIsNotASuccess(string toolName, string expectedCode)
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => ValueTask.FromResult<AgentDesktopExecutionResult>(
            new AgentDesktopExecutionResult.Succeeded(
                new AgentDesktopResult.WaitCompleted(Satisfied: true, ElapsedMilliseconds: 1)));

        var (_, result) = await fixture.RunAsync(DesktopSamples.Request(toolName));

        Assert.Equal(expectedCode, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task ReplayedAuthorizationIsRejected()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);

        _ = (await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default)).Value();
        var replay = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.InvalidRequest, replay.Error().Code);
        Assert.Equal("invalid_request", replay.Error().StableCode);
        Assert.False(replay.Error().Retryable);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task ForeignAuthorizationIsRejected()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var approved = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var other = await fixture.PrepareAsync(
            DesktopSamples.Request(DesktopAgentToolNames.Click, text: "Other"));
        var authorization = await fixture.AuthorizeAsync(approved);

        var unknown = await fixture.Host.RunAgentDesktopActionAsync(
            AgentAuthorizationId.New(),
            other,
            default);
        var borrowed = await fixture.Host.RunAgentDesktopActionAsync(
            authorization.Id,
            other,
            default);

        Assert.Equal("invalid_request", unknown.Error().StableCode);
        Assert.False(unknown.Error().Retryable);
        Assert.Equal(HostErrorCode.InvalidRequest, borrowed.Error().Code);
        Assert.Equal("invalid_request", borrowed.Error().StableCode);
        Assert.False(borrowed.Error().Retryable);
        Assert.Empty(fixture.Engine.Executions);
    }

    [Fact]
    public async Task ActionBoundToAChangedContextIsRejected()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);
        await fixture.ChangeWorkspaceGraphAsync();

        var result = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.InvalidRequest, result.Error().Code);
        Assert.Equal("invalid_request", result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Denied],
            fixture.Outcomes(action));
    }

    [Fact]
    public async Task ActionForAWorkspaceTheHostDoesNotHoldIsRejected()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        await using var otherHost = new InMemorySessionHostClient(
            new FakeTerminalSessionFactory(),
            new DesktopLifecyclePolicy(),
            fixture.Clock,
            agentAuthorizationConsumer: fixture.Broker,
            agentDesktopActionComposer: fixture.Composer,
            agentDesktopAutomationEngine: fixture.Engine);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);

        var result = await otherHost.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.InvalidRequest, result.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.InvalidArguments, result.Error().StableCode);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Approved],
            fixture.Outcomes(action));
    }

    [Fact]
    public async Task AllowedTargetIsNotAudited()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));

        Assert.Null(await fixture.Host.CheckAgentDesktopTargetAsync(action, default));
        Assert.Empty(fixture.Trail(action));
        Assert.DoesNotContain(fixture.Audit.Events, item => item.Outcome == AuditOutcome.Denied);
        Assert.Empty(fixture.Engine.Executions);
    }

    [Fact]
    public async Task GuardThatCannotAnswerRefusesTheTarget()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Guard = _ => throw new InvalidOperationException("guard fault");
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Snapshot));

        var refusal = await fixture.Host.CheckAgentDesktopTargetAsync(action, default);

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, refusal?.Code);
        Assert.Equal([AuditOutcome.Requested, AuditOutcome.Denied], fixture.Outcomes(action));
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, fixture.LastDetails(action).ResultCode);
        fixture.AssertTrailIsOneReadableEntry(action);
    }

    [Fact]
    public async Task RefusalStandsWhenTheAuditStoreIsUnavailable()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var processId = (uint)Environment.ProcessId;
        fixture.Engine.AsuraProcessIds.Add(processId);
        var action = await fixture.PrepareAsync(
            DesktopSamples.Request(DesktopAgentToolNames.PressKey, processId));
        fixture.Audit.Unavailable = true;

        var refusal = await fixture.Host.CheckAgentDesktopTargetAsync(action, default);
        var requested = await fixture.Broker.RequestAsync(action.Proposal, default);

        Assert.Equal(AgentDesktopFailureCode.SelfTarget, refusal?.Code);
        Assert.IsType<AgentAuthorizationResult.Denied>(requested);
        Assert.Empty(fixture.Engine.Executions);
    }

    [Fact]
    public async Task SelfTargetFoundAfterPermitConsumptionIsRefusedWithoutDispatch()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var processId = (uint)Environment.ProcessId;
        var action = await fixture.PrepareAsync(
            DesktopSamples.Request(DesktopAgentToolNames.Click, processId));
        Assert.Null(await fixture.Host.CheckAgentDesktopTargetAsync(action, default));
        var authorization = await fixture.AuthorizeAsync(action);
        fixture.Engine.AsuraProcessIds.Add(processId);

        var result = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.InvalidRequest, result.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal(
            ["check:" + DesktopAgentToolNames.Click, "check:" + DesktopAgentToolNames.Click],
            fixture.Engine.Calls);
        Assert.Equal(
            [
                AuditOutcome.Requested,
                AuditOutcome.Approved,
                AuditOutcome.Started,
                AuditOutcome.Failed,
            ],
            fixture.Outcomes(action));
        Assert.Equal(AgentDesktopStableCodes.SelfTarget, fixture.LastDetails(action).ResultCode);
        fixture.AssertTrailIsOneReadableEntry(action);
    }

    [Fact]
    public async Task CancellationAfterDispatchKeepsTheOutcome()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentDesktopExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Engine.Execute = async (_, _) =>
        {
            entered.SetResult();
            return await release.Task;
        };
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);
        using var caller = new CancellationTokenSource();

        var running = fixture.Host
            .RunAgentDesktopActionAsync(authorization.Id, action, caller.Token)
            .AsTask();
        await entered.Task;
        await caller.CancelAsync();
        var abandoned = await Task.WhenAny(running, Task.Delay(TimeSpan.FromMilliseconds(200)));
        var delivered = DesktopSamples.Delivered(AgentDesktopDelivery.DeliveredVerified);
        release.SetResult(new AgentDesktopExecutionResult.Succeeded(delivered));
        var result = await running;

        Assert.NotSame(running, abandoned);
        Assert.Same(delivered, result.Value());
        Assert.Single(fixture.Engine.Executions);
        Assert.Equal(AuditOutcome.Succeeded, fixture.Outcomes(action)[^1]);
    }

    [Fact]
    public async Task CancellationAfterDispatchWithoutProofIsOutcomeUnknown()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        fixture.Engine.Execute = async (_, token) =>
        {
            await caller.CancelAsync();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The dispatch token must observe the caller.");
        };
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.TypeText));
        var authorization = await fixture.AuthorizeAsync(action);

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            authorization.Id,
            action,
            caller.Token);

        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Single(fixture.Engine.Executions);
        Assert.Equal(AuditOutcome.Failed, fixture.Outcomes(action)[^1]);
        Assert.Equal(
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            fixture.LastDetails(action).ResultCode);
        Assert.Equal([fixture.RunId], fixture.Engine.ReleasedRuns);
    }

    [Fact]
    public async Task CancelledObservationIsCancelledNotUnknown()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        fixture.Engine.Execute = async (_, token) =>
        {
            await caller.CancelAsync();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The dispatch token must observe the caller.");
        };
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Wait));
        var authorization = await fixture.AuthorizeAsync(action);

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            authorization.Id,
            action,
            caller.Token);

        Assert.Equal(HostErrorCode.Cancelled, result.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.Cancelled, result.Error().StableCode);
        Assert.Equal(AuditOutcome.Cancelled, fixture.Outcomes(action)[^1]);
    }

    [Fact]
    public async Task CancellationBeforeConsumptionLeavesThePermitUnused()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var cancelled = await fixture.Host.RunAgentDesktopActionAsync(
            authorization.Id,
            action,
            caller.Token);
        var later = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.Cancelled, cancelled.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.Cancelled, cancelled.Error().StableCode);
        Assert.IsType<HostResult<AgentDesktopResult>.Success>(later);
        Assert.Single(fixture.Engine.Executions);
    }

    [Fact]
    public async Task CancellationBetweenConsumptionAndDispatchNeverReachesTheEngine()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        fixture.Engine.Guard = _ =>
        {
            caller.Cancel();
            return null;
        };
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            authorization.Id,
            action,
            caller.Token);

        Assert.Equal(HostErrorCode.Cancelled, result.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.Cancelled, result.Error().StableCode);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal(
            [
                AuditOutcome.Requested,
                AuditOutcome.Approved,
                AuditOutcome.Started,
                AuditOutcome.Cancelled,
            ],
            fixture.Outcomes(action));
    }

    [Fact]
    public async Task ExpiredAuthorizationKeepsTheCodeTheRuntimeRenewsFrom()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));

        var result = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.DeadlineExceeded, result.Error().Code);
        Assert.Equal("authorization_expired", result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Empty(fixture.Engine.Calls);
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Denied],
            fixture.Outcomes(action));
    }

    /// <summary>
    /// What the host reports for each permit the broker refuses. Both expiries
    /// keep the runtime's renewal codes, as the MCP host's do; every refusal of
    /// the authorization itself is the non-retryable invalid request the other
    /// families report, so <c>desktop_policy_denied</c> only ever carries the
    /// library's own retryable refusal.
    /// </summary>
    [Theory]
    [MemberData(nameof(AuthorizationRefusals))]
    public async Task RefusedPermitIsReportedLikeEveryOtherFamily(
        AgentAuthorizationErrorCode refusal,
        HostErrorCode expectedCode,
        string expectedStableCode)
    {
        var consumer = new RefusingConsumer(refusal);
        await using var fixture = await DesktopHostFixture.CreateAsync(consumer: consumer);
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.TypeText));

        var result = await fixture.Host.RunAgentDesktopActionAsync(
            AgentAuthorizationId.New(),
            action,
            default);

        Assert.Equal(expectedCode, result.Error().Code);
        Assert.Equal(expectedStableCode, result.Error().StableCode);
        Assert.False(result.Error().Retryable);
        Assert.Empty(fixture.Engine.Calls);
        Assert.Equal(1, consumer.Consumptions);
    }

    [Fact]
    public async Task LibraryPolicyRefusalStaysRetryableAndApartFromARejectedAuthorization()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        fixture.Engine.Execute = (_, _) => DesktopSamples.Failed(AgentDesktopFailureCode.PolicyDenied);

        var (action, library) = await fixture.RunAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var replay = await fixture.Host.RunAgentDesktopActionAsync(AgentAuthorizationId.New(), action, default);

        Assert.Equal(AgentDesktopStableCodes.PolicyDenied, library.Error().StableCode);
        Assert.True(library.Error().Retryable);
        Assert.Equal(AgentDesktopStableCodes.PolicyDenied, fixture.LastDetails(action).ResultCode);
        Assert.Equal("invalid_request", replay.Error().StableCode);
        Assert.False(replay.Error().Retryable);
    }

    [Fact]
    public async Task RunCancelledWhileTheGuardRunsNeverReachesTheEngine()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        var authorization = await fixture.AuthorizeAsync(action);
        fixture.Engine.DuringCheck = async () => Assert.Null(await fixture.Broker.CancelRunAsync(
            new AgentRunCancellation(fixture.RunId, fixture.Human, "user_stop", fixture.Clock.GetUtcNow()),
            default));

        var result = await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default);

        Assert.Equal(HostErrorCode.Cancelled, result.Error().Code);
        Assert.Equal(AgentDesktopStableCodes.Cancelled, result.Error().StableCode);
        Assert.Empty(fixture.Engine.Executions);
        Assert.Equal(["check:" + DesktopAgentToolNames.Click], fixture.Engine.Calls);
        Assert.Equal(AuditOutcome.Cancelled, fixture.Outcomes(action)[^1]);
        fixture.AssertTrailIsOneReadableEntry(action);
    }

    [Fact]
    public async Task ChecksBetweenConsumptionAndDispatchRunInOrderAndOnlyOnce()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();
        var action = await fixture.PrepareAsync(DesktopSamples.Request(DesktopAgentToolNames.Click));
        Assert.Null(await fixture.Host.CheckAgentDesktopTargetAsync(action, default));
        var authorization = await fixture.AuthorizeAsync(action);
        IReadOnlyList<AuditOutcome>? seenAtGuard = null;
        fixture.Engine.DuringCheck = () =>
        {
            seenAtGuard = fixture.Outcomes(action);
            return ValueTask.CompletedTask;
        };

        _ = (await fixture.Host.RunAgentDesktopActionAsync(authorization.Id, action, default)).Value();

        // The repeated guard runs on a consumed permit, and the engine call
        // follows it with nothing else in between.
        Assert.Equal(
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Started],
            seenAtGuard);
        Assert.Equal(
            [
                "check:" + DesktopAgentToolNames.Click,
                "check:" + DesktopAgentToolNames.Click,
                "execute:" + DesktopAgentToolNames.Click,
            ],
            fixture.Engine.Calls);
    }

    [Fact]
    public async Task ReleaseForwardsToTheEngine()
    {
        await using var fixture = await DesktopHostFixture.CreateAsync();

        fixture.Host.ReleaseAgentDesktopRun(fixture.RunId);

        Assert.Equal([fixture.RunId], fixture.Engine.ReleasedRuns);
    }

    private static Exception Escaping(string exceptionName) => exceptionName switch
    {
        nameof(InvalidOperationException) => new InvalidOperationException("engine fault"),
        nameof(ArgumentException) => new ArgumentException("engine fault"),
        nameof(IOException) => new IOException("engine fault"),
        nameof(KeyNotFoundException) => new KeyNotFoundException("engine fault"),
        nameof(OperationCanceledException) => new OperationCanceledException("engine fault"),
        _ => throw new ArgumentOutOfRangeException(nameof(exceptionName)),
    };

    /// <summary>A broker stand-in that refuses every permit with one error code.</summary>
    private sealed class RefusingConsumer(AgentAuthorizationErrorCode refusal) : IAgentAuthorizationConsumer
    {
        public int Consumptions { get; private set; }

        public ValueTask<AgentPermitResult> ConsumeAsync(
            AgentAuthorizationId authorizationId,
            AgentActionExecutionBinding currentBinding,
            CancellationToken cancellationToken)
        {
            _ = authorizationId;
            _ = currentBinding;
            cancellationToken.ThrowIfCancellationRequested();
            Consumptions++;
            return ValueTask.FromResult<AgentPermitResult>(
                new AgentPermitResult.Denied(
                    new AgentAuthorizationError(refusal, "Refused by the test consumer.")));
        }

        public ValueTask<AgentAuthorizationError?> CompleteAsync(
            AgentActionPermit permit,
            AgentActionCompletion completion,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A refused permit can never be completed.");
    }
}
