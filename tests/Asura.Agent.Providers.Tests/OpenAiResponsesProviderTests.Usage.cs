using System.Text;
using Asura.Agent;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Agent.Providers.Tests;

/// <summary>
/// GPT-6 chats failed at random with "The AI provider returned an unsupported
/// or invalid response.": ChatGPT's Codex backend reports output tokens
/// without the reasoning before them, and an answer that reasoned more than it
/// wrote was discarded for counts that looked impossible.
/// </summary>
public sealed partial class OpenAiResponsesProviderTests
{
    [Fact]
    public async Task An_answer_that_reasoned_more_than_it_wrote_is_kept()
    {
        var result = await RunAgainstStreamAsync(ResponsesTextStreamWithUsage(
            "{\"input_tokens\":160767,\"output_tokens\":79,"
            + "\"input_tokens_details\":{\"cached_tokens\":150912},"
            + "\"output_tokens_details\":{\"reasoning_tokens\":2400}}"));

        Assert.True(result.Result.Succeeded, result.Result.ProviderFailure?.Message);
        var usage = result.Session.Snapshot().Conversation[^1].Usage!;
        Assert.Equal(160_767, usage.InputTokens);
        Assert.Equal(150_912, usage.CachedInputTokens);
        Assert.Equal(2_479, usage.OutputTokens);
        Assert.Equal(2_400, usage.ReasoningTokens);
    }

    [Fact]
    public async Task More_cached_than_sent_tokens_do_not_discard_the_answer()
    {
        var result = await RunAgainstStreamAsync(ResponsesTextStreamWithUsage(
            "{\"input_tokens\":100,\"output_tokens\":10,\"input_tokens_details\":{\"cached_tokens\":120}}"));

        Assert.True(result.Result.Succeeded, result.Result.ProviderFailure?.Message);
        Assert.Equal(100, result.Session.Snapshot().Conversation[^1].Usage!.CachedInputTokens);
    }

    [Fact]
    public async Task A_response_Asura_cannot_read_names_the_check_that_failed()
    {
        var builder = new StringBuilder();
        AppendEvent(
            builder,
            "response.created",
            "{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\",\"status\":\"in_progress\"}}");
        AppendEvent(
            builder,
            "response.output_item.done",
            "{\"type\":\"response.output_item.done\",\"output_index\":3,\"item\":{\"type\":\"message\",\"id\":\"msg-9\"}}");
        builder.Append("data: [DONE]\n\n");

        var result = await RunAgainstStreamAsync(builder.ToString());

        Assert.False(result.Result.Succeeded);
        Assert.Equal("ai_provider_protocol_error", result.Result.ProviderFailure?.StableCode);
        Assert.Contains("(Responses CompleteOutputItem:", result.Result.ProviderFailure!.Message, StringComparison.Ordinal);
    }

    private async Task<(AgentTurnResult Result, NativeAgentSession Session)> RunAgainstStreamAsync(string stream)
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        using var handler = new CapturingHandler(stream);
        using var factory = new AiProviderFactory(vault, handler);
        var session = new NativeAgentSession(new AgentRunId("usage"));
        var result = await session.RunTurnAsync("Continue.", [], factory.Create(profile), CancellationToken.None);
        return (result, session);
    }

    private static string ResponsesTextStreamWithUsage(string usageJson)
    {
        var builder = new StringBuilder();
        AppendEvent(
            builder,
            "response.created",
            "{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\",\"status\":\"in_progress\"}}");
        AppendEvent(
            builder,
            "response.output_text.delta",
            "{\"type\":\"response.output_text.delta\",\"item_id\":\"msg-1\",\"delta\":\"Answered.\"}");
        AppendEvent(
            builder,
            "response.completed",
            "{\"type\":\"response.completed\",\"response\":{\"id\":\"resp-1\",\"status\":\"completed\",\"usage\":"
            + usageJson
            + "}}");
        builder.Append("data: [DONE]\n\n");
        return builder.ToString();
    }
}
