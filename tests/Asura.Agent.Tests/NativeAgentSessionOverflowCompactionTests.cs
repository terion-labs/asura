using System.Collections.Immutable;
using Asura.Agent;
using Asura.Core;

namespace Asura.Agent.Tests;

/// <summary>
/// A long chat failed once the provider refused it as longer than the model
/// reads, and compacting it by hand failed too: the whole history was sent to
/// be summarized in one request, which the provider refused for the same
/// reason. Overflow compaction keeps the recent messages word for word and
/// folds the older history from its oldest end, a bounded part at a time.
/// </summary>
public sealed partial class NativeAgentSessionTests
{
    // Each turn is about 2 000 estimated tokens: 1 000 for the question, 1 000 for the answer.
    private static ImmutableArray<AgentMessage> LongHistory(int turns) =>
        [
            .. Enumerable.Range(1, turns).SelectMany(turn => new[]
            {
                new AgentMessage(AgentMessageRole.User, $"question {turn} " + new string('q', 4_000)),
                new AgentMessage(AgentMessageRole.Assistant, $"answer {turn} " + new string('a', 4_000)),
            }),
        ];

    [Fact]
    public async Task An_overflow_compacts_even_when_the_local_estimate_says_the_chat_fits()
    {
        var session = CreateSession(LongHistory(6));
        Assert.False(session.EstimateContextUsage().RequiresCompaction(
            AgentContextWindowPolicy.FallbackContextWindowTokens,
            new AgentCompactionSettings()));
        var compactor = new ImmediateCompactor(new AgentMessage(AgentMessageRole.Summary, "Earlier turns"));

        var result = await session.CompactForOverflowAsync(3_000, 1_000_000, compactor, CancellationToken.None);

        Assert.True(result.Succeeded);
        var conversation = session.Snapshot().Conversation;
        Assert.Equal(AgentMessageRole.Summary, conversation[0].Role);
        Assert.StartsWith("answer 6", conversation[^1].Content, StringComparison.Ordinal);
        Assert.StartsWith("question 1", compactor.LastRequest!.Messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            compactor.LastRequest.Messages,
            message => message.Content.StartsWith("answer 6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_history_too_long_to_summarize_at_once_is_folded_from_its_oldest_end()
    {
        var session = CreateSession(LongHistory(8));
        var compactor = new ImmediateCompactor(new AgentMessage(AgentMessageRole.Summary, "Oldest turns"));

        var result = await session.CompactForOverflowAsync(3_000, 4_500, compactor, CancellationToken.None);

        Assert.True(result.Succeeded);
        var folded = compactor.LastRequest!.Messages;
        Assert.StartsWith("question 1", folded[0].Content, StringComparison.Ordinal);
        Assert.StartsWith("answer 2", folded[^1].Content, StringComparison.Ordinal);
        var conversation = session.Snapshot().Conversation;
        Assert.Equal(AgentMessageRole.Summary, conversation[0].Role);
        Assert.StartsWith("question 3", conversation[1].Content, StringComparison.Ordinal);
        Assert.StartsWith("answer 8", conversation[^1].Content, StringComparison.Ordinal);

        // The next pass folds the previous summary with the next oldest turns.
        var next = new ImmediateCompactor(new AgentMessage(AgentMessageRole.Summary, "More turns"));
        Assert.True((await session.CompactForOverflowAsync(3_000, 4_500, next, CancellationToken.None)).Succeeded);
        Assert.Contains(next.LastRequest!.Messages, message => message.Content == "Oldest turns");
        Assert.StartsWith("answer 8", session.Snapshot().Conversation[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_compactor_that_cannot_fit_the_history_leaves_the_chat_unchanged()
    {
        var session = CreateSession(LongHistory(6));
        var before = session.Snapshot().Conversation;

        var result = await session.CompactForOverflowAsync(
            3_000,
            1_000_000,
            new TooLargeCompactor(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AgentCompactionErrorCode.InputTooLarge, result.ErrorCode);
        Assert.Equal(before.ToArray(), session.Snapshot().Conversation.ToArray());
        Assert.True((await session.RunTurnAsync("Continue", [], TextProvider("Continued"), CancellationToken.None)).Succeeded);
    }

    private sealed class TooLargeCompactor : IAgentConversationCompactor
    {
        public ValueTask<AgentMessage> CompactAsync(
            AgentCompactionRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AgentMessage>(new AgentCompactionInputTooLargeException());
    }
}
