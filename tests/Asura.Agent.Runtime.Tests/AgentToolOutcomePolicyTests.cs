using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

public sealed class AgentToolOutcomePolicyTests
{
    public static TheoryData<string, string> Outcomes =>
        new()
        {
            { "terminal_read_failed", nameof(AgentToolOutcomeDisposition.Continue) },
            { "kubernetes_mutation_outcome_unknown", nameof(AgentToolOutcomeDisposition.Reconcile) },
            {
                BrowserAgentToolResultJson.InteractionOutcomeUnknownStableCode,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            {
                FileAgentToolResultJson.FileMutationOutcomeUnknownStableCode,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            {
                GitAgentToolResultJson.MutationOutcomeUnknownStableCode,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            {
                McpAgentToolResultJson.OutcomeUnknownStableCode,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            {
                WorkspaceLayoutAgentToolResultJson.OutcomeUnknownStableCode,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            {
                AgentDesktopStableCodes.ActionOutcomeUnknown,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            {
                AgentDesktopStableCodes.AdapterFaulted,
                nameof(AgentToolOutcomeDisposition.Reconcile)
            },
            { AgentDesktopStableCodes.StaleRef, nameof(AgentToolOutcomeDisposition.Continue) },
            { AgentDesktopStableCodes.TimeoutBeforeDelivery, nameof(AgentToolOutcomeDisposition.Continue) },
            {
                AgentActionFailureCodes.CompletionAuditUnavailable,
                nameof(AgentToolOutcomeDisposition.Quarantine)
            },
            {
                McpAgentToolResultJson.ManifestChangedStableCode,
                nameof(AgentToolOutcomeDisposition.Quarantine)
            },
        };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void OutcomeClassificationIsExplicitAndStable(
        string stableCode,
        string expected)
    {
        Assert.Equal(expected, AgentToolOutcomePolicy.Classify(stableCode).ToString());
    }

    [Fact]
    public void EveryDesktopCodeThatLeavesTheDesktopUnknownReconcilesAndNoOtherDoes()
    {
        var codes = typeof(AgentDesktopStableCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Contains(AgentDesktopStableCodes.ActionOutcomeUnknown, codes, StringComparer.Ordinal);
        Assert.All(
            codes,
            code => Assert.Equal(
                AgentDesktopStableCodes.RequiresReconciliation(code)
                    ? AgentToolOutcomeDisposition.Reconcile
                    : AgentToolOutcomeDisposition.Continue,
                AgentToolOutcomePolicy.Classify(code)));
    }
}
