using System.Collections.Concurrent;
using Asura.Agent;
using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

/// <summary>
/// A long chat failed for good once the provider refused it as longer than
/// the model reads, and compacting it by hand failed the same way: the history
/// sent to be summarized was itself too long. A refusal now compacts and asks
/// again, and a history too long to summarize at once is folded from its
/// oldest end while the recent messages stay word for word.
/// </summary>
public sealed partial class GovernedAgentRuntimeTests
{
    private const string OverflowSummary =
        "## Goal\nContinue the task.\n\n## Critical Context\n- Earlier turns were summarized.";

    [Fact]
    public async Task A_provider_refusal_for_length_compacts_and_answers_the_same_message()
    {
        var provider = new LengthLimitedProvider(maximumCharacters: 40_000);
        await using var fixture = new RuntimeFixture(provider.Round);
        for (var turn = 1; turn <= 6; turn++)
        {
            Assert.True((await fixture.Runtime.SendAsync(
                fixture.Prompt(LongPrompt(turn)),
                CancellationToken.None)).IsSuccess);
        }

        var result = await fixture.Runtime.SendAsync(fixture.Prompt("Latest question"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(provider.Refusals > 0);
        var answered = provider.Round.Requests.Last();
        Assert.Contains(answered.Messages, message => message.Role == AgentMessageRole.Summary);
        Assert.Equal("Latest question", answered.Messages.Last(message => message.Role == AgentMessageRole.User).Content);
        Assert.Contains(fixture.Runtime.Snapshot.Messages, message => message.Content == LongPrompt(1));
        Assert.Equal("Latest question", fixture.Runtime.Snapshot.Messages[^2].Content);
    }

    [Fact]
    public async Task Manual_compaction_of_a_history_too_long_to_summarize_at_once_folds_it_in_parts()
    {
        var provider = new LengthLimitedProvider(maximumCharacters: 1_000_000, maximumCompactionCharacters: 60_000);
        await using var fixture = new RuntimeFixture(provider.Round);
        fixture.ProviderResolver.Binding.ContextWindowTokenLimit = 1_000_000;
        for (var turn = 1; turn <= 20; turn++)
        {
            Assert.True((await fixture.Runtime.SendAsync(
                fixture.Prompt(LongPrompt(turn)),
                CancellationToken.None)).IsSuccess);
        }

        var history = fixture.Runtime.Snapshot.Messages.Select(message => message.Content).ToArray();
        var refusedBefore = provider.Refusals;

        var result = await fixture.Runtime.CompactAsync(CancellationToken.None);

        Assert.Equal(0, refusedBefore);
        Assert.True(result.IsSuccess, result.Message);
        Assert.True(provider.Refusals > 1);
        Assert.Equal(history, fixture.Runtime.Snapshot.Messages.Select(message => message.Content), StringComparer.Ordinal);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("Continue"), CancellationToken.None)).IsSuccess);
        var next = provider.Round.Requests.Last();
        Assert.Contains(next.Messages, message => message.Role == AgentMessageRole.Summary);
        Assert.DoesNotContain(next.Messages, message => message.Content == LongPrompt(1));
        Assert.Contains(next.Messages, message => message.Content == LongPrompt(20));
    }

    private static string LongPrompt(int turn) => $"Question {turn}: " + new string('q', 4_000);

    /// <summary>
    /// Answers every request that fits, and refuses any longer than the
    /// model reads as a real provider does. A compaction request can have a
    /// smaller limit, as a compaction model with a smaller window has.
    /// </summary>
    private sealed class LengthLimitedProvider
    {
        private int _refusals;

        public LengthLimitedProvider(int maximumCharacters, int? maximumCompactionCharacters = null)
        {
            Round = new ProviderRound((_, request) =>
            {
                var compaction = request.Messages.Any(message =>
                    message.Content.StartsWith("<conversation>", StringComparison.Ordinal));
                var characters = request.Messages.Sum(message => (long)message.Content.Length);
                if (characters > (compaction ? maximumCompactionCharacters ?? maximumCharacters : maximumCharacters))
                {
                    Interlocked.Increment(ref _refusals);
                    throw new TestProviderException(
                        "ai_provider_request_too_large",
                        "The conversation is longer than this model can read. Start a new chat.");
                }

                return ProviderRound.Answer(compaction ? OverflowSummary : "Answer " + new string('a', 4_000));
            });
        }

        public ProviderRound Round { get; }

        public int Refusals => Volatile.Read(ref _refusals);

        public ConcurrentQueue<AgentProviderRequest> Requests => Round.Requests;
    }
}
