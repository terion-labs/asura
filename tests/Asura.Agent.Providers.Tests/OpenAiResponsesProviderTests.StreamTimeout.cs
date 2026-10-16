using System.Net;
using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Agent.Providers.Tests;

/// <summary>
/// Compacting a long chat failed with no reason given: the provider stream was
/// cut off a fixed time after it started, so a summary still arriving word by
/// word was abandoned as a timeout. The limit now bounds silence only.
/// </summary>
public sealed partial class OpenAiResponsesProviderTests
{
    private static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task An_answer_that_keeps_arriving_outlasts_the_silence_limit()
    {
        var deltas = Enumerable.Range(0, 10).Select(index => $"part{index} ").ToArray();
        var result = await RunAgainstTrickleAsync(TimeSpan.FromMilliseconds(250), deltas, stallAfterFirst: false);

        Assert.True(result.Result.Succeeded, result.Result.ProviderFailure?.Message);
        Assert.Equal(string.Concat(deltas), result.Session.Snapshot().Conversation[^1].Content);
    }

    [Fact]
    public async Task A_stream_that_falls_silent_still_times_out()
    {
        var result = await RunAgainstTrickleAsync(TimeSpan.Zero, ["first "], stallAfterFirst: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("ai_provider_timeout", result.Result.ProviderFailure?.StableCode);
    }

    private async Task<(AgentTurnResult Result, NativeAgentSession Session)> RunAgainstTrickleAsync(
        TimeSpan gap,
        string[] deltas,
        bool stallAfterFirst)
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        using var handler = new TrickleHandler(TrickleEvents(deltas), gap, stallAfterFirst);
        using var factory = new AiProviderFactory(
            vault,
            handler,
            new AiProviderRuntimeLimits(streamTimeout: SilenceLimit));
        var session = new NativeAgentSession(new AgentRunId("trickle"));
        var result = await session.RunTurnAsync("Summarize.", [], factory.Create(profile), CancellationToken.None);
        return (result, session);
    }

    private static string[] TrickleEvents(string[] deltas)
    {
        var events = new List<string>();
        var builder = new StringBuilder();
        AppendEvent(
            builder,
            "response.created",
            "{\"type\":\"response.created\",\"response\":{\"id\":\"resp-1\",\"status\":\"in_progress\"}}");
        events.Add(builder.ToString());
        foreach (var delta in deltas)
        {
            builder.Clear();
            AppendEvent(
                builder,
                "response.output_text.delta",
                "{\"type\":\"response.output_text.delta\",\"item_id\":\"msg-1\",\"delta\":"
                + JsonSerializer.Serialize(delta)
                + "}");
            events.Add(builder.ToString());
        }

        builder.Clear();
        AppendEvent(
            builder,
            "response.completed",
            "{\"type\":\"response.completed\",\"response\":{\"id\":\"resp-1\",\"status\":\"completed\"}}");
        builder.Append("data: [DONE]\n\n");
        events.Add(builder.ToString());
        return [.. events];
    }

    private sealed class TrickleHandler(string[] events, TimeSpan gap, bool stallAfterFirst) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new StreamContent(new TrickleStream(events, gap, stallAfterFirst ? 2 : int.MaxValue));
            content.Headers.ContentType = new("text/event-stream") { CharSet = "utf-8" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            });
        }
    }

    /// <summary>Hands out one server-sent event per read, pausing before each.</summary>
    private sealed class TrickleStream(string[] events, TimeSpan gap, int stallAt) : Stream
    {
        private int _next;
        private ReadOnlyMemory<byte> _pending;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pending.IsEmpty)
            {
                if (_next >= events.Length)
                {
                    return 0;
                }

                await Task.Delay(_next >= stallAt ? Timeout.InfiniteTimeSpan : gap, cancellationToken);
                _pending = Encoding.UTF8.GetBytes(events[_next++]);
            }

            var count = Math.Min(buffer.Length, _pending.Length);
            _pending[..count].CopyTo(buffer);
            _pending = _pending[count..];
            return count;
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
