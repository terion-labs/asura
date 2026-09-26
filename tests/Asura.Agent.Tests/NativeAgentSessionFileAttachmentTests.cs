using Asura.Agent;
using Asura.Core;

namespace Asura.Agent.Tests;

public sealed partial class NativeAgentSessionTests
{
    [Fact]
    public async Task File_only_turn_has_provider_references_and_survives_checkpoint_without_file_bytes()
    {
        var file = new AgentFileAttachment(Guid.NewGuid().ToString("N"), "large custom.file", AgentFileAttachment.MaximumBytes);
        var session = CreateSession();
        var provider = TextProvider("I can open the attachment.");
        var result = await session.RunTurnAsync("", [], [], AgentReasoningEffort.Automatic, provider, CancellationToken.None, [file]);
        Assert.True(result.Succeeded);
        var userInput = provider.LastRequest!.Messages.Single(message => message.Role == AgentMessageRole.User).Content;
        Assert.Contains(file.Id, userInput, StringComparison.Ordinal);
        Assert.Contains("attachments.open", userInput, StringComparison.Ordinal);
        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        Assert.True(checkpoint.PayloadJson.Length < 8192);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        Assert.Equal(file, Assert.Single(restored.Snapshot().Transcript.Single(message => message.Role == AgentMessageRole.User).Files));
        var interrupted = CreateSession().CaptureInterruptedCheckpoint("", [], [file]);
        var interruptedSession = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(interrupted.Checkpoint!).Session);
        Assert.Equal(file, Assert.Single(interruptedSession.Snapshot().Transcript.Single(message => message.Role == AgentMessageRole.User).Files));
    }
}
