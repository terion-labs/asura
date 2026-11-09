using System.Text.Json;
using Asura.Agent;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Fact]
    public async Task SentPromptIsInTheTranscriptWhileTheProviderWorks()
    {
        var provider = ProviderRound.AnswerEveryTurn(blockOnCall: 2);
        await using var fixture = new RuntimeFixture(provider);
        Assert.True((await fixture.Runtime.SendAsync(fixture.Prompt("First"),
            CancellationToken.None)).IsSuccess);

        var sending = fixture.Runtime.SendAsync(fixture.Prompt("Second"),
            CancellationToken.None).AsTask();
        await provider.BlockedCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var during = fixture.Runtime.Snapshot.Messages[^1];
        provider.ReleaseBlockedCall.TrySetResult();
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);

        Assert.Equal(AgentChatMessageRole.User, during.Role);
        Assert.Equal("Second", during.Content);
    }

    [Fact]
    public async Task AnsweredQuestionIsInTheTranscriptWhileTheProviderContinues()
    {
        var provider = new ProviderRound((call, _) => call switch
        {
            1 => ToolCall(
                "ask-1",
                IntrinsicAgentTools.AskUser,
                JsonSerializer.Serialize(new { question = "Which colour?" })),
            _ => Answer("Done."),
        })
        {
            BlockOnCall = 2,
        };
        await using var fixture = new RuntimeFixture(provider);
        var sending = fixture.Runtime.SendAsync(fixture.Prompt("Paint it"),
            CancellationToken.None).AsTask();
        await WaitUntilAsync(() =>
            fixture.Runtime.Snapshot.State == GovernedAgentState.AwaitingUserInput);
        var question = Assert.IsType<GovernedAgentQuestion>(
            fixture.Runtime.Snapshot.PendingQuestion);

        var response = await fixture.Runtime.RespondToQuestionAsync(
            question.Id,
            new GovernedAgentQuestionResponse.Submitted("Blue"),
            CancellationToken.None);
        var accepted = fixture.Runtime.Snapshot.Messages
            .Select(message => (message.Content, message.Kind))
            .ToArray();
        await provider.BlockedCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duringProvider = fixture.Runtime.Snapshot.Messages
            .Select(message => (message.Content, message.Kind))
            .ToArray();
        provider.ReleaseBlockedCall.TrySetResult();
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);

        Assert.True(response.IsAccepted);
        (string, AgentChatMessageKind)[] exchange =
        [
            ("Paint it", AgentChatMessageKind.Message),
            ("Which colour?", AgentChatMessageKind.Question),
            ("Blue", AgentChatMessageKind.Answer),
        ];
        Assert.Equal(exchange, accepted);
        Assert.Equal(exchange, duringProvider);
    }

    [Fact]
    public async Task QueuedFollowUpDoesNotWaitForTheConversationTitle()
    {
        var provider = ProviderRound.AnswerEveryTurn(blockOnCall: 2);
        var policy = AgentPolicy.Default with
        {
            TitleModel = new AgentModelSelection("provider-1", "title-model"),
        };
        await using var fixture = new RuntimeFixture(
            provider,
            configuredPolicy: policy);

        var sending = fixture.Runtime.SendAsync(
            fixture.Prompt("First"),
            CancellationToken.None).AsTask();
        await provider.BlockedCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Naming the conversation…", fixture.Runtime.Snapshot.Status);

        var queued = await fixture.Runtime.QueueFollowUpAsync(
            new GovernedAgentFollowUp(
                "Right away",
                AgentReasoningEffort.Automatic,
                GovernedAgentFollowUpDelivery.FollowUp),
            CancellationToken.None);

        Assert.True(queued.IsAccepted);
        Assert.True((await sending.WaitAsync(TimeSpan.FromSeconds(5))).IsSuccess);
        Assert.Equal(
            ["First", "Completed.", "Right away", "Completed."],
            fixture.Runtime.Snapshot.Messages.Select(message => message.Content),
            StringComparer.Ordinal);
    }
}
