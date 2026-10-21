using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

/// <summary>
/// A message sent after Stop resumes the conversation. Stopped while the
/// model was answering a tool result, the conversation ended in that result,
/// the resumed session refused it, and every message sent after Stop was lost.
/// </summary>
public sealed partial class GovernedAgentRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_message_after_stopping_while_the_model_reads_a_tool_result_resumes(bool fullAccess)
    {
        var provider = new ProviderRound((call, _) => call == 1
            ? ProviderRound.ToolCall("call-1", BuiltInAgentTools.TerminalReadScreen, "{}")
            : ProviderRound.Answer("Completed."))
        {
            BlockOnCall = 2,
        };
        await using var fixture = new RuntimeFixture(provider, checkpointStore: new InMemoryCheckpointStore());
        var sending = fixture.Runtime.SendAsync(
            StopResumePrompt(fixture, "Update the instance.", fullAccess),
            CancellationToken.None).AsTask();
        await provider.BlockedCall.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True((await fixture.Runtime.StopAsync(CancellationToken.None)).WasRunning);
        Assert.Equal("agent_cancelled", (await sending.WaitAsync(TimeSpan.FromSeconds(5))).Code);
        var resumed = await fixture.Runtime.SendAsync(
            StopResumePrompt(fixture, "Try again.", fullAccess),
            CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(resumed.IsSuccess, $"{resumed.Code}: {resumed.Message}");
        Assert.Contains(
            fixture.Runtime.Snapshot.Messages,
            message => message.Role == AgentChatMessageRole.User
                && string.Equals(message.Content, "Try again.", StringComparison.Ordinal));
        var request = provider.Requests.Last();
        Assert.Equal(AgentMessageRole.User, request.Messages[^1].Role);
        Assert.Equal(AgentMessageRole.Assistant, request.Messages[^2].Role);
        Assert.Contains(request.Messages, message => message.Role == AgentMessageRole.Tool);
    }

    [Fact]
    public async Task A_message_after_stopping_while_the_model_thinks_resumes()
    {
        var thinking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ProviderRound((call, _) => call == 1
            ? [new AgentProviderEvent.ResponseStarted(), new AgentProviderEvent.ReasoningSummaryDelta("Thinking")]
            : ProviderRound.Answer("Completed."))
        {
            AfterEvent = async (providerEvent, token) =>
            {
                if (providerEvent is AgentProviderEvent.ReasoningSummaryDelta && thinking.TrySetResult())
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            },
        };
        await using var fixture = new RuntimeFixture(provider, checkpointStore: new InMemoryCheckpointStore());
        var sending = fixture.Runtime.SendAsync(
            StopResumePrompt(fixture, "Update the instance.", fullAccess: false),
            CancellationToken.None).AsTask();
        await thinking.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True((await fixture.Runtime.StopAsync(CancellationToken.None)).WasRunning);
        _ = await sending.WaitAsync(TimeSpan.FromSeconds(5));
        var resumed = await fixture.Runtime.SendAsync(
            StopResumePrompt(fixture, "Try again.", fullAccess: false),
            CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(resumed.IsSuccess, $"{resumed.Code}: {resumed.Message}");
    }

    private static GovernedAgentPrompt StopResumePrompt(RuntimeFixture fixture, string message, bool fullAccess)
    {
        var prompt = fixture.Prompt(message);
        return new GovernedAgentPrompt(
            prompt.ProviderId,
            message,
            prompt.Target,
            [],
            AgentReasoningEffort.Automatic,
            AgentServiceTier.Automatic,
            prompt.Policy,
            fullAccess ? AgentApprovalMode.FullAccess : AgentApprovalMode.Ask);
    }
}
