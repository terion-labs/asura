using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Agent.Providers.Tests;

public sealed partial class OpenAiResponsesProviderTests
{
    [Fact]
    public async Task Next_message_keeps_accepted_screenshots_when_encoded_history_exceeds_four_MiB()
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        using var handler = new CapturingHandler(ResponsesTextStream("continued"));
        using var factory = new AiProviderFactory(vault, handler);
        var bytes = new byte[3 * 1024 * 1024];
        TinyPng().Content.CopyTo(bytes);
        var image = new AgentImageAttachment("screenshot.png", "image/png", bytes);
        var session = new NativeAgentSession(new AgentRunId("large-image-history"),
        [
            new AgentMessage(AgentMessageRole.User, "Inspect both screenshots.", [image, image]),
            new AgentMessage(AgentMessageRole.Assistant, "I have inspected them."),
        ]);

        var result = await session.RunTurnAsync("Continue.", [], factory.Create(profile), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("continued", session.Snapshot().Conversation[^1].Content);
        var request = Assert.IsType<CapturedRequest>(handler.LastRequest);
        Assert.True(Encoding.UTF8.GetByteCount(request.Body) > 4 * 1024 * 1024);
        using var body = JsonDocument.Parse(request.Body);
        var input = body.RootElement.GetProperty("input");
        var content = input[0].GetProperty("content");
        Assert.Equal(3, content.GetArrayLength());
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(bytes);
        Assert.Equal(dataUrl, content[1].GetProperty("image_url").GetString());
        Assert.Equal(dataUrl, content[2].GetProperty("image_url").GetString());
        Assert.Equal("Continue.", input[2].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Oversized_request_reports_size_failure_without_sending_or_blaming_configuration()
    {
        using var vault = new InMemorySecretVault();
        var profile = await CreateProfileAsync(vault);
        using var handler = new CapturingHandler(ResponsesTextStream("unexpected"));
        using var factory = new AiProviderFactory(vault, handler, new AiProviderRuntimeLimits(maximumRequestBytes: 1024));
        var session = new NativeAgentSession(new AgentRunId("request-size-limit"));

        var result = await session.RunTurnAsync(new string('x', 2048), [], factory.Create(profile), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ai_provider_request_too_large", result.ProviderFailure?.StableCode);
        Assert.Contains("too large", result.ProviderFailure!.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Request_limit_accepts_exact_boundary_and_rejects_the_next_byte()
    {
        using var stream = new BoundedMemoryStream(1024);
        stream.Write(new byte[1024]);

        var error = Assert.Throws<AiProviderClientException>(() => stream.WriteByte(0));

        Assert.Equal(AiProviderRuntimeErrorCode.RequestTooLarge, error.Code);
        Assert.Equal(1024, stream.Length);
    }
}
