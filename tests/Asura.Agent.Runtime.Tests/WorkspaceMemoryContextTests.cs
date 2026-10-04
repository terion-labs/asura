using System.Collections.Immutable;
using System.Text.Json;
using Asura.Agent;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

public sealed class WorkspaceMemoryContextTests
{
    [Theory]
    [InlineData(false, 1, 4)]
    [InlineData(true, 2, 4)]
    [InlineData(true, 1, 5)]
    public void ChangedOrDisabledMemoryRemovesOldContentAndArgumentsWithoutBreakingCallPairing(bool enabled, long generation, long revision)
    {
        using var args = JsonDocument.Parse("""{"body":"old sensitive advice"}""");
        var call = new AgentToolProposal("proposal", 1, "call", "memory.save", args.RootElement);
        var result = new AgentToolResult(call, AgentToolResultStatus.Succeeded, "memory_saved",
            AgentToolResultValue.FromJson("""{"state":{"generation":1,"revision":4},"note":{"body":"old sensitive advice"}}"""u8.ToArray()));
        ImmutableArray<AgentMessage> original = [AgentMessage.Assistant("", [call]), AgentMessage.FromToolResult(result)];
        var projected = GovernedAgentRuntime.ProjectMemoryResults(original, new(enabled, true, generation, revision, 0));
        Assert.Equal("{}", projected[0].ToolCalls[0].Arguments.GetRawText());
        Assert.Equal("call", projected[1].ToolResult!.ProviderCallId);
        Assert.Equal("memory_context_expired", projected[1].ToolResult!.StableCode);
        Assert.DoesNotContain("old sensitive advice", projected[1].Content, StringComparison.Ordinal);
        Assert.Contains("old sensitive advice", original[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentMemoryAndOrdinaryToolResultsRemainAvailable()
    {
        using var args = JsonDocument.Parse("{}");
        var memory = new AgentToolProposal("p1", 1, "call1", "memory.search", args.RootElement);
        var terminal = new AgentToolProposal("p2", 1, "call2", "terminal.read", args.RootElement);
        var recalled = AgentMessage.FromToolResult(new(memory, AgentToolResultStatus.Succeeded, "memory_retrieved",
            AgentToolResultValue.FromJson("""{"state":{"generation":1,"revision":4},"notes":[]}"""u8.ToArray())));
        var output = AgentMessage.FromToolResult(new(terminal, AgentToolResultStatus.Succeeded, "tool_succeeded", AgentToolResultValue.FromText("output")));
        var projected = GovernedAgentRuntime.ProjectMemoryResults([recalled, output], new(true, true, 1, 4, 0));
        Assert.Same(recalled, projected[0]); Assert.Same(output, projected[1]);
    }
}
