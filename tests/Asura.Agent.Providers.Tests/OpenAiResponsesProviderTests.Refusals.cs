using System.Net;
using System.Text;
using Asura.Agent;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Agent.Providers.Tests;

/// <summary>
/// A long agent chat failed with "The AI provider returned an unsupported or
/// invalid response." when the provider had simply refused the request: every
/// 400 became a protocol error, its body unread, so a conversation that had
/// outgrown the model's context window gave no hint of what to do. A stream
/// event the parser didn't know ended the run the same way.
/// </summary>
public sealed partial class OpenAiResponsesProviderTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest,
        "{\"error\":{\"message\":\"Your input exceeds the context window of this model.\",\"type\":\"invalid_request_error\",\"code\":\"context_length_exceeded\"}}")]
    [InlineData(HttpStatusCode.BadRequest,
        "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"prompt is too long: 290000 tokens > 272000 maximum\"}}")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "")]
    public async Task A_conversation_that_outgrew_the_model_is_reported_as_too_large(
        HttpStatusCode status,
        string body)
    {
        var result = await RunAgainstRefusalAsync(status, body);

        Assert.False(result.Succeeded);
        Assert.Equal("ai_provider_request_too_large", result.ProviderFailure?.StableCode);
        Assert.Contains("Start a new chat", result.ProviderFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_refusal_names_the_providers_code_and_none_of_its_text()
    {
        var result = await RunAgainstRefusalAsync(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Unsupported parameter: secret-ish detail\",\"type\":\"invalid_request_error\",\"code\":\"unsupported_parameter\"}}");

        Assert.False(result.Succeeded);
        Assert.Equal("ai_provider_request_rejected", result.ProviderFailure?.StableCode);
        Assert.Contains("(unsupported_parameter)", result.ProviderFailure!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-ish", result.ProviderFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_without_a_readable_body_is_still_a_refusal()
    {
        var result = await RunAgainstRefusalAsync(HttpStatusCode.BadRequest, "<html>Bad Request</html>");

        Assert.False(result.Succeeded);
        Assert.Equal("ai_provider_request_rejected", result.ProviderFailure?.StableCode);
    }

    [Fact]
    public async Task A_stream_event_this_version_does_not_know_is_skipped()
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        using var handler = new CapturingHandler(ResponsesTextStreamWithNewEvent("still answered"));
        using var factory = new AiProviderFactory(vault, handler);
        var session = new NativeAgentSession(new AgentRunId("unknown-stream-event"));

        var result = await session.RunTurnAsync("Hello.", [], factory.Create(profile), CancellationToken.None);

        Assert.True(result.Succeeded, result.ProviderFailure?.Message);
        Assert.Equal("still answered", session.Snapshot().Conversation[^1].Content);
    }

    [Fact]
    public void Refusal_bodies_keep_only_a_safe_identifier()
    {
        var refusal = AiProviderRequestRefusal.Parse(
            Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"has spaces and <tags>\",\"message\":\"x\"}}"));

        Assert.False(refusal.IsContextOverflow);
        Assert.Null(refusal.Code);
    }

    private async Task<AgentTurnResult> RunAgainstRefusalAsync(HttpStatusCode status, string body)
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        using var handler = new RefusingHandler(status, body);
        using var factory = new AiProviderFactory(vault, handler);
        var session = new NativeAgentSession(new AgentRunId("refused-request"));
        return await session.RunTurnAsync("Continue.", [], factory.Create(profile), CancellationToken.None);
    }

    private static string ResponsesTextStreamWithNewEvent(string value)
    {
        var builder = new StringBuilder();
        AppendEvent(
            builder,
            "response.created",
            "{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\",\"status\":\"in_progress\"}}");
        AppendEvent(
            builder,
            "response.context_compaction.started",
            "{\"type\":\"response.context_compaction.started\",\"sequence_number\":2}");
        AppendEvent(
            builder,
            "response.output_text.delta",
            "{\"type\":\"response.output_text.delta\",\"item_id\":\"msg-1\",\"delta\":"
            + System.Text.Json.JsonSerializer.Serialize(value)
            + "}");
        AppendEvent(
            builder,
            "response.completed",
            "{\"type\":\"response.completed\",\"response\":{\"id\":\"resp-1\",\"status\":\"completed\"}}");
        builder.Append("data: [DONE]\n\n");
        return builder.ToString();
    }

    private sealed class RefusingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
