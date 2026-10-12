using System.Globalization;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// The agent's records, dates, and panel nouns as the HIG review asked for
/// them: words instead of policy enum names, identifiers behind Details, the
/// reader's own date convention, and one noun per panel type.
/// </summary>
public sealed class AgentHigCopyTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 20, 21, 15, 3, TimeSpan.Zero);

    [Fact]
    public void Every_policy_value_has_words_of_its_own()
    {
        foreach (var capability in Enum.GetValues<AgentCapability>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AgentPolicyPresentation.CapabilityName(capability)));
        }

        foreach (var permission in Enum.GetValues<AgentPermission>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AgentPolicyPresentation.PermissionName(permission)));
        }

        foreach (var risk in Enum.GetValues<AgentActionRisk>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AgentPolicyPresentation.RiskName(risk)));
        }

        foreach (var source in Enum.GetValues<AgentAuthorizationSource>())
        {
            Assert.NotEqual(source.ToString(), AgentPolicyPresentation.AuthorizationSourceName(source), StringComparer.Ordinal);
        }

        foreach (var transition in Enum.GetValues<AgentRunPolicyTransition>())
        {
            Assert.NotEqual(transition.ToString(), AgentPolicyPresentation.PolicyTransitionName(transition), StringComparer.Ordinal);
        }

        foreach (var code in Enum.GetValues<AgentAuthorizationErrorCode>())
        {
            var words = AgentPolicyPresentation.ErrorName(code);
            Assert.DoesNotContain(code.ToString(), words, StringComparison.Ordinal);
        }

        Assert.Equal(
            Enum.GetValues<AuditOutcome>().Length,
            Enum.GetValues<AuditOutcome>().Select(AgentPolicyPresentation.OutcomeName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Canceled", AgentPolicyPresentation.OutcomeName(AuditOutcome.Cancelled));
        Assert.Equal("Full access", AgentPolicyPresentation.PermissionName(AgentPermission.Yolo));
    }

    [Fact]
    public void Recorded_action_reads_in_words_and_keeps_identifiers_in_Details()
    {
        using var culture = new CultureScope("en-US");
        var entry = AgentChatViewModel.CreateActionAuditEntry(Action(
            AgentAuthorizationSource.HumanApproval,
            errorCode: null,
            resultCode: "terminal_read_succeeded",
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Started, AuditOutcome.Succeeded]));

        Assert.Equal("Action", entry.Kind);
        Assert.Equal("Succeeded", entry.Outcome);
        Assert.Equal("Terminal read · Read only · Approved by you", entry.Summary);
        Assert.Equal("Requested → Approved → Started → Succeeded", entry.Timeline);
        Assert.Equal("Took 25 ms · 1 result", entry.Result);
        Assert.Equal(DisplayDates.FormatWithSeconds(Moment), entry.OccurredAt);
        Assert.Contains(":03", entry.OccurredAt, StringComparison.Ordinal);

        // None of the identifiers reaches the card or its spoken name.
        var visible = new[] { entry.Title, entry.Outcome, entry.Summary, entry.Timeline, entry.Result, entry.OccurredAt, entry.AccessibleName };
        foreach (var text in visible)
        {
            Assert.DoesNotContain(BuiltInAgentTools.TerminalReadScreen, text, StringComparison.Ordinal);
            Assert.DoesNotContain("terminal_read_succeeded", text, StringComparison.Ordinal);
            Assert.DoesNotContain("generation", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HumanApproval", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TerminalRead", text, StringComparison.Ordinal);
        }

        Assert.Equal(
            ["Tool ID", "Permission", "Policy generation", "Verified target", "Result code"],
            entry.Details.Select(fact => fact.Label),
            StringComparer.Ordinal);
        Assert.Equal(BuiltInAgentTools.TerminalReadScreen, entry.Details[0].Value);
        Assert.Equal("Auto", entry.Details[1].Value);
        Assert.Equal("3", entry.Details[2].Value);
        Assert.Equal("terminal_read_succeeded", entry.Details[4].Value);
    }

    [Fact]
    public void Recorded_action_that_did_not_run_says_why_in_words()
    {
        using var culture = new CultureScope("en-US");
        var entry = AgentChatViewModel.CreateActionAuditEntry(Action(
            authorizationSource: null,
            AgentAuthorizationErrorCode.ApprovalDenied,
            resultCode: null,
            [AuditOutcome.Requested, AuditOutcome.Denied]));

        Assert.Equal("Denied", entry.Outcome);
        Assert.Equal("Requested → Denied", entry.Timeline);
        Assert.Equal("Not run · You denied it", entry.Result);
        Assert.DoesNotContain("ApprovalDenied", entry.Result, StringComparison.Ordinal);
        var code = Assert.Single(entry.Details, fact => fact.Label == "Error code");
        Assert.Equal("ApprovalDenied", code.Value);
    }

    [Fact]
    public void Recorded_action_revoked_before_dispatch_says_why_and_not_how_long_it_took()
    {
        using var culture = new CultureScope("en-US");
        var entry = AgentChatViewModel.CreateActionAuditEntry(Action(
            AgentAuthorizationSource.AutoPolicy,
            AgentAuthorizationErrorCode.PolicyChanged,
            resultCode: "authority_revoked_before_dispatch",
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Started, AuditOutcome.Cancelled]));

        Assert.Equal("Canceled", entry.Outcome);
        Assert.Equal(
            $"Not run · {AgentPolicyPresentation.ErrorName(AgentAuthorizationErrorCode.PolicyChanged)}",
            entry.Result);
        Assert.DoesNotContain("Took", entry.Result, StringComparison.Ordinal);
        Assert.DoesNotContain("authority_revoked_before_dispatch", entry.Result, StringComparison.Ordinal);
        Assert.Equal(
            "authority_revoked_before_dispatch",
            Assert.Single(entry.Details, fact => fact.Label == "Result code").Value);
        Assert.Equal(
            "PolicyChanged",
            Assert.Single(entry.Details, fact => fact.Label == "Error code").Value);
    }

    [Fact]
    public void Recorded_action_closed_after_a_restart_says_its_result_is_unknown()
    {
        using var culture = new CultureScope("en-US");
        var entry = AgentChatViewModel.CreateActionAuditEntry(Action(
            AgentAuthorizationSource.AutoPolicy,
            errorCode: null,
            AgentAuditRecovery.RecoveryResultCode,
            [AuditOutcome.Requested, AuditOutcome.Approved, AuditOutcome.Started, AuditOutcome.Failed]));

        Assert.Equal("Failed", entry.Outcome);
        Assert.Equal("Result unknown · Asura quit before the action finished", entry.Result);
        Assert.Equal(
            AgentAuditRecovery.RecoveryResultCode,
            Assert.Single(entry.Details, fact => fact.Label == "Result code").Value);
    }

    [Fact]
    public void Every_reason_an_action_did_not_run_reads_after_Not_run()
    {
        foreach (var code in Enum.GetValues<AgentAuthorizationErrorCode>())
        {
            var words = AgentPolicyPresentation.ErrorName(code);
            Assert.DoesNotContain("didn't run", words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not run", words, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Recorded_permission_change_dates_follow_the_reader()
    {
        using var culture = new CultureScope("en-US");
        var entry = AgentChatViewModel.CreatePolicyAuditEntry(new AgentRunAuditPolicyEntry(
            AgentActionDigest.FromUtf8("policy"),
            AgentRunPolicyTransition.YoloEnabled,
            policyGeneration: 4,
            AgentActionDigest.FromUtf8("target"),
            Moment.AddHours(1),
            Moment));

        Assert.Equal("Full access turned on", entry.Title);
        Assert.Equal($"Access ends {DisplayDates.Format(Moment.AddHours(1))}", entry.Result);
        Assert.Equal(DisplayDates.FormatWithSeconds(Moment), entry.OccurredAt);
        Assert.False(entry.HasSummary);
        Assert.False(entry.HasTimeline);
        Assert.Equal(["Policy generation", "Verified target"], entry.Details.Select(fact => fact.Label), StringComparer.Ordinal);
        Assert.DoesNotContain("generation", entry.AccessibleName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Agent_expiry_uses_the_dates_Files_and_Git_use(string cultureName)
    {
        using var culture = new CultureScope(cultureName);
        var question = new AgentQuestionCardViewModel(new GovernedAgentQuestion(
            new AgentQuestionId("question-1"),
            "Which branch?",
            Moment));

        Assert.Equal(DisplayDates.Format(Moment), question.ExpiresAt);
        Assert.Equal(
            Moment.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(cultureName)),
            question.ExpiresAt);
    }

    [Fact]
    public void Panel_nouns_come_from_one_table_for_every_type()
    {
        foreach (var kind in Enum.GetValues<PanelKind>().Where(kind => kind != PanelKind.Placeholder))
        {
            Assert.Equal(PanelTypes.Name(kind), PanelTypeNames.Name(kind));
            Assert.NotEqual(PanelTypeNames.Panel, PanelTypeNames.Name(kind), StringComparer.Ordinal);
        }

        foreach (var kind in Enum.GetValues<ScreenPanelKind>())
        {
            Assert.Equal(PanelTypes.Name(kind), PanelTypeNames.Name(kind));
        }
    }

    private static AgentRunAuditActionEntry Action(
        AgentAuthorizationSource? authorizationSource,
        AgentAuthorizationErrorCode? errorCode,
        string? resultCode,
        IReadOnlyList<AuditOutcome> outcomes) =>
        new(
            AgentActionDigest.FromUtf8("action"),
            BuiltInAgentTools.TerminalReadScreen,
            AgentCapability.TerminalRead,
            AgentActionRisk.Observation,
            AgentPermission.Auto,
            AgentPolicyDecision.AuthorizedByAuto,
            authorizationSource,
            errorCode,
            resultCode,
            policyGeneration: 3,
            AgentActionDigest.FromUtf8("target"),
            executionDurationMilliseconds: 25,
            resultCount: 1,
            outcomes.Select((outcome, index) => new AgentRunAuditPhase(
                outcome,
                ActorKind.Agent,
                Moment.AddMilliseconds(index - outcomes.Count + 1))));
}
