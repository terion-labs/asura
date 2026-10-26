using System.Net;
using System.Text;
using Asura.Agent;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Agent.Providers.Tests;

/// <summary>
/// Every failure the response stream reported, and a connection that broke
/// mid-answer, read as "The AI provider is unavailable". A conversation longer
/// than the model reads was then never compacted, and a rate limit or an
/// exhausted quota looked like an outage. The stream's own reason is used now.
/// </summary>
public sealed partial class OpenAiResponsesProviderTests
{
    [Theory]
    [InlineData("response.failed", "{\"type\":\"response.failed\",\"response\":{\"id\":\"resp-1\",\"status\":\"failed\",\"error\":{\"code\":\"context_length_exceeded\",\"message\":\"Your input exceeds the context window of this model.\"}}}", "ai_provider_request_too_large", "Start a new chat")]
    [InlineData("error", "{\"type\":\"error\",\"code\":\"rate_limit_exceeded\",\"message\":\"Slow down.\"}", "ai_provider_rate_limited", "rate-limited")]
    [InlineData("error", "{\"type\":\"error\",\"error\":{\"type\":\"insufficient_quota\",\"message\":\"You exceeded your current quota.\"}}", "ai_provider_quota_exceeded", "quota")]
    [InlineData("response.failed", "{\"type\":\"response.failed\",\"response\":{\"id\":\"resp-1\",\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"Something went wrong.\"}}}", "ai_provider_unavailable", "(server_error)")]
    [InlineData("error", "{\"type\":\"error\",\"code\":\"invalid_prompt\",\"message\":\"Private request text.\"}", "ai_provider_request_rejected", "(invalid_prompt)")]
    [InlineData("error", "{\"type\":\"error\",\"message\":\"Private request text.\"}", "ai_provider_unavailable", "gave no reason")]
    public async Task A_failed_stream_reports_its_own_reason(
        string eventType,
        string payload,
        string expectedCode,
        string expectedMessage)
    {
        var builder = new StringBuilder();
        AppendEvent(
            builder,
            "response.created",
            "{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\",\"status\":\"in_progress\"}}");
        AppendEvent(builder, eventType, payload);

        var result = await RunAgainstStreamAsync(builder.ToString());

        Assert.False(result.Result.Succeeded);
        Assert.Equal(expectedCode, result.Result.ProviderFailure?.StableCode);
        Assert.Contains(expectedMessage, result.Result.ProviderFailure!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Private request text", result.Result.ProviderFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connection_that_drops_mid_answer_says_so()
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        var first = new StringBuilder();
        AppendEvent(
            first,
            "response.created",
            "{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\",\"status\":\"in_progress\"}}");
        using var handler = new DroppingHandler(Encoding.UTF8.GetBytes(first.ToString()));
        using var factory = new AiProviderFactory(vault, handler);
        var session = new NativeAgentSession(new AgentRunId("dropped"));

        var result = await session.RunTurnAsync("Continue.", [], factory.Create(profile), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ai_provider_connection_lost", result.ProviderFailure?.StableCode);
        Assert.Contains("dropped before the response finished", result.ProviderFailure!.Message, StringComparison.Ordinal);
    }

    /// <summary>Sends the first event, then breaks the connection.</summary>
    private sealed class DroppingHandler(byte[] prefix) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new StreamContent(new DroppingStream(prefix));
            content.Headers.ContentType = new("text/event-stream") { CharSet = "utf-8" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            });
        }
    }

    private sealed class DroppingStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= prefix.Length)
            {
                throw new IOException("The response ended prematurely.");
            }

            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer, offset, count).GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
