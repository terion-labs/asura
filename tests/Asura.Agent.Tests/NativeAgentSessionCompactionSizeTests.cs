using System.Collections.Immutable;
using Asura.Agent;
using Asura.Core;

namespace Asura.Agent.Tests;

public sealed partial class NativeAgentSessionTests
{
    [Fact]
    public async Task ImageHistoryCompactsDespiteLowProviderTokenUsageWithoutLosingTranscript()
    {
        var bytes = new byte[3 * 1024 * 1024];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(bytes, 0);
        ImmutableArray<AgentMessage> history =
        [
            new(AgentMessageRole.User, "Keep the screenshot findings", [new AgentImageAttachment("screen.png", "image/png", bytes)]),
            AgentMessage.Assistant("The screenshot findings", [], usage: new AgentTokenUsage(100, 10)),
            new(AgentMessageRole.User, "Current question"),
            new(AgentMessageRole.Assistant, "Current answer"),
        ];
        var session = CreateSession(history);
        var originalTranscript = session.Snapshot().Transcript;
        Assert.True(session.EstimateContextUsage().UsesProviderReportedUsage);
        Assert.True(session.EstimateContextUsage().EstimatedTokens < 20_000);
        var compactor = new ImmediateCompactor(new AgentMessage(AgentMessageRole.Summary, "Screenshot findings retained"));

        var result = await session.CompactAsync(1_000_000, new AgentCompactionSettings(), compactor, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(originalTranscript.ToArray(), session.Snapshot().Transcript.ToArray());
        Assert.True(session.EstimateContextUsage().EstimatedBytes < AgentContextWindowPolicy.MaximumHistoryBytes);
        Assert.Equal(AgentMessageRole.Summary, session.Snapshot().Conversation[0].Role);
        Assert.Equal("Current answer", session.Snapshot().Conversation[^1].Content);
        Assert.True((await session.RunTurnAsync("Continue", [], TextProvider("Continued"), CancellationToken.None)).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForkWithToolHistoryCanRestoreBeforeItsFirstNewTurn(bool legacyZeroCounters)
    {
        var source = CreateSession();
        var tools = ImmutableArray.Create(Tool("terminal.read_screen"));
        var turn = await source.RunTurnAsync("Read", tools, ToolProvider("terminal.read_screen", "{}"), CancellationToken.None);
        var proposal = Assert.Single(turn.ToolProposals);
        Assert.Null(source.CommitToolResults(proposal.Generation, [SuccessJson(proposal, "{}")], tools));
        Assert.True((await source.ContinueToolTurnAsync(tools, TextProvider("Read complete"), CancellationToken.None)).Succeeded);
        var fork = new NativeAgentSession(AgentRunId.New(), source.Snapshot().Transcript);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(fork.CaptureCheckpoint().Checkpoint);

        Assert.True(checkpoint.Generation >= proposal.Generation);
        if (legacyZeroCounters)
        {
            checkpoint = new AgentSessionCheckpoint(checkpoint.RunId, checkpoint.SchemaVersion,
                0, checkpoint.Revision, checkpoint.PayloadJson, checkpoint.UpdatedAt);
        }
        var restored = NativeAgentSession.RestoreCheckpoint(checkpoint);
        Assert.True(restored.Succeeded);
        var continued = await restored.Session!.RunTurnAsync("Continue", [], TextProvider("Done"), CancellationToken.None);
        Assert.True(continued.Succeeded);
        Assert.True(restored.Session.Snapshot().Generation > proposal.Generation);
    }
}
