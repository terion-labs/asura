using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime.Tests;

public sealed partial class GovernedAgentRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_opens_only_files_attached_to_its_conversation(bool foreign)
    {
        var file = new AgentFileAttachment(Guid.NewGuid().ToString("N"), "notes.txt", 12);
        var id = foreign ? Guid.NewGuid().ToString("N") : file.Id;
        var provider = new ProviderRound((call, _) => call == 1
            ? ProviderRound.ToolCall("open-file", "attachments.open", "{\"id\":\"" + id + "\",\"offset\":0}")
            : ProviderRound.Answer("Finished."));
        var attachments = new TestAttachmentService();
        await using var fixture = new RuntimeFixture(provider, workspaceId: new WorkspaceInstanceId("workspace-1"), attachments: attachments);
        var prompt = new GovernedAgentPrompt(new AiProviderProfileId("provider-1"), "Read this file", fixture.Target,
            [], AgentReasoningEffort.Automatic, AgentServiceTier.Automatic,
            fixture.Runtime.Snapshot.EffectivePolicy!.SelectPrimaryModel("provider-1", fixture.ProviderResolver.Binding.DefaultModel),
            AgentApprovalMode.Ask, [file]);
        var result = await fixture.Runtime.SendAsync(prompt, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Code);
        Assert.Equal(foreign ? 0 : 1, attachments.OpenCount);
        var toolResult = provider.Requests.Last().Messages.Single(message => message.Role == AgentMessageRole.Tool).Content;
        Assert.Contains(foreign ? "attachment_not_in_conversation" : "file content", toolResult, StringComparison.Ordinal);
        Assert.Contains(provider.Requests.First().Tools, tool => tool.Name == "attachments.open");
        Assert.Equal("notes.txt", Assert.Single(fixture.Runtime.Snapshot.Messages.Single(message => message.Role == AgentChatMessageRole.User).Files!));
    }

    private sealed class TestAttachmentService : IAgentAttachmentService
    {
        public int OpenCount { get; private set; }
        public ValueTask<AgentFileAttachment> ImportAsync(AgentConversationScopeId scope, string fileName,
            ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentFileAttachment(Guid.NewGuid().ToString("N"), fileName, content.Length));

        public ValueTask<AgentOpenedAttachment> OpenAsync(AgentConversationScopeId scope, WorkspaceInstanceId workspace,
            AgentFileAttachment attachment, int offset, CancellationToken cancellationToken)
        {
            OpenCount++;
            Assert.Equal("test-files", scope.Value);
            Assert.Equal("workspace-1", workspace.Value);
            return ValueTask.FromResult(new AgentOpenedAttachment("/tmp/test-file.txt", "workspace local terminal", "file content", 12, false));
        }
    }
}
