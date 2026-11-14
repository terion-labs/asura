using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

/// <summary>
/// The <c>desktop.*</c> family end to end through the runtime (ADR 0061),
/// against a recording session host and the real capability broker.
/// </summary>
public sealed class GovernedAgentRuntimeDesktopTests
{
    private const string TextEditSnapshot = """{"app":"TextEdit"}""";

    private const string ClickThenToggleSequence = """
        {"steps":[
          {"tool":"desktop.click","arguments":{"ref":"@s01:e1"}},
          {"tool":"desktop.toggle","arguments":{"ref":"@s01:e3"}}
        ]}
        """;

    [Fact]
    public async Task AGrantedWorkspaceRunIsOfferedAllSixteenTools()
    {
        var provider = ScriptedProvider.Rounds(Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Equal(
            DesktopAgentToolSet.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal),
            AdvertisedDesktopTools(provider).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.Equal(16, AdvertisedDesktopTools(provider).Length);
        Assert.Equal(1, fixture.Desktop.AccessChecks);
    }

    [Fact]
    public async Task ToolsAreOfferedBeforeTheSystemGrantSoTheModelKnowsThemAndCanAskForIt()
    {
        var provider = ScriptedProvider.Rounds(Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.Access = AgentDesktopAccessState.NotGranted;

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Equal(16, AdvertisedDesktopTools(provider).Length);
        Assert.Equal(1, fixture.Desktop.AccessChecks);
    }

    [Fact]
    public async Task ToolsAreWithheldWhereTheLibraryIsUnavailable()
    {
        var provider = ScriptedProvider.Rounds(Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.Access = AgentDesktopAccessState.Unavailable;

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Empty(AdvertisedDesktopTools(provider));
        Assert.Equal(1, fixture.Desktop.AccessChecks);
    }

    [Fact]
    public async Task ACallBeforeTheSystemGrantWaitsForItAndThenRuns()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("waits", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.Access = AgentDesktopAccessState.NotGranted;

        var sending = fixture.SendAsync("Which apps are open?");
        var pending = await WaitForDesktopAccessRequestAsync(fixture.Runtime);

        // Nothing reaches authority while the person is in System Settings.
        Assert.True(pending.ExpiresAtUtc > DateTimeOffset.UtcNow);
        AssertNothingReachedAuthority(fixture);

        fixture.Desktop.Access = AgentDesktopAccessState.Granted;
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "waits").Status);
        Assert.Null(fixture.Runtime.Snapshot.PendingDesktopAccess);
        Assert.Single(fixture.Desktop.Dispatched);
    }

    [Fact]
    public async Task DismissingTheSystemGrantCardRefusesTheCallBeforeAuthority()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("dismissed", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.Access = AgentDesktopAccessState.NotGranted;

        var sending = fixture.SendAsync("Which apps are open?");
        var pending = await WaitForDesktopAccessRequestAsync(fixture.Runtime);

        Assert.False(await fixture.Runtime.DismissDesktopAccessRequestAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.True(await fixture.Runtime.DismissDesktopAccessRequestAsync(pending.Id, CancellationToken.None));
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        AssertRefusal(
            ToolResultOf(provider, "dismissed"),
            AgentDesktopStableCodes.PermissionDenied,
            retryable: false,
            requiresSnapshot: false);
        AssertNothingReachedAuthority(fixture);
        Assert.Null(fixture.Runtime.Snapshot.PendingDesktopAccess);
    }

    [Fact]
    public async Task StoppingTheRunEndsTheWaitForTheSystemGrant()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("stopped", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.Access = AgentDesktopAccessState.NotGranted;

        var sending = fixture.SendAsync("Which apps are open?");
        _ = await WaitForDesktopAccessRequestAsync(fixture.Runtime);

        _ = await fixture.Runtime.StopAsync(CancellationToken.None);
        _ = await sending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(fixture.Runtime.Snapshot.PendingDesktopAccess);
        AssertNothingReachedAuthority(fixture);
    }

    [Theory]
    [InlineData(AgentDesktopAccessState.Unavailable, AgentDesktopStableCodes.Unavailable)]
    public async Task AGrantLostDuringTheRunRefusesCallsBeforeAuthority(
        AgentDesktopAccessState access,
        string stableCode)
    {
        var provider = ScriptedProvider.Rounds(
            Answer(),
            Batch(new ToolCall("revoked", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);
        Assert.NotEmpty(AdvertisedDesktopTools(provider));

        // The run keeps the tool list it started with, so the tool is still
        // offered; the call itself asks the host again.
        fixture.Desktop.Access = access;
        Assert.True((await fixture.SendAsync("Look again.")).IsSuccess);

        Assert.Contains(
            provider.Requests.ToArray()[1].Tools,
            tool => string.Equals(tool.Name, DesktopAgentToolNames.ListApps, StringComparison.Ordinal));
        AssertRefusal(ToolResultOf(provider, "revoked"), stableCode, retryable: false, requiresSnapshot: false);
        AssertNothingReachedAuthority(fixture);
        Assert.Equal(3, fixture.Desktop.AccessChecks);
    }

    [Theory]
    [InlineData(AgentDesktopAccessState.Unavailable, AgentDesktopStableCodes.Unavailable)]
    public async Task AGrantLostAfterTheToolListWasBuiltRefusesTheCallBeforeAuthority(
        AgentDesktopAccessState access,
        string stableCode)
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("revoked", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.ScriptAccess(AgentDesktopAccessState.Granted, access);

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.NotEmpty(AdvertisedDesktopTools(provider));
        AssertRefusal(ToolResultOf(provider, "revoked"), stableCode, retryable: false, requiresSnapshot: false);
        AssertNothingReachedAuthority(fixture);
    }

    [Fact]
    public async Task ToolsAreWithheldWhenTheHostCannotReportItsAccess()
    {
        var provider = ScriptedProvider.Rounds(Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.AccessFailure = new InvalidOperationException("The engine is gone.");

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Empty(AdvertisedDesktopTools(provider));
    }

    [Fact]
    public async Task ToolsAreWithheldWhenTheRunTargetIsNotTheWorkspace()
    {
        var provider = ScriptedProvider.Rounds(Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(),
            DesktopScope.ExactPanel);

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Empty(AdvertisedDesktopTools(provider));
        Assert.Equal(0, fixture.Desktop.AccessChecks);
    }

    [Fact]
    public async Task ToolsAreWithheldWithoutADesktopHost()
    {
        var provider = ScriptedProvider.Rounds(Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(),
            DesktopScope.Workspace,
            withDesktopHost: false);

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Empty(AdvertisedDesktopTools(provider));
    }

    [Fact]
    public void EveryCapabilityHasATitleForItsRequestCard()
    {
        // A capability without one made the card throw and ended the turn,
        // which is how requests for the desktop and Kubernetes capabilities
        // failed before they had titles.
        Assert.All(
            Enum.GetValues<AgentCapability>(),
            capability => Assert.False(string.IsNullOrWhiteSpace(
                GovernedAgentRuntime.CapabilityDisplayTitle(capability))));
    }

    [Fact]
    public async Task CapabilityRequestOffersDataAndInteractionButNeverControl()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall(
                "ask-control",
                IntrinsicAgentTools.RequestCapability,
                """{"capability":"desktop_control"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(AgentPermission.Off, AgentPermission.Off, AgentPermission.Off));

        Assert.True((await fixture.SendAsync("Quit TextEdit.")).IsSuccess);

        var offered = Assert.Single(
                provider.Requests.ToArray()[0].Tools,
                tool => string.Equals(tool.Name, IntrinsicAgentTools.RequestCapability, StringComparison.Ordinal))
            .InputSchema
            .GetProperty("properties")
            .GetProperty("capability")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(token => token.GetString()!)
            .ToArray();
        Assert.Contains(AgentCapabilityProtocol.DesktopData, offered, StringComparer.Ordinal);
        Assert.Contains(AgentCapabilityProtocol.DesktopInteraction, offered, StringComparer.Ordinal);
        Assert.DoesNotContain(AgentCapabilityProtocol.DesktopControl, offered, StringComparer.Ordinal);

        // Asking for it anyway never reaches the user.
        Assert.Equal(AgentToolResultStatus.Failed, ToolResultOf(provider, "ask-control").Status);
        Assert.Null(fixture.Runtime.Snapshot.PendingCapabilityRequest);
        Assert.Equal(
            AgentPermission.Off,
            fixture.Runtime.Snapshot.EffectivePolicy!.GetPermission(AgentCapability.DesktopControl));
    }

    [Fact]
    public async Task ASnapshotRefIsBoundToTheObservedTargetApprovedAndDispatchedOnce()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall("click", DesktopAgentToolNames.Click, """{"ref":"@s01:e1","kind":"double"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        var sending = fixture.SendAsync("Save the document.");
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);

        // Nothing is dispatched while the approval is pending, and the card
        // names what the host observed, not what the model wrote.
        Assert.Equal(DesktopAgentToolNames.Click, approval.ToolName);
        Assert.Equal(AgentActionRisk.Mutation, approval.Risk);
        Assert.Equal("TextEdit", approval.Presentation.Host);
        AssertArgument(approval, "element_ref", "@s01:e1");
        AssertArgument(approval, "pid", "4100");
        AssertArgument(approval, "window", "Untitled");
        AssertArgument(approval, "role", "button");
        AssertArgument(approval, "element", "Save");
        AssertArgument(approval, "bundle_id", "com.apple.TextEdit");
        Assert.Contains(
            approval.Presentation.Arguments,
            argument => string.Equals(argument.Name, "snapshot_age", StringComparison.Ordinal)
                && argument.DisplayValue.EndsWith(" s when proposed", StringComparison.Ordinal));
        Assert.Single(fixture.Desktop.Dispatched);

        Assert.True((await fixture.Runtime.DecideAsync(approval.Id, approved: true, CancellationToken.None)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        var snapshot = ToolResultOf(provider, "snap");
        Assert.Equal(AgentToolResultStatus.Succeeded, snapshot.Status);
        Assert.Equal(DesktopAgentToolResultJson.ObservationCompletedStableCode, snapshot.StableCode);
        using (var document = JsonDocument.Parse(snapshot.Value.Content))
        {
            Assert.Equal("untrusted_desktop", document.RootElement.GetProperty("content_origin").GetString());
            Assert.Equal("s01", document.RootElement.GetProperty("snapshot_id").GetString());
        }

        var click = ToolResultOf(provider, "click");
        Assert.Equal(AgentToolResultStatus.Succeeded, click.Status);
        Assert.Equal(DesktopAgentToolResultJson.ActionCompletedStableCode, click.StableCode);
        var dispatched = Assert.IsType<AgentDesktopRequest.Click>(fixture.Desktop.Dispatched.ToArray()[1].Request);
        Assert.Equal(AgentDesktopClickKind.Double, dispatched.Kind);
        Assert.Equal("@s01:e1", dispatched.Target.Reference.Value);
        Assert.Equal("s01", dispatched.Target.Reference.SnapshotId);
        Assert.Equal(4100u, dispatched.Target.ProcessId);
        Assert.Equal("generation-4100", dispatched.Target.ProcessInstance);
        Assert.Equal("TextEdit", dispatched.Target.Application.Text);

        // Window and application identity come from the host's snapshot
        // result, and the observation time from the run's own clock.
        Assert.Equal("w-1", dispatched.Target.WindowId);
        Assert.Equal("Untitled", dispatched.Target.WindowTitle?.Text);
        Assert.Equal(
            new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.BundleIdentifier,
                "com.apple.TextEdit"),
            dispatched.Target.ApplicationIdentity);
        Assert.InRange(
            dispatched.Target.ObservedAt,
            DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1),
            DateTimeOffset.UtcNow);
        Assert.Equal(2, fixture.Broker.DesktopRequests.Length);
        Assert.Equal(2, fixture.Desktop.Checked.Count);
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.Click, """{"ref":"@never:e1"}""")]
    [InlineData(DesktopAgentToolNames.TypeText, """{"ref":"@s01:e1","text":"hello"}""")]
    [InlineData(DesktopAgentToolNames.PressKey, """{"ref":"@s01:e1","key":"return"}""")]
    [InlineData(DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}""")]
    [InlineData(DesktopAgentToolNames.CloseApp, """{"app":"TextEdit"}""")]
    [InlineData(DesktopAgentToolNames.CloseApp, """{"app":"com.apple.TextEdit"}""")]
    public async Task ATargetTheRunNeverObservedIsRefusedBeforeAuthority(string toolName, string arguments)
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("unobserved", toolName, arguments)),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        Assert.True((await fixture.SendAsync("Act without looking.")).IsSuccess);

        AssertRefusal(
            ToolResultOf(provider, "unobserved"),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        AssertNothingReachedAuthority(fixture);
    }

    [Fact]
    public async Task ARefOutsideItsSnapshotAndARefSpentByAMutationAreRefusedBeforeAuthority()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("foreign", DesktopAgentToolNames.Click, """{"ref":"@s01:e99"}"""),
                new ToolCall("other-snapshot", DesktopAgentToolNames.Click, """{"ref":"@s02:e1"}"""),
                new ToolCall("click", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}"""),
                new ToolCall("spent", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        var sending = fixture.SendAsync("Click around.");
        var approval = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        Assert.Equal(DesktopAgentToolNames.Click, approval.ToolName);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "foreign").StableCode);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "other-snapshot").StableCode);
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "click").Status);

        // The delivered click changed the window, so its other refs expired.
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "spent").StableCode);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click],
            fixture.Broker.DesktopRequests.Select(proposal => proposal.ToolName),
            StringComparer.Ordinal);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.TypeText)]
    [InlineData(DesktopAgentToolNames.SetValue)]
    public async Task SecretShapedTextIsRefusedBeforeAuthority(string toolName)
    {
        const string secret = "password=hunter2-correct-horse";
        Assert.True(AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(secret));
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall(
                "secret",
                toolName,
                JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ref"] = "@s01:e1",
                    ["text"] = secret,
                }))),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        Assert.True((await fixture.SendAsync("Sign in for me.")).IsSuccess);

        var refused = ToolResultOf(provider, "secret");
        AssertRefusal(refused, AgentDesktopStableCodes.PolicyDenied, retryable: false, requiresSnapshot: false);
        Assert.DoesNotContain("hunter2", refused.Value.Content, StringComparison.Ordinal);
        AssertOnlyTheSnapshotReachedAuthority(fixture);
    }

    [Theory]
    [InlineData(DesktopAgentToolNames.TypeText, """{"ref":"@s01:e2","text":"hello"}""")]
    [InlineData(DesktopAgentToolNames.SetValue, """{"ref":"@s01:e2","text":"hello"}""")]
    [InlineData(DesktopAgentToolNames.PressKey, """{"ref":"@s01:e2","key":"a"}""")]
    [InlineData(DesktopAgentToolNames.PressKey, """{"ref":"@s01:e2","key":"delete","modifier":"cmd"}""")]
    [InlineData(DesktopAgentToolNames.Clear, """{"ref":"@s01:e2"}""")]
    public async Task TextClearingAndKeysForAFieldTheSnapshotMarksSecureAreRefusedBeforeAuthority(
        string toolName,
        string arguments)
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall("secure", toolName, arguments)),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        Assert.True((await fixture.SendAsync("Fill in the password.")).IsSuccess);

        AssertRefusal(
            ToolResultOf(provider, "secure"),
            AgentDesktopStableCodes.SecureField,
            retryable: false,
            requiresSnapshot: false);
        AssertOnlyTheSnapshotReachedAuthority(fixture);
    }

    [Fact]
    public async Task ClosingAnApplicationThatShowsASheetIsRefusedBeforeAuthority()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall("close", DesktopAgentToolNames.CloseApp, """{"app":"TextEdit"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.ShowSheet = true;

        Assert.True((await fixture.SendAsync("Quit TextEdit.")).IsSuccess);

        AssertRefusal(
            ToolResultOf(provider, "close"),
            AgentDesktopStableCodes.UnsavedChanges,
            retryable: false,
            requiresSnapshot: false);
        AssertOnlyTheSnapshotReachedAuthority(fixture);
    }

    [Fact]
    public async Task ClosingBindsTheListedProcessAndShowsItOnTheCard()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("apps", DesktopAgentToolNames.ListApps, "{}")),
            Batch(new ToolCall("close", DesktopAgentToolNames.CloseApp, """{"app":"com.apple.TextEdit"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        var sending = fixture.SendAsync("Quit TextEdit.");
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);

        // The card names the process and identity the listing reported.
        Assert.Equal(DesktopAgentToolNames.CloseApp, approval.ToolName);
        Assert.Equal(AgentActionRisk.Destructive, approval.Risk);
        Assert.Equal("TextEdit", approval.Presentation.Host);
        AssertArgument(approval, "pid", "4100");
        AssertArgument(approval, "bundle_id", "com.apple.TextEdit");
        Assert.Contains(
            approval.Presentation.Arguments,
            argument => string.Equals(argument.Name, "observation_age", StringComparison.Ordinal)
                && argument.DisplayValue.EndsWith(" s when proposed", StringComparison.Ordinal));

        Assert.True((await fixture.Runtime.DecideAsync(approval.Id, approved: true, CancellationToken.None)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "close").Status);
        var checkedClose = Assert.IsType<AgentDesktopRequest.CloseApp>(fixture.Desktop.Checked.ToArray()[1].Request);
        var dispatched = Assert.IsType<AgentDesktopRequest.CloseApp>(fixture.Desktop.Dispatched.ToArray()[1].Request);
        Assert.Same(checkedClose, dispatched);
        Assert.Equal(4100u, dispatched.Target.ProcessId);
        Assert.Equal(
            new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.BundleIdentifier,
                "com.apple.TextEdit"),
            dispatched.Target.ApplicationIdentity);
        Assert.Null(dispatched.Target.ProcessInstance);
        Assert.Equal("TextEdit", dispatched.Target.Application.Text);
    }

    [Fact]
    public async Task ControlThePolicyTurnsOffNeverRunsTheTargetCheckAndIsDeniedByPolicy()
    {
        // Threat model D13: the host's target check lists the running
        // applications, which is native code; under Off nothing of it runs.
        // Control is never offered through a capability request, so the call
        // is refused without a card.
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("off", DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(control: AgentPermission.Off));

        // A guard that ran would have answered with this refusal instead.
        fixture.Desktop.TargetRefusal = new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget);

        Assert.True((await fixture.SendAsync("Act anyway.").WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        var refused = ToolResultOf(provider, "off");
        Assert.Equal(AgentToolResultStatus.Failed, refused.Status);
        Assert.Equal("policy_denied", refused.StableCode);
        Assert.Empty(fixture.Desktop.Checked);
        Assert.Empty(fixture.Desktop.Dispatched);
        Assert.Single(fixture.Broker.DesktopRequests);
        Assert.Single(fixture.Audit.Events, auditEvent => auditEvent.Outcome == AuditOutcome.Denied);
        Assert.Null(fixture.Runtime.Snapshot.PendingApproval);
        Assert.Null(fixture.Runtime.Snapshot.PendingCapabilityRequest);
    }

    [Fact]
    public async Task ACallWhoseCapabilityIsOffAsksToEnableItThenAsksForTheAction()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("enabled", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(data: AgentPermission.Off));

        var sending = fixture.SendAsync("Which apps are open?");
        var request = await WaitForCapabilityRequestAsync(fixture.Runtime);

        // The card is the ordinary run-local Off-to-Ask request; the target
        // check has not run and nothing has asked for authority yet.
        Assert.Equal(AgentCapability.DesktopData, request.Capability);
        Assert.Empty(fixture.Desktop.Checked);
        Assert.Empty(fixture.Broker.DesktopRequests);

        Assert.True((await fixture.Runtime.DecideCapabilityRequestAsync(
            request.Id,
            new GovernedAgentCapabilityDecision.AllowAsk(),
            CancellationToken.None)).IsAccepted);

        // Enabling it approved nothing: the listing itself still asks.
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);
        Assert.Equal(DesktopAgentToolNames.ListApps, approval.ToolName);
        Assert.Empty(fixture.Desktop.Dispatched);
        Assert.True((await fixture.Runtime.DecideAsync(approval.Id, approved: true, CancellationToken.None)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "enabled").Status);
        Assert.Equal(
            AgentPermission.Ask,
            fixture.Runtime.Snapshot.EffectivePolicy!.GetPermission(AgentCapability.DesktopData));
    }

    [Fact]
    public async Task KeepingACapabilityOffRefusesTheCallWithoutRunningTheTargetCheck()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("kept-off", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(data: AgentPermission.Off));
        fixture.Desktop.TargetRefusal = new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget);

        var sending = fixture.SendAsync("Read TextEdit.");
        var request = await WaitForCapabilityRequestAsync(fixture.Runtime);
        Assert.True((await fixture.Runtime.DecideCapabilityRequestAsync(
            request.Id,
            new GovernedAgentCapabilityDecision.KeepOff(),
            CancellationToken.None)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        var refused = ToolResultOf(provider, "kept-off");
        Assert.Equal(AgentToolResultStatus.Failed, refused.Status);
        Assert.Equal("capability_request_denied", refused.StableCode);
        Assert.Empty(fixture.Desktop.Checked);
        AssertNothingReachedAuthority(fixture);
        Assert.Equal(
            AgentPermission.Off,
            fixture.Runtime.Snapshot.EffectivePolicy!.GetPermission(AgentCapability.DesktopData));
    }

    [Fact]
    public async Task AnOffCapabilityWithoutTheSystemGrantAsksForTheCapabilityBeforeTheGrant()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("both", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(
            provider,
            DesktopPolicy(data: AgentPermission.Off));
        fixture.Desktop.Access = AgentDesktopAccessState.NotGranted;

        var sending = fixture.SendAsync("Which apps are open?");
        var request = await WaitForCapabilityRequestAsync(fixture.Runtime);
        Assert.Null(fixture.Runtime.Snapshot.PendingDesktopAccess);
        Assert.True((await fixture.Runtime.DecideCapabilityRequestAsync(
            request.Id,
            new GovernedAgentCapabilityDecision.AllowAsk(),
            CancellationToken.None)).IsAccepted);

        _ = await WaitForDesktopAccessRequestAsync(fixture.Runtime);
        fixture.Desktop.Access = AgentDesktopAccessState.Granted;
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);
        Assert.True((await fixture.Runtime.DecideAsync(approval.Id, approved: true, CancellationToken.None)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "both").Status);
    }

    [Fact]
    public async Task ASelfTargetIsRefusedWithoutAnApproval()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("self", DesktopAgentToolNames.LaunchApp, """{"app":"Asura"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.TargetRefusal = new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget);

        // Launching needs an approval under this policy; the send completing
        // with nobody deciding shows that none was requested.
        Assert.True((await fixture.SendAsync("Open Asura.").WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        AssertRefusal(
            ToolResultOf(provider, "self"),
            AgentDesktopStableCodes.SelfTarget,
            retryable: false,
            requiresSnapshot: false);
        Assert.IsType<AgentDesktopRequest.LaunchApp>(Assert.Single(fixture.Desktop.Checked).Request);
        Assert.Empty(fixture.Broker.DesktopRequests);
        Assert.Empty(fixture.Desktop.Dispatched);
        Assert.Null(fixture.Runtime.Snapshot.PendingApproval);
    }

    [Fact]
    public async Task AGuardThatCannotAnswerRefusesTheTarget()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("unguarded", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.TargetCheckFailure = new InvalidOperationException("The guard is gone.");

        Assert.True((await fixture.SendAsync("Look around.")).IsSuccess);

        Assert.Equal(AgentDesktopStableCodes.Unavailable, ToolResultOf(provider, "unguarded").StableCode);
        Assert.Empty(fixture.Broker.DesktopRequests);
        Assert.Empty(fixture.Desktop.Dispatched);
    }

    [Fact]
    public async Task AnUnknownOutcomeSkipsTheBatchRemainderAndRequiresAFreshSnapshot()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(
                new ToolCall("snap-1", DesktopAgentToolNames.Snapshot, TextEditSnapshot),
                new ToolCall("windows", DesktopAgentToolNames.ListWindows, "{}")),
            Batch(
                new ToolCall("click-unknown", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}"""),
                new ToolCall("click-skipped", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}"""),
                new ToolCall("list-skipped", DesktopAgentToolNames.ListApps, "{}")),
            Batch(
                new ToolCall("click-stale", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}"""),
                new ToolCall("focus-stale", DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}"""),
                new ToolCall("launch", DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit"}"""),
                new ToolCall("close-blocked", DesktopAgentToolNames.CloseApp, """{"app":"TextEdit"}"""),
                new ToolCall("look", DesktopAgentToolNames.ListApps, "{}")),
            Batch(new ToolCall("snap-2", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall("click-fresh", DesktopAgentToolNames.Click, """{"ref":"@s02:e1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            "The click may have been delivered."));

        var sending = fixture.SendAsync("Save the document.");
        var uncertain = await ApproveNextAsync(fixture.Runtime, previous: null);
        var launch = await ApproveNextAsync(fixture.Runtime, uncertain.Id);
        var fresh = await ApproveNextAsync(fixture.Runtime, launch.Id);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        var unknown = ToolResultOf(provider, "click-unknown");
        Assert.Equal(AgentToolResultStatus.Failed, unknown.Status);
        AssertRefusal(
            unknown,
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            retryable: false,
            requiresSnapshot: true);
        foreach (var skipped in new[] { "click-skipped", "list-skipped" })
        {
            var result = ToolResultOf(provider, skipped);
            Assert.Equal("tool_batch_reconciliation_required", result.StableCode);
            using var document = JsonDocument.Parse(result.Value.Content);
            Assert.Equal(
                AgentDesktopStableCodes.ActionOutcomeUnknown,
                document.RootElement.GetProperty("caused_by").GetString());
        }

        // Everything observed before the unknown outcome is gone, and no
        // further mutation resolves until the run has snapshotted again. A
        // launch is the exception: it names no observed target, so it goes
        // to its own exact approval, and it does not stand in for a snapshot.
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "click-stale").StableCode);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "focus-stale").StableCode);
        Assert.Equal(DesktopAgentToolNames.LaunchApp, launch.ToolName);
        Assert.Equal(AgentActionRisk.Destructive, launch.Risk);
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "launch").Status);
        AssertRefusal(
            ToolResultOf(provider, "close-blocked"),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "look").Status);
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "click-fresh").Status);
        Assert.Equal(DesktopAgentToolNames.Click, fresh.ToolName);

        // The uncertain click was dispatched exactly once.
        Assert.Equal(
            [
                DesktopAgentToolNames.Snapshot,
                DesktopAgentToolNames.ListWindows,
                DesktopAgentToolNames.Click,
                DesktopAgentToolNames.LaunchApp,
                DesktopAgentToolNames.ListApps,
                DesktopAgentToolNames.Snapshot,
                DesktopAgentToolNames.Click,
            ],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
        Assert.Equal(
            fixture.Desktop.Dispatched.Select(action => action.Proposal.Id),
            fixture.Broker.DesktopRequests.Select(proposal => proposal.Id));
        Assert.Empty(fixture.Desktop.Released);
    }

    [Fact]
    public async Task FocusingAListedWindowSucceedsAsARequestAndBindsTheListedIdentity()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("windows", DesktopAgentToolNames.ListWindows, "{}")),
            Batch(new ToolCall("focus", DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        var sending = fixture.SendAsync("Bring TextEdit forward.");
        var approval = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        AssertArgument(approval, "window_id", "w-1");
        AssertArgument(approval, "window", "Untitled");
        AssertArgument(approval, "bundle_id", "com.apple.TextEdit");
        Assert.Contains(
            approval.Presentation.Arguments,
            argument => string.Equals(argument.Name, "listing_age", StringComparison.Ordinal));

        // The host can only say the focus was requested; that is a success.
        var focus = ToolResultOf(provider, "focus");
        Assert.Equal(AgentToolResultStatus.Succeeded, focus.Status);
        using (var document = JsonDocument.Parse(focus.Value.Content))
        {
            Assert.False(document.RootElement.GetProperty("verified").GetBoolean());
        }

        var dispatched = Assert.IsType<AgentDesktopRequest.FocusWindow>(
            fixture.Desktop.Dispatched.ToArray()[1].Request);
        Assert.Equal("w-1", dispatched.Window.WindowId);
        Assert.Equal(4100u, dispatched.Window.ProcessId);
        Assert.Equal("generation-4100", dispatched.Window.ProcessInstance);
        Assert.Equal(AgentDesktopApplicationIdentityKind.BundleIdentifier, dispatched.Window.ApplicationIdentity.Kind);
    }

    [Fact]
    public async Task AnElementActionDeliveredWithoutVerificationSucceedsAndExpiresItsWindow()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("click-unverified", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}"""),
                new ToolCall("list-after", DesktopAgentToolNames.ListApps, "{}"),
                new ToolCall("toggle-same-window", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.ElementDelivery = AgentDesktopDelivery.DeliveredUnverified;

        var sending = fixture.SendAsync("Save the document.");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        // A plain button press is delivered but never verifiable, so it is a
        // success marked unverified, not an unknown outcome: the batch goes on.
        var click = ToolResultOf(provider, "click-unverified");
        Assert.Equal(AgentToolResultStatus.Succeeded, click.Status);
        using (var document = JsonDocument.Parse(click.Value.Content))
        {
            Assert.False(document.RootElement.GetProperty("verified").GetBoolean());
        }

        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "list-after").Status);

        // The input was delivered, so the window's refs expired with it.
        AssertRefusal(
            ToolResultOf(provider, "toggle-same-window"),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click, DesktopAgentToolNames.ListApps],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnElementActionThatWasAlreadySatisfiedNeitherSkipsTheBatchNorExpiresItsWindow()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("toggle-satisfied", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}"""),
                new ToolCall("list-after", DesktopAgentToolNames.ListApps, "{}"),
                new ToolCall("click-same-window", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());

        // The library's answer for a checkbox that is already checked.
        fixture.Desktop.ElementDelivery = AgentDesktopDelivery.NotDelivered;

        var sending = fixture.SendAsync("Turn on line wrapping, then save.");
        var toggle = await ApproveNextAsync(fixture.Runtime, previous: null);
        var click = await ApproveNextAsync(fixture.Runtime, toggle.Id);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        var satisfied = ToolResultOf(provider, "toggle-satisfied");
        Assert.Equal(AgentToolResultStatus.Succeeded, satisfied.Status);
        Assert.Equal(DesktopAgentToolResultJson.ActionCompletedStableCode, satisfied.StableCode);
        using (var document = JsonDocument.Parse(satisfied.Value.Content))
        {
            Assert.True(document.RootElement.GetProperty("already_in_state").GetBoolean());
            Assert.Equal("not_delivered", document.RootElement.GetProperty("delivery").GetString());
            Assert.False(document.RootElement.TryGetProperty("required_action", out _));
        }

        // Nothing was delivered, so nothing is reconciled and the window's
        // other refs still resolve to an approval and a dispatch.
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "list-after").Status);
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "click-same-window").Status);
        Assert.Equal(DesktopAgentToolNames.Toggle, toggle.ToolName);
        Assert.Equal(DesktopAgentToolNames.Click, click.ToolName);
        AssertArgument(click, "element_ref", "@s01:e1");
        Assert.Equal(
            [
                DesktopAgentToolNames.Snapshot,
                DesktopAgentToolNames.Toggle,
                DesktopAgentToolNames.ListApps,
                DesktopAgentToolNames.Click,
            ],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AMutationWhoseCompletionAuditFailedStillRequiresAFreshSnapshot()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await fixture.CallExternalAsync(DesktopAgentToolNames.Snapshot, TextEditSnapshot)).Status);

        // SessionHost replaces the result of an action whose completion it
        // could not audit; the click may well have been delivered.
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentActionFailureCodes.CompletionAuditUnavailable,
            "The desktop action outcome could not be audited."));
        var clicking = fixture.CallExternalAsync(DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        var click = await clicking.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AgentActionFailureCodes.CompletionAuditUnavailable, click.StableCode);

        // Clearing forgets every observation anyway, so fresh listings are
        // what shows that the snapshot requirement itself survived.
        Assert.True(await fixture.Runtime.ClearAsync(CancellationToken.None));
        await AssertListedAgainButStillStaleAsync(fixture);
        Assert.Equal(
            [
                DesktopAgentToolNames.Snapshot,
                DesktopAgentToolNames.Click,
                DesktopAgentToolNames.ListApps,
                DesktopAgentToolNames.ListWindows,
            ],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task ALaunchThatReachedAsuraSkipsTheBatchRemainderAndRequiresAFreshSnapshot()
    {
        // The launch happened and started Asura, so the desktop changed under
        // the rest of the batch exactly as after an unknown outcome.
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("windows", DesktopAgentToolNames.ListWindows, "{}")),
            Batch(
                new ToolCall("launch-asura", DesktopAgentToolNames.LaunchApp, """{"app":"Notes"}"""),
                new ToolCall("list-skipped", DesktopAgentToolNames.ListApps, "{}")),
            Batch(new ToolCall("focus-stale", DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.SelfTargetLaunched,
            "The launch reached Asura itself; nothing further was done. Take a fresh desktop snapshot."));

        var sending = fixture.SendAsync("Open Notes.");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        AssertRefusal(
            ToolResultOf(provider, "launch-asura"),
            AgentDesktopStableCodes.SelfTargetLaunched,
            retryable: false,
            requiresSnapshot: true);
        var skipped = ToolResultOf(provider, "list-skipped");
        Assert.Equal("tool_batch_reconciliation_required", skipped.StableCode);
        using (var document = JsonDocument.Parse(skipped.Value.Content))
        {
            Assert.Equal(
                AgentDesktopStableCodes.SelfTargetLaunched,
                document.RootElement.GetProperty("caused_by").GetString());
        }

        AssertRefusal(
            ToolResultOf(provider, "focus-stale"),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        Assert.Equal(
            [DesktopAgentToolNames.ListWindows, DesktopAgentToolNames.LaunchApp],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AHostSelfTargetRefusalOfALaunchProvesNothingWasDelivered()
    {
        // The host's own guard after permit consumption refuses before any
        // input, with the plain code: the observations stay usable.
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await fixture.CallExternalAsync(DesktopAgentToolNames.ListWindows, "{}")).Status);

        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.InvalidRequest,
            AgentDesktopStableCodes.SelfTarget,
            "Desktop automation cannot observe or operate Asura itself."));
        var launching = fixture.CallExternalAsync(DesktopAgentToolNames.LaunchApp, """{"app":"Asura"}""");
        var launchCard = await ApproveNextAsync(fixture.Runtime, previous: null);
        AssertRefusal(
            await launching.WaitAsync(TimeSpan.FromSeconds(10)),
            AgentDesktopStableCodes.SelfTarget,
            retryable: false,
            requiresSnapshot: false);

        // The window listing taken before the refusal still resolves.
        var focusing = fixture.CallExternalAsync(DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}""");
        _ = await ApproveNextAsync(fixture.Runtime, launchCard.Id);
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await focusing.WaitAsync(TimeSpan.FromSeconds(10))).Status);
    }

    [Fact]
    public async Task AnAdapterFaultCommitsTheResultSkipsTheBatchAndCancelsTheRun()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("click-fault", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}"""),
                new ToolCall("list-skipped", DesktopAgentToolNames.ListApps, "{}")),
            Batch(new ToolCall("click-after", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.AdapterFaulted,
            "The automation library faulted."));

        var sending = fixture.SendAsync("Save the document.");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        var result = await sending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, result.Code);
        Assert.Equal(GovernedAgentState.Cancelled, fixture.Runtime.Snapshot.State);

        // The provider is not asked to continue a run whose adapter is gone.
        Assert.Equal(2, provider.Requests.Count);
        var faultedRun = fixture.Desktop.Dispatched.ToArray()[1].Proposal.RunId;
        var cancellation = Assert.Single(fixture.Broker.Cancellations);
        Assert.Equal(faultedRun, cancellation.RunId);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, cancellation.StableReasonCode);
        Assert.Contains(faultedRun, fixture.Desktop.Released);
        Assert.Equal(2, fixture.Desktop.Dispatched.Count);

        // The next message continues the conversation in a new run: the
        // faulted result and the skipped remainder were committed, and
        // nothing the faulted run observed is usable.
        Assert.True((await fixture.SendAsync("What happened?").WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        var faulted = ToolResultOf(provider, "click-fault");
        AssertRefusal(faulted, AgentDesktopStableCodes.AdapterFaulted, retryable: false, requiresSnapshot: true);
        Assert.Equal(
            "tool_batch_reconciliation_required",
            ToolResultOf(provider, "list-skipped").StableCode);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "click-after").StableCode);
        Assert.Equal(2, fixture.Desktop.Dispatched.Count);
    }

    [Fact]
    public async Task AnUnknownOutcomeInsideASequenceStopsItAndSkipsTheBatchRemainder()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("sequence", IntrinsicAgentTools.RunSequence, ClickThenToggleSequence),
                new ToolCall("list-skipped", DesktopAgentToolNames.ListApps, "{}")),
            Batch(new ToolCall("toggle-stale", DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            "The click may have been delivered."));

        var sending = fixture.SendAsync("Save and wrap.");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        // The sequence stops at the uncertain step and carries its code out,
        // so the outer batch reconciles exactly as for a direct call.
        var sequence = ToolResultOf(provider, "sequence");
        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, sequence.StableCode);
        using (var document = JsonDocument.Parse(sequence.Value.Content))
        {
            Assert.Equal(1, document.RootElement.GetProperty("executed_steps").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("remaining_steps").GetInt32());
        }

        var skipped = ToolResultOf(provider, "list-skipped");
        Assert.Equal("tool_batch_reconciliation_required", skipped.StableCode);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "toggle-stale").StableCode);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
        Assert.Empty(fixture.Broker.Cancellations);
    }

    [Fact]
    public async Task AnAdapterFaultInsideASequenceCancelsTheRun()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("sequence", IntrinsicAgentTools.RunSequence, ClickThenToggleSequence),
                new ToolCall("list-skipped", DesktopAgentToolNames.ListApps, "{}")));
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.AdapterFaulted,
            "The automation library faulted."));

        var sending = fixture.SendAsync("Save and wrap.");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        var result = await sending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, result.Code);
        Assert.Equal(GovernedAgentState.Cancelled, fixture.Runtime.Snapshot.State);
        Assert.Equal(2, provider.Requests.Count);
        var cancellation = Assert.Single(fixture.Broker.Cancellations);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, cancellation.StableReasonCode);
        Assert.Equal(2, fixture.Desktop.Dispatched.Count);
    }

    [Fact]
    public async Task AnExpiredApprovalIsRenewedFromTheOriginalCallLikeEveryOtherFamily()
    {
        var time = new ManualDesktopTimeProvider(DateTimeOffset.UtcNow);
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("launch", DesktopAgentToolNames.LaunchApp, """{"app":"TextEdit"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy(), time: time);

        var sending = fixture.SendAsync("Open TextEdit.");
        var first = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);
        await time.WaitForTimerDueAtAsync(first.ExpiresAtUtc);
        time.Advance(AgentCapabilityBroker.DefaultApprovalLifetime);
        var renewed = await WaitForNewApprovalAsync(fixture.Runtime, first.Id);

        // A fresh proposal for the same call: re-parsed, re-guarded and
        // shown again, with nothing dispatched under the expired one.
        Assert.Equal(first.ToolName, renewed.ToolName);
        Assert.Equal(first.Presentation.Arguments.ToArray(), renewed.Presentation.Arguments.ToArray());
        Assert.True(renewed.ExpiresAtUtc > first.ExpiresAtUtc);
        Assert.Empty(fixture.Desktop.Dispatched);
        Assert.Equal(2, fixture.Desktop.Checked.Count);
        Assert.False((await fixture.Runtime.DecideAsync(first.Id, approved: true, CancellationToken.None)).IsAccepted);

        Assert.True((await fixture.Runtime.DecideAsync(renewed.Id, approved: true, CancellationToken.None)).IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "launch").Status);
        Assert.IsType<AgentDesktopRequest.LaunchApp>(Assert.Single(fixture.Desktop.Dispatched).Request);
        Assert.Equal(2, fixture.Broker.DesktopRequests.Length);
    }

    [Fact]
    public async Task ARenewedApprovalRebindsItsRefAndFindsTheSnapshotExpired()
    {
        var time = new ManualDesktopTimeProvider(DateTimeOffset.UtcNow);
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall("click", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy(), time: time);

        var sending = fixture.SendAsync("Save the document.");
        var approval = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);
        await time.WaitForTimerDueAtAsync(approval.ExpiresAtUtc);
        time.Advance(AgentCapabilityBroker.DefaultApprovalLifetime);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        // Renewal goes back through the ledger, which no longer holds a
        // snapshot that old, so no second card is shown.
        AssertRefusal(
            ToolResultOf(provider, "click"),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        Assert.IsType<AgentDesktopRequest.Snapshot>(Assert.Single(fixture.Desktop.Dispatched).Request);
        Assert.Equal(2, fixture.Broker.DesktopRequests.Length);
    }

    [Fact]
    public async Task AnAuthorizationThatExpiresAtTheHostIsACleanFailureThatDispatchedNothing()
    {
        var time = new ManualDesktopTimeProvider(DateTimeOffset.UtcNow);
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(
                new ToolCall("click-expired", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}"""),
                new ToolCall("look", DesktopAgentToolNames.ListApps, "{}")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy(), time: time);

        // The user approves, and the one-action authorization runs out
        // before the host can consume it.
        fixture.Desktop.BeforeNextMutationConsume(() => time.Advance(TimeSpan.FromMinutes(5)));
        var sending = fixture.SendAsync("Save the document.");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);

        // The host reports the expiry with the generic deadline code every
        // other family's host uses. Nothing reached the application, so it
        // is neither renewed behind the user's back nor treated as an
        // unknown outcome: the batch goes on.
        var click = ToolResultOf(provider, "click-expired");
        Assert.Equal(AgentToolResultStatus.Failed, click.Status);
        AssertRefusal(click, "deadline_exceeded", retryable: false, requiresSnapshot: false);
        Assert.Equal(AgentToolResultStatus.Succeeded, ToolResultOf(provider, "look").Status);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.ListApps],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click, DesktopAgentToolNames.ListApps],
            fixture.Broker.DesktopRequests.Select(proposal => proposal.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheExternalToolListAsksTheHostForTheGrantEveryTime()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);

        var granted = await fixture.Runtime.ListExternalToolsAsync(CancellationToken.None);
        fixture.Desktop.Access = AgentDesktopAccessState.Unavailable;
        var withheld = await fixture.Runtime.ListExternalToolsAsync(CancellationToken.None);

        Assert.Equal(
            DesktopAgentToolSet.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal),
            granted.Select(tool => tool.Name).Where(DesktopAgentToolNames.Owns).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.DoesNotContain(withheld, tool => DesktopAgentToolNames.Owns(tool.Name));
        Assert.Equal(2, fixture.Desktop.AccessChecks);
    }

    [Theory]
    [InlineData(AgentDesktopAccessState.NotGranted, AgentDesktopStableCodes.PermissionDenied)]
    [InlineData(AgentDesktopAccessState.Unavailable, AgentDesktopStableCodes.Unavailable)]
    public async Task AnExternalCallWithoutTheGrantIsRefusedBeforeAuthority(
        AgentDesktopAccessState access,
        string stableCode)
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        fixture.Desktop.ScriptAccess(AgentDesktopAccessState.Granted, access);

        var result = await fixture.CallExternalAsync(DesktopAgentToolNames.ListApps, "{}");

        AssertRefusal(result, stableCode, retryable: false, requiresSnapshot: false);
        AssertNothingReachedAuthority(fixture);
        Assert.Equal(2, fixture.Desktop.AccessChecks);
    }

    [Fact]
    public async Task AnExternalUnknownOutcomeEndsTheRunAndStillRequiresAFreshSnapshot()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await fixture.CallExternalAsync(DesktopAgentToolNames.Snapshot, TextEditSnapshot)).Status);
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            "The click may have been delivered."));

        var clicking = fixture.CallExternalAsync(DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        var click = await clicking.WaitAsync(TimeSpan.FromSeconds(10));

        AssertRefusal(click, AgentDesktopStableCodes.ActionOutcomeUnknown, retryable: false, requiresSnapshot: true);
        Assert.Equal(GovernedAgentState.Failed, fixture.Runtime.Snapshot.State);
        Assert.Equal(
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            Assert.Single(fixture.Broker.Cancellations).StableReasonCode);
        Assert.Equal(
            "agent_run_requires_clear",
            (await fixture.CallExternalAsync(DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}""")).StableCode);

        // Clearing ends the run, not the doubt about the desktop.
        Assert.True(await fixture.Runtime.ClearAsync(CancellationToken.None));
        AssertRefusal(
            await fixture.CallExternalAsync(DesktopAgentToolNames.Toggle, """{"ref":"@s01:e3"}"""),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);

        // Clearing forgets every observation anyway, so fresh listings are
        // what shows that the snapshot requirement itself survived.
        await AssertListedAgainButStillStaleAsync(fixture);

        // Only a snapshot lifts it; then the same listings resolve.
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await fixture.CallExternalAsync(DesktopAgentToolNames.Snapshot, TextEditSnapshot)).Status);
        var focusing = fixture.CallExternalAsync(DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}""");
        var focus = await ApproveNextAsync(fixture.Runtime, previous: null);
        Assert.Equal(AgentToolResultStatus.Succeeded, (await focusing.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        var closing = fixture.CallExternalAsync(DesktopAgentToolNames.CloseApp, """{"app":"TextEdit"}""");
        var close = await ApproveNextAsync(fixture.Runtime, focus.Id);
        Assert.Equal(AgentToolResultStatus.Succeeded, (await closing.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Equal(DesktopAgentToolNames.FocusWindow, focus.ToolName);
        Assert.Equal(DesktopAgentToolNames.CloseApp, close.ToolName);
        Assert.Equal(
            [
                DesktopAgentToolNames.Snapshot,
                DesktopAgentToolNames.Click,
                DesktopAgentToolNames.ListApps,
                DesktopAgentToolNames.ListWindows,
                DesktopAgentToolNames.Snapshot,
                DesktopAgentToolNames.FocusWindow,
                DesktopAgentToolNames.CloseApp,
            ],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnExternalSequenceStopsAtAnUnknownOutcomeAndEndsTheRun()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        _ = await fixture.CallExternalAsync(DesktopAgentToolNames.Snapshot, TextEditSnapshot);
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.ActionOutcomeUnknown,
            "The click may have been delivered."));

        var running = fixture.CallExternalAsync(IntrinsicAgentTools.RunSequence, ClickThenToggleSequence);
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        var sequence = await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(AgentDesktopStableCodes.ActionOutcomeUnknown, sequence.StableCode);
        using (var document = JsonDocument.Parse(sequence.Value.Content))
        {
            Assert.Equal(1, document.RootElement.GetProperty("remaining_steps").GetInt32());
        }

        Assert.Equal(GovernedAgentState.Failed, fixture.Runtime.Snapshot.State);
        Assert.Single(fixture.Broker.Cancellations);
        Assert.Equal(
            [DesktopAgentToolNames.Snapshot, DesktopAgentToolNames.Click],
            fixture.Desktop.Dispatched.Select(action => action.Request.ToolName),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnExternalAdapterFaultCancelsTheRunAndReleasesItsDesktopSession()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        _ = await fixture.CallExternalAsync(DesktopAgentToolNames.Snapshot, TextEditSnapshot);
        var run = Assert.Single(fixture.Desktop.Dispatched).Proposal.RunId;
        fixture.Desktop.FailNextMutation(new HostError(
            HostErrorCode.EngineFailed,
            AgentDesktopStableCodes.AdapterFaulted,
            "The automation library faulted."));

        var clicking = fixture.CallExternalAsync(DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""");
        _ = await ApproveNextAsync(fixture.Runtime, previous: null);
        var click = await clicking.WaitAsync(TimeSpan.FromSeconds(10));

        AssertRefusal(click, AgentDesktopStableCodes.AdapterFaulted, retryable: false, requiresSnapshot: true);
        var cancellation = Assert.Single(fixture.Broker.Cancellations);
        Assert.Equal(run, cancellation.RunId);
        Assert.Equal(AgentDesktopStableCodes.AdapterFaulted, cancellation.StableReasonCode);
        Assert.Contains(run, fixture.Desktop.Released);
        Assert.Equal(GovernedAgentState.Failed, fixture.Runtime.Snapshot.State);
    }

    [Fact]
    public async Task ClearReleasesTheRunAndForgetsWhatItObserved()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Answer(),
            Batch(new ToolCall("after-clear", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        Assert.True((await fixture.SendAsync("Look at TextEdit.")).IsSuccess);
        var run = Assert.Single(fixture.Desktop.Dispatched).Proposal.RunId;
        Assert.Empty(fixture.Desktop.Released);

        Assert.True(await fixture.Runtime.ClearAsync(CancellationToken.None));

        Assert.Equal([run], fixture.Desktop.Released);
        Assert.True((await fixture.SendAsync("Click Save.")).IsSuccess);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "after-clear").StableCode);
        Assert.Single(fixture.Desktop.Dispatched);
    }

    [Fact]
    public async Task StopReleasesTheRunAndForgetsWhatItObserved()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Answer(),
            Batch(new ToolCall("after-stop", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""")),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        Assert.True((await fixture.SendAsync("Look at TextEdit.")).IsSuccess);
        var run = Assert.Single(fixture.Desktop.Dispatched).Proposal.RunId;

        var stopped = await fixture.Runtime.StopAsync(CancellationToken.None);

        Assert.Equal("agent_stopped", stopped.Code);
        Assert.Equal([run], fixture.Desktop.Released);
        Assert.True((await fixture.SendAsync("Click Save.")).IsSuccess);
        Assert.Equal(AgentDesktopStableCodes.StaleRef, ToolResultOf(provider, "after-stop").StableCode);
        Assert.Single(fixture.Desktop.Dispatched);
    }

    [Fact]
    public async Task CancellingTheRequestReleasesTheRunBeforeAnythingIsDispatched()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Batch(new ToolCall("click", DesktopAgentToolNames.Click, """{"ref":"@s01:e1"}""")));
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        using var cancellation = new CancellationTokenSource();

        var sending = fixture.SendAsync("Save the document.", cancellation.Token);
        _ = await WaitForNewApprovalAsync(fixture.Runtime, previous: null);
        var run = Assert.Single(fixture.Desktop.Dispatched).Proposal.RunId;
        await cancellation.CancelAsync();
        try
        {
            _ = await sending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
            // Either outcome of the cancelled send is acceptable here.
        }

        Assert.Contains(run, fixture.Desktop.Released);
        Assert.Single(fixture.Desktop.Dispatched);
    }

    [Fact]
    public async Task DisposeReleasesTheRun()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Answer());
        var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        AgentRunId run;
        try
        {
            Assert.True((await fixture.SendAsync("Look at TextEdit.")).IsSuccess);
            run = Assert.Single(fixture.Desktop.Dispatched).Proposal.RunId;
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        Assert.Equal([run], fixture.Desktop.Released);
    }

    [Fact]
    public async Task AHostileSnapshotReachesTheProviderInsideTheResultBudget()
    {
        var provider = ScriptedProvider.Rounds(
            Batch(new ToolCall("snap", DesktopAgentToolNames.Snapshot, TextEditSnapshot)),
            Answer());
        await using var fixture = DesktopRuntimeFixture.Create(provider, DesktopPolicy());
        fixture.Desktop.HostileSnapshot = true;

        Assert.True((await fixture.SendAsync("Look at TextEdit.")).IsSuccess);

        var snapshot = ToolResultOf(provider, "snap");
        Assert.Equal(AgentToolResultStatus.Succeeded, snapshot.Status);
        Assert.InRange(
            System.Text.Encoding.UTF8.GetByteCount(snapshot.Value.Content),
            1,
            DesktopAgentToolResultJson.MaximumResultBytes);
        using var document = JsonDocument.Parse(snapshot.Value.Content);
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        Assert.InRange(
            document.RootElement.GetProperty("nodes").GetArrayLength(),
            1,
            AgentDesktopResult.MaximumSnapshotNodes - 1);
    }

    [Fact]
    public async Task OnlySnapshotNodesTheProviderWasShownBecomeTargets()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        fixture.Desktop.HostileSnapshot = true;

        var snapshot = await fixture.CallExternalAsync(DesktopAgentToolNames.Snapshot, TextEditSnapshot);

        Assert.Equal(AgentToolResultStatus.Succeeded, snapshot.Status);
        using var document = JsonDocument.Parse(snapshot.Value.Content);
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        var shown = document.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
        var observed = Assert.IsType<AgentDesktopResult.Snapshot>(Assert.Single(fixture.Desktop.Responses));
        Assert.InRange(shown.Length, 1, observed.Nodes.Count - 1);
        var lastShown = shown[^1].GetProperty("ref").GetString()!;
        Assert.Equal(observed.Nodes[shown.Length - 1].Reference?.Value, lastShown);
        var firstCut = Assert.IsType<string>(observed.Nodes[shown.Length].Reference?.Value);

        // The host returned the node past the cut, but the model never saw it.
        await AssertStaleBeforeAuthorityAsync(
            fixture,
            DesktopAgentToolNames.Click,
            $$"""{"ref":"{{firstCut}}"}""");

        var clicking = fixture.CallExternalAsync(DesktopAgentToolNames.Click, $$"""{"ref":"{{lastShown}}"}""");
        var approval = await ApproveNextAsync(fixture.Runtime, previous: null);
        AssertArgument(approval, "element_ref", lastShown);
        Assert.Equal(AgentToolResultStatus.Succeeded, (await clicking.WaitAsync(TimeSpan.FromSeconds(10))).Status);
    }

    [Fact]
    public async Task OnlyListedWindowsTheProviderWasShownBecomeTargets()
    {
        await using var fixture = DesktopRuntimeFixture.Create(
            ScriptedProvider.Rounds(),
            DesktopPolicy(),
            external: true);
        fixture.Desktop.HostileWindows = true;

        var windows = await fixture.CallExternalAsync(DesktopAgentToolNames.ListWindows, "{}");

        Assert.Equal(AgentToolResultStatus.Succeeded, windows.Status);
        Assert.InRange(
            System.Text.Encoding.UTF8.GetByteCount(windows.Value.Content),
            1,
            DesktopAgentToolResultJson.MaximumResultBytes);
        using var document = JsonDocument.Parse(windows.Value.Content);
        Assert.True(document.RootElement.GetProperty("truncated").GetBoolean());
        var shown = document.RootElement.GetProperty("windows").EnumerateArray().ToArray();
        var observed = Assert.IsType<AgentDesktopResult.WindowList>(Assert.Single(fixture.Desktop.Responses));
        Assert.InRange(shown.Length, 1, observed.Windows.Count - 1);
        var lastShown = shown[^1].GetProperty("window_id").GetString()!;
        Assert.Equal(observed.Windows[shown.Length - 1].Id, lastShown);
        var firstCut = observed.Windows[shown.Length].Id;

        await AssertStaleBeforeAuthorityAsync(
            fixture,
            DesktopAgentToolNames.FocusWindow,
            $$"""{"window_id":"{{firstCut}}"}""");

        var focusing = fixture.CallExternalAsync(DesktopAgentToolNames.FocusWindow, $$"""{"window_id":"{{lastShown}}"}""");
        var approval = await ApproveNextAsync(fixture.Runtime, previous: null);
        AssertArgument(approval, "window_id", lastShown);
        Assert.Equal(AgentToolResultStatus.Succeeded, (await focusing.WaitAsync(TimeSpan.FromSeconds(10))).Status);
    }

    /// <summary>
    /// Shows that an external call is refused as a stale target before it
    /// asks for authority; only the one earlier observation reached it.
    /// </summary>
    private static async Task AssertStaleBeforeAuthorityAsync(
        DesktopRuntimeFixture fixture,
        string toolName,
        string arguments)
    {
        // A call that resolved would wait on its approval card instead.
        AssertRefusal(
            await fixture.CallExternalAsync(toolName, arguments).WaitAsync(TimeSpan.FromSeconds(10)),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        Assert.Single(fixture.Broker.DesktopRequests);
        Assert.Single(fixture.Desktop.Checked);
        Assert.Single(fixture.Desktop.Dispatched);
        Assert.Null(fixture.Runtime.Snapshot.PendingApproval);
    }

    /// <summary>
    /// Lists the applications and windows again, which names the targets of
    /// <c>desktop.close_app</c> and <c>desktop.focus_window</c>, and shows that
    /// both are still refused before authority until a snapshot is taken.
    /// </summary>
    private static async Task AssertListedAgainButStillStaleAsync(DesktopRuntimeFixture fixture)
    {
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await fixture.CallExternalAsync(DesktopAgentToolNames.ListApps, "{}")).Status);
        Assert.Equal(
            AgentToolResultStatus.Succeeded,
            (await fixture.CallExternalAsync(DesktopAgentToolNames.ListWindows, "{}")).Status);
        var authorityRequests = fixture.Broker.DesktopRequests.Length;

        // A call that resolved would wait on its approval card instead.
        AssertRefusal(
            await fixture.CallExternalAsync(DesktopAgentToolNames.CloseApp, """{"app":"TextEdit"}""")
                .WaitAsync(TimeSpan.FromSeconds(10)),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        AssertRefusal(
            await fixture.CallExternalAsync(DesktopAgentToolNames.FocusWindow, """{"window_id":"w-1"}""")
                .WaitAsync(TimeSpan.FromSeconds(10)),
            AgentDesktopStableCodes.StaleRef,
            retryable: true,
            requiresSnapshot: true);
        Assert.Equal(authorityRequests, fixture.Broker.DesktopRequests.Length);
        Assert.Null(fixture.Runtime.Snapshot.PendingApproval);
    }

    private static void AssertArgument(GovernedAgentApproval approval, string name, string value) =>
        Assert.Contains(
            approval.Presentation.Arguments,
            argument => string.Equals(argument.Name, name, StringComparison.Ordinal)
                && string.Equals(argument.DisplayValue, value, StringComparison.Ordinal));

    private static void AssertRefusal(
        AgentToolResult result,
        string stableCode,
        bool retryable,
        bool requiresSnapshot)
    {
        Assert.NotEqual(AgentToolResultStatus.Succeeded, result.Status);
        Assert.Equal(stableCode, result.StableCode);
        using var document = JsonDocument.Parse(result.Value.Content);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(stableCode, error.GetProperty("code").GetString());
        Assert.Equal(retryable, error.GetProperty("retryable").GetBoolean());
        Assert.Equal(
            requiresSnapshot,
            document.RootElement.TryGetProperty("required_action", out var action)
                && string.Equals(action.GetString(), "desktop_snapshot", StringComparison.Ordinal));
    }

    private static void AssertNothingReachedAuthority(DesktopRuntimeFixture fixture)
    {
        Assert.Empty(fixture.Broker.DesktopRequests);
        Assert.Empty(fixture.Desktop.Checked);
        Assert.Empty(fixture.Desktop.Dispatched);
        Assert.Null(fixture.Runtime.Snapshot.PendingApproval);
    }

    private static void AssertOnlyTheSnapshotReachedAuthority(DesktopRuntimeFixture fixture)
    {
        Assert.Equal(
            DesktopAgentToolNames.Snapshot,
            Assert.Single(fixture.Broker.DesktopRequests).ToolName);
        Assert.IsType<AgentDesktopRequest.Snapshot>(Assert.Single(fixture.Desktop.Checked).Request);
        Assert.IsType<AgentDesktopRequest.Snapshot>(Assert.Single(fixture.Desktop.Dispatched).Request);
        Assert.Null(fixture.Runtime.Snapshot.PendingApproval);
    }

    private static string[] AdvertisedDesktopTools(ScriptedProvider provider) =>
        [.. provider.Requests.ToArray()[0].Tools
            .Select(tool => tool.Name)
            .Where(DesktopAgentToolNames.Owns)];

    private static AgentToolResult ToolResultOf(ScriptedProvider provider, string providerCallId) =>
        Assert.Single(
                provider.Requests.ToArray()[^1].Messages,
                message => string.Equals(
                    message.ToolResult?.ProviderCallId,
                    providerCallId,
                    StringComparison.Ordinal))
            .ToolResult!;

    private static async Task<GovernedAgentApproval> ApproveNextAsync(
        GovernedAgentRuntime runtime,
        AgentApprovalId? previous)
    {
        var approval = await WaitForNewApprovalAsync(runtime, previous);
        Assert.True((await runtime.DecideAsync(approval.Id, approved: true, CancellationToken.None)).IsAccepted);
        return approval;
    }

    private static async Task<GovernedAgentApproval> WaitForNewApprovalAsync(
        GovernedAgentRuntime runtime,
        AgentApprovalId? previous)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            if (runtime.Snapshot.PendingApproval is { } approval && approval.Id != previous)
            {
                return approval;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The desktop approval did not arrive.");
            }

            await Task.Delay(10);
        }
    }

    private static async Task<GovernedAgentDesktopAccessRequest> WaitForDesktopAccessRequestAsync(
        GovernedAgentRuntime runtime)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            if (runtime.Snapshot.PendingDesktopAccess is { } request)
            {
                return request;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The desktop access card did not appear.");
            }

            await Task.Delay(10);
        }
    }

    private static async Task<GovernedAgentCapabilityRequest> WaitForCapabilityRequestAsync(
        GovernedAgentRuntime runtime)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            if (runtime.Snapshot.PendingCapabilityRequest is { } request)
            {
                return request;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The capability card did not appear.");
            }

            await Task.Delay(10);
        }
    }

    private static AgentPolicy DesktopPolicy(
        AgentPermission data = AgentPermission.Auto,
        AgentPermission interaction = AgentPermission.Ask,
        AgentPermission control = AgentPermission.Ask) =>
        AgentPolicy.Default with
        {
            Permissions = AgentPolicy.Default.Permissions
                .SetItem(AgentCapability.DesktopData, data)
                .SetItem(AgentCapability.DesktopInteraction, interaction)
                .SetItem(AgentCapability.DesktopControl, control),
        };

    private static AgentProviderEvent[] Batch(params ToolCall[] calls)
    {
        var events = new List<AgentProviderEvent> { new AgentProviderEvent.ResponseStarted() };
        for (var index = 0; index < calls.Length; index++)
        {
            events.Add(new AgentProviderEvent.ToolCallStarted(
                index,
                calls[index].ProviderCallId,
                ProviderToolName.FromInternal(calls[index].ToolName)));
            events.Add(new AgentProviderEvent.ToolCallArgumentsDelta(index, calls[index].Arguments));
            events.Add(new AgentProviderEvent.ToolCallCompleted(index));
        }

        events.Add(new AgentProviderEvent.ResponseCompleted(AgentProviderStopReason.ToolUse));
        return [.. events];
    }

    private static AgentProviderEvent[] Answer() =>
    [
        new AgentProviderEvent.ResponseStarted(),
        new AgentProviderEvent.TextDelta("Done."),
        new AgentProviderEvent.ResponseCompleted(AgentProviderStopReason.EndTurn),
    ];

    private sealed record ToolCall(string ProviderCallId, string ToolName, string Arguments);

    public enum DesktopScope
    {
        Workspace,
        ExactPanel,
    }

    private sealed class DesktopRuntimeFixture : IAsyncDisposable
    {
        private const string ProviderProfile = "desktop-provider";

        private readonly AgentCapabilityBroker _innerBroker;
        private readonly AgentPolicy _policy;
        private readonly DesktopContextProxy _context;

        private DesktopRuntimeFixture(
            ISessionHostClient sessionHost,
            DesktopContextProxy context,
            ScriptedProvider provider,
            AgentPolicy policy,
            bool withDesktopHost,
            TimeProvider time,
            bool external)
        {
            _context = context;
            _policy = AgentPolicyResolver.Resolve(policy);
            _innerBroker = new AgentCapabilityBroker(
                BuiltInAgentTools.Catalog,
                Audit,
                time);
            Broker = new RecordingBroker(_innerBroker);
            var composer = new AgentDesktopActionComposer();
            Desktop = new RecordingDesktopHost(Broker, composer, context, time);
            var workspace = context.Target as AgentTarget.Workspace;
            Runtime = new GovernedAgentRuntime(
                sessionHost,
                Broker,
                new RejectingTerminalHost(),
                agentBrowserHost: null,
                agentFileHost: null,
                new AgentTerminalActionComposer(),
                browserComposer: null,
                fileComposer: null,
                BuiltInAgentTools.Catalog,
                new FixedProviderResolver(provider),
                new TestApprovalPrincipal(new ClientId("desktop-test-client")),
                time,
                _policy,
                workspaceId: external ? workspace!.WorkspaceId : null,
                agentWorkspaceLayoutHost: external ? new RejectingLayoutHost() : null,
                workspaceLayoutComposer: external ? new AgentWorkspaceLayoutActionComposer() : null,
                agentDesktopHost: withDesktopHost ? Desktop : null,
                desktopComposer: withDesktopHost ? composer : null);
            if (external)
            {
                // The trusted desktop attachment an external MCP operator
                // reaches the runtime through; it answers with this policy.
                Runtime.AttachWorkspaceLayoutPort(new ExternalOperatorPort(
                    workspace!,
                    _policy.SelectPrimaryModel(ProviderProfile, "desktop-default-model")));
            }
        }

        public RecordingBroker Broker { get; }

        public RecordingAuditStore Audit { get; } = new();

        public RecordingDesktopHost Desktop { get; }

        public GovernedAgentRuntime Runtime { get; }

        public static DesktopRuntimeFixture Create(
            ScriptedProvider provider,
            AgentPolicy policy,
            DesktopScope scope = DesktopScope.Workspace,
            bool withDesktopHost = true,
            TimeProvider? time = null,
            bool external = false)
        {
            var clock = time ?? TimeProvider.System;
            var sessionHost = DispatchProxy.Create<ISessionHostClient, DesktopContextProxy>();
            var context = (DesktopContextProxy)(object)sessionHost;
            context.Initialize(scope, clock);
            return new DesktopRuntimeFixture(
                sessionHost,
                context,
                provider,
                policy,
                withDesktopHost,
                clock,
                external);
        }

        /// <summary>One call from an external MCP operator, outside any chat run.</summary>
        public async Task<AgentToolResult> CallExternalAsync(string toolName, string arguments)
        {
            using var document = JsonDocument.Parse(arguments);
            return await Runtime.CallExternalToolAsync(toolName, document.RootElement.Clone(), CancellationToken.None);
        }

        public Task<GovernedAgentSendResult> SendAsync(
            string message,
            CancellationToken cancellationToken = default) =>
            Runtime.SendAsync(
                    new GovernedAgentPrompt(
                        new AiProviderProfileId(ProviderProfile),
                        message,
                        _context.Target,
                        _policy.SelectPrimaryModel(ProviderProfile, "desktop-default-model")),
                    cancellationToken)
                .AsTask();

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            await _innerBroker.DisposeAsync();
        }
    }

    /// <summary>
    /// The session host as the runtime sees it: one workspace with one
    /// terminal panel, inspected either as the whole workspace or as that
    /// panel alone.
    /// </summary>
    public class DesktopContextProxy : DispatchProxy
    {
        private static readonly WindowInstanceId WindowId = new("desktop-window");
        private static readonly WorkspaceInstanceId WorkspaceId = new("desktop-workspace");
        private static readonly TabInstanceId TabId = new("desktop-tab");
        private static readonly PanelInstanceId PanelId = new("desktop-panel");
        private static readonly SessionId TerminalSessionId = new("desktop-terminal-session");

        public AgentTarget Target { get; private set; } = null!;

        private TimeProvider _time = TimeProvider.System;

        public void Initialize(DesktopScope scope, TimeProvider time)
        {
            _time = time;
            Target = scope switch
            {
                DesktopScope.Workspace => new AgentTarget.Workspace(WindowId, WorkspaceId),
                DesktopScope.ExactPanel => new AgentTarget.Panel(WindowId, WorkspaceId, TabId, PanelId),
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            };
        }

        public AgentContextSnapshot CurrentContext()
        {
            var graph = new WorkspaceGraphSnapshot(
                WindowId,
                new WorkspaceInstance(
                    WorkspaceId,
                    "Desktop workspace",
                    [
                        new TabInstance(
                            TabId,
                            "Operations",
                            [new PanelInstance(PanelId, PanelKind.Terminal, "Shell", TerminalSessionId)],
                            PanelId),
                    ],
                    TabId),
                revision: 1,
                lastSequence: 1);
            var descriptor = new SessionDescriptor(
                TerminalSessionId,
                PanelKind.Terminal,
                SessionLifecycle.Active,
                SessionHealth.Healthy,
                new SessionOwner(HostMode.Desktop, WindowId, WorkspaceId, TabId, PanelId),
                CapabilitySet.Empty,
                Revision: 1,
                HasActiveWork: false,
                StatusDetail: "Ready");
            return new AgentContextSnapshot(
                Target,
                [AgentContextPanel.ForGraphPanel(graph, TabId, PanelId, descriptor)],
                _time.GetUtcNow());
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(ISessionHostClient.InspectAgentContextAsync)
                    when args is [AgentContextRequest request, OperationContext _, CancellationToken cancellationToken] =>
                    InspectAsync(request, cancellationToken),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private ValueTask<HostResult<AgentContextSnapshot>> InspectAsync(
            AgentContextRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Target != Target)
            {
                return ValueTask.FromResult(
                    HostResult<AgentContextSnapshot>.Fail(
                        HostError.Create(HostErrorCode.NotFound, "The desktop test target is unavailable."),
                        1));
            }

            var snapshot = CurrentContext();
            return ValueTask.FromResult(
                HostResult<AgentContextSnapshot>.Succeed(snapshot, snapshot.Revision));
        }
    }

    /// <summary>
    /// A session host that answers like the real one: it guards the target,
    /// consumes the one-action permit, records one outcome and reports the
    /// closed desktop codes. It records every call so a test can show what
    /// never reached it.
    /// </summary>
    private sealed class RecordingDesktopHost(
        IAgentCapabilityBroker broker,
        AgentDesktopActionComposer composer,
        DesktopContextProxy context,
        TimeProvider time) : IAgentDesktopSessionHost
    {
        private const uint ProcessId = 4100;
        private static readonly AgentDesktopApplicationIdentity TextEditIdentity = new(
            AgentDesktopApplicationIdentityKind.BundleIdentifier,
            "com.apple.TextEdit");

        private readonly ConcurrentQueue<HostError> _mutationFailures = [];
        private readonly ConcurrentQueue<AgentDesktopAccessState> _accessScript = [];
        private int _accessChecks;
        private int _snapshotCount;
        private Action? _beforeNextConsume;

        public AgentDesktopAccessState Access { get; set; } = AgentDesktopAccessState.Granted;

        public Exception? AccessFailure { get; set; }

        public AgentDesktopFailure? TargetRefusal { get; set; }

        public Exception? TargetCheckFailure { get; set; }

        /// <summary>Makes every snapshot report an unsaved-changes sheet.</summary>
        public bool ShowSheet { get; set; }

        /// <summary>Makes every snapshot a full tree of maximum-width, escape-heavy text.</summary>
        public bool HostileSnapshot { get; set; }

        /// <summary>Makes every window listing the longest list of escape-heavy titles.</summary>
        public bool HostileWindows { get; set; }

        /// <summary>What an element action reports as delivered.</summary>
        public AgentDesktopDelivery ElementDelivery { get; set; } = AgentDesktopDelivery.DeliveredVerified;

        /// <summary>Every successful result, in the order the host returned it.</summary>
        public ConcurrentQueue<AgentDesktopResult> Responses { get; } = [];

        public int AccessChecks => Volatile.Read(ref _accessChecks);

        public ConcurrentQueue<AgentDesktopAction> Checked { get; } = [];

        public ConcurrentQueue<AgentDesktopAction> Dispatched { get; } = [];

        public ConcurrentQueue<AgentRunId> Released { get; } = [];

        public void FailNextMutation(HostError error) => _mutationFailures.Enqueue(error);

        /// <summary>Answers the next access checks in order, then <see cref="Access"/> again.</summary>
        public void ScriptAccess(params AgentDesktopAccessState[] answers)
        {
            foreach (var answer in answers)
            {
                _accessScript.Enqueue(answer);
            }
        }

        /// <summary>Runs once, between the next mutation's authorization and its consumption.</summary>
        public void BeforeNextMutationConsume(Action action) => Volatile.Write(ref _beforeNextConsume, action);

        public ValueTask<AgentDesktopAccessState> GetAgentDesktopAccessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _accessChecks);
            if (AccessFailure is { } failure)
            {
                throw failure;
            }

            return ValueTask.FromResult(_accessScript.TryDequeue(out var scripted) ? scripted : Access);
        }

        public ValueTask<AgentDesktopFailure?> CheckAgentDesktopTargetAsync(
            AgentDesktopAction action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checked.Enqueue(action);
            return TargetCheckFailure is { } failure
                ? throw failure
                : ValueTask.FromResult(TargetRefusal);
        }

        public async ValueTask<HostResult<AgentDesktopResult>> RunAgentDesktopActionAsync(
            AgentAuthorizationId authorizationId,
            AgentDesktopAction action,
            CancellationToken cancellationToken)
        {
            if (action.Request.IsMutation
                && Interlocked.Exchange(ref _beforeNextConsume, null) is { } beforeConsume)
            {
                beforeConsume();
            }

            var consumed = await broker.ConsumeAsync(
                authorizationId,
                composer.BindForExecution(action, context.CurrentContext()),
                cancellationToken);
            if (consumed is AgentPermitResult.Denied
                {
                    Error.Code: AgentAuthorizationErrorCode.AuthorizationExpired
                        or AgentAuthorizationErrorCode.ApprovalExpired,
                })
            {
                // Reported the way the terminal, browser and web hosts report
                // a permit that expired before it could be consumed.
                return HostResult<AgentDesktopResult>.Fail(
                    HostError.Create(
                        HostErrorCode.DeadlineExceeded,
                        "The one-action desktop authorization has expired."),
                    1);
            }

            if (consumed is not AgentPermitResult.Granted granted)
            {
                return HostResult<AgentDesktopResult>.Fail(
                    HostError.Create(HostErrorCode.InvalidRequest, "The desktop authorization was rejected."),
                    1);
            }

            Dispatched.Enqueue(action);
            var failure = action.Request.IsMutation && _mutationFailures.TryDequeue(out var queued)
                ? queued
                : null;
            Assert.Null(await broker.CompleteAsync(
                granted.Permit,
                new AgentActionCompletion(
                    failure is null ? AgentActionOutcome.Succeeded : AgentActionOutcome.Failed,
                    failure?.StableCode ?? "desktop_test_completed",
                    time.GetUtcNow()),
                CancellationToken.None));
            if (failure is not null)
            {
                return HostResult<AgentDesktopResult>.Fail(failure, 1);
            }

            var response = Respond(action.Request);
            Responses.Enqueue(response);
            return HostResult<AgentDesktopResult>.Succeed(response, 1);
        }

        public void ReleaseAgentDesktopRun(AgentRunId runId) => Released.Enqueue(runId);

        private AgentDesktopResult Respond(AgentDesktopRequest request) => request switch
        {
            AgentDesktopRequest.ListApps => new AgentDesktopResult.AppList(
                [new AgentDesktopApplicationEntry(Text("TextEdit"), ProcessId, Text("com.apple.TextEdit"), TextEditIdentity)],
                truncated: false),
            AgentDesktopRequest.ListWindows => new AgentDesktopResult.WindowList(
                HostileWindows
                    ? HostileWindowEntries()
                    : [new AgentDesktopWindowEntry("w-1", Text("Untitled"), Text("TextEdit"), TextEditIdentity, ProcessId, "generation-4100", IsFocused: true)],
                truncated: false),
            AgentDesktopRequest.Snapshot => Snapshot($"s0{Interlocked.Increment(ref _snapshotCount)}"),
            AgentDesktopRequest.Wait => new AgentDesktopResult.WaitCompleted(Satisfied: true, ElapsedMilliseconds: 1),
            AgentDesktopRequest.ElementAction => new AgentDesktopResult.ActionDelivered(
                ElementDelivery,
                postRole: null,
                postValue: null,
                postStates: null),
            AgentDesktopRequest.FocusWindow =>
                new AgentDesktopResult.ActionDelivered(
                    AgentDesktopDelivery.DeliveredUnverified,
                    postRole: null,
                    postValue: null,
                    postStates: null),
            AgentDesktopRequest.LaunchApp => new AgentDesktopResult.AppLaunched(ProcessId, "w-1", Text("Untitled")),
            AgentDesktopRequest.CloseApp => new AgentDesktopResult.AppCloseRequested(
                AgentDesktopDelivery.DeliveredUnverified),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        private AgentDesktopResult.Snapshot Snapshot(string snapshotId)
        {
            AgentDesktopSnapshotNode[] nodes = HostileSnapshot
                ? HostileNodes(snapshotId)
                :
                [
                    Node(ShowSheet ? "sheet" : "window", "Untitled", reference: null),
                    Node("button", "Save", $"@{snapshotId}:e1"),
                    Node("securetextfield", "Password", $"@{snapshotId}:e2", "secure"),
                    Node("checkbox", "Wrap lines", $"@{snapshotId}:e3"),
                ];
            return new AgentDesktopResult.Snapshot(
                snapshotId,
                Text("TextEdit"),
                TextEditIdentity,
                ProcessId,
                "generation-4100",
                "w-1",
                Text("Untitled"),
                complete: true,
                identityUnproven: false,
                nodes,
                nodesTruncated: false,
                refCount: nodes.Count(node => node.Reference is not null));
        }

        private static AgentDesktopSnapshotNode[] HostileNodes(string snapshotId)
        {
            var wide = new string('é', AgentDesktopDisplayText.MaximumTextBytes / 2);
            return [.. Enumerable.Range(1, AgentDesktopResult.MaximumSnapshotNodes)
                .Select(index => Node(wide, wide, $"@{snapshotId}:e{index}", "focused", "enabled"))];
        }

        // Each '<' is written as a six-byte escape, so the full list cannot
        // fit the result budget.
        private static AgentDesktopWindowEntry[] HostileWindowEntries()
        {
            var wide = new string('<', AgentDesktopDisplayText.MaximumTextBytes);
            return [.. Enumerable.Range(1, AgentDesktopResult.MaximumListEntries)
                .Select(index => new AgentDesktopWindowEntry(
                    $"w-{index}",
                    Text(wide),
                    Text("TextEdit"),
                    TextEditIdentity,
                    ProcessId,
                    "generation-4100",
                    IsFocused: index == 1))];
        }

        private static AgentDesktopSnapshotNode Node(
            string role,
            string name,
            string? reference,
            params string[] states) =>
            new(
                depth: 1,
                Text(role),
                Text(name),
                value: null,
                reference is null ? null : new AgentDesktopElementRef(reference),
                states,
                subtreeTruncated: false);

        private static AgentDesktopDisplayText Text(string value) =>
            AgentDesktopDisplayText.FromUntrusted(value, "unknown");
    }

    /// <summary>
    /// The real broker, with a record of what the runtime asked of it. An
    /// authorization request is the first thing a refused call must not make.
    /// </summary>
    private sealed class RecordingBroker(AgentCapabilityBroker inner) : IAgentCapabilityBroker
    {
        private readonly ConcurrentQueue<AgentActionProposal> _requests = [];

        public AgentActionProposal[] DesktopRequests =>
            [.. _requests.Where(proposal => DesktopAgentToolNames.Owns(proposal.ToolName))];

        public ConcurrentQueue<AgentRunCancellation> Cancellations { get; } = [];

        public ValueTask<AgentAuthorizationError?> RegisterRunAsync(
            AgentRunRegistration registration,
            CancellationToken cancellationToken) =>
            inner.RegisterRunAsync(registration, cancellationToken);

        public ValueTask<AgentAuthorizationError?> UpdateRunPolicyAsync(
            AgentRunPolicyUpdate update,
            CancellationToken cancellationToken) =>
            inner.UpdateRunPolicyAsync(update, cancellationToken);

        public ValueTask<AgentAuthorizationError?> RecordCapabilityRequestAuditAsync(
            AgentCapabilityRequestAuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            inner.RecordCapabilityRequestAuditAsync(auditEvent, cancellationToken);

        public ValueTask<AgentAuthorizationError?> CancelRunAsync(
            AgentRunCancellation cancellation,
            CancellationToken cancellationToken)
        {
            Cancellations.Enqueue(cancellation);
            return inner.CancelRunAsync(cancellation, cancellationToken);
        }

        public ValueTask<AgentAuthorizationResult> RequestAsync(
            AgentActionProposal proposal,
            CancellationToken cancellationToken)
        {
            _requests.Enqueue(proposal);
            return inner.RequestAsync(proposal, cancellationToken);
        }

        public ValueTask<AgentAuthorizationResult> DecideAsync(
            AgentApprovalDecision decision,
            CancellationToken cancellationToken) =>
            inner.DecideAsync(decision, cancellationToken);

        public ValueTask<AgentPermitResult> ConsumeAsync(
            AgentAuthorizationId authorizationId,
            AgentActionExecutionBinding currentBinding,
            CancellationToken cancellationToken) =>
            inner.ConsumeAsync(authorizationId, currentBinding, cancellationToken);

        public ValueTask<AgentAuthorizationError?> CompleteAsync(
            AgentActionPermit permit,
            AgentActionCompletion completion,
            CancellationToken cancellationToken) =>
            inner.CompleteAsync(permit, completion, cancellationToken);

        public ValueTask<AgentAuthorizationError?> RefuseAsync(
            AgentActionProposal proposal,
            string stableCode,
            CancellationToken cancellationToken) =>
            inner.RefuseAsync(proposal, stableCode, cancellationToken);
    }

    private sealed class RejectingLayoutHost : IAgentWorkspaceLayoutSessionHost
    {
        public ValueTask<HostResult<AgentWorkspaceLayoutReceipt>> RunAgentWorkspaceLayoutActionAsync(
            AgentAuthorizationId authorizationId,
            AgentWorkspaceLayoutAction action,
            IAgentWorkspaceLayoutMutationPort mutationPort,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("A desktop runtime test dispatched a layout action.");
    }

    /// <summary>
    /// The workspace port an external operator call resolves its policy
    /// through. It never mutates the layout.
    /// </summary>
    private sealed class ExternalOperatorPort(AgentTarget.Workspace workspace, AgentPolicy policy)
        : IAgentWorkspaceLayoutMutationPort
    {
        public WindowInstanceId WindowId => workspace.WindowId;

        public WorkspaceInstanceId WorkspaceId => workspace.WorkspaceId;

        public IReadOnlySet<PanelKind> SupportedPanelKinds { get; } = new HashSet<PanelKind>();

        public ValueTask<AgentPolicy?> ResolveExternalPolicyAsync(
            AgentTarget target,
            IReadOnlyList<AgentApprovalArgument> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<AgentPolicy?>(policy);
        }

        public ValueTask<AgentWorkspaceLayoutMutationResult> MutateAsync(
            AgentWorkspaceLayoutRequest request,
            long expectedWorkspaceRevision,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("A desktop runtime test mutated the layout.");
    }

    /// <summary>
    /// A clock that moves only when a test says so. Every timer fires once,
    /// when the clock passes its due time.
    /// </summary>
    private sealed class ManualDesktopTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _ = timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>
        /// Waits until something is waiting for the given moment, so moving
        /// the clock past it cannot race the code that starts the wait.
        /// </summary>
        public async Task WaitForTimerDueAtAsync(DateTimeOffset dueAt)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (true)
            {
                lock (_gate)
                {
                    if (_timers.Exists(timer => timer.DueAt == dueAt))
                    {
                        return;
                    }
                }

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new TimeoutException("Nothing waited for the expected moment.");
                }

                await Task.Delay(10);
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _now += elapsed;
                due = [.. _timers.Where(timer => timer.DueAt <= _now)];
                foreach (var timer in due)
                {
                    timer.DueAt = null;
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(
            ManualDesktopTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            /// <summary>Guarded by the owner's lock.</summary>
            public DateTimeOffset? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                    if (!owner._timers.Contains(this))
                    {
                        owner._timers.Add(this);
                    }
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._gate)
                {
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RejectingTerminalHost : IAgentTerminalSessionHost
    {
        public ValueTask<HostResult<AgentTerminalActionResult>> RunAgentTerminalActionAsync(
            AgentAuthorizationId authorizationId,
            AgentTerminalAction action,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("A desktop runtime test dispatched a terminal action.");
    }

    private sealed class FixedProviderResolver(IAgentProvider provider) : IAgentProviderResolver
    {
        private readonly FixedProviderBinding _binding = new(provider);

        // The title route names another profile; refusing it keeps every
        // recorded request a conversation request.
        public IAgentProviderBinding PinProvider(AiProviderProfileId profileId)
        {
            Assert.Equal(new AiProviderProfileId("desktop-provider"), profileId);
            return _binding;
        }
    }

    private sealed class FixedProviderBinding(IAgentProvider provider) : IAgentProviderBinding
    {
        public AiProviderProfileId ProfileId => new("desktop-provider");

        public long Revision => 1;

        public string DefaultModel => "desktop-default-model";

        public bool IsCurrent => true;

        public IAgentProvider CreateProvider(string model) => provider;
    }

    private sealed class TestApprovalPrincipal(ClientId clientId) : IAgentApprovalPrincipal
    {
        public ActorDescriptor Actor { get; } =
            new(new ActorId(clientId.Value), ActorKind.Human, "Test desktop user", clientId);
    }

    /// <summary>Plays one scripted provider response per request, in order.</summary>
    private sealed class ScriptedProvider(AgentProviderEvent[][] rounds) : IAgentProvider
    {
        private int _callCount;

        public ConcurrentQueue<AgentProviderRequest> Requests { get; } = [];

        public static ScriptedProvider Rounds(params AgentProviderEvent[][] rounds) => new(rounds);

        public async IAsyncEnumerable<AgentProviderEvent> StreamAsync(
            AgentProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            var call = Interlocked.Increment(ref _callCount);
            if (call > rounds.Length)
            {
                throw new InvalidOperationException("The desktop provider received an unexpected round.");
            }

            foreach (var providerEvent in rounds[call - 1])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return providerEvent;
                await Task.Yield();
            }
        }
    }

    /// <summary>Accepts every audit event and keeps it for the test to read.</summary>
    private sealed class RecordingAuditStore : IAuditStore
    {
        public ConcurrentQueue<AuditEventRecord> Events { get; } = [];

        public ValueTask<AuditStoreResult<Unit>> AppendAsync(
            AuditEventRecord auditEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Enqueue(auditEvent);
            return ValueTask.FromResult(AuditStoreResult<Unit>.Success(Unit.Value));
        }

        public ValueTask<AuditStoreResult<IReadOnlyList<AuditEventRecord>>> ListByCorrelationAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                AuditStoreResult<IReadOnlyList<AuditEventRecord>>.Success([]));
        }
    }
}
