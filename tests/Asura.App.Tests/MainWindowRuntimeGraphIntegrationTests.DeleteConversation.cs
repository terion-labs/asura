using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;

namespace Asura.App.Tests;

public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    private static readonly AgentRunId SavedConversationRunId = new("saved-run");

    /// <summary>
    /// File › Delete Conversation… deletes the conversation the agent panel
    /// shows, so it is dimmed while the panel is hidden, while the agent is
    /// busy, and while the panel shows no saved conversation.
    /// </summary>
    [Fact]
    public async Task Delete_conversation_waits_for_a_visible_idle_panel_that_shows_a_saved_conversation()
    {
        var provider = CreateAgentProvider();
        using var agentRuntime = new RecordingGovernedAgentRuntime();
        using var aiProfiles = new FixedAiProfileRuntime([provider]);
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(
            client,
            CreateCatalogSnapshot(),
            agentRuntime: agentRuntime,
            aiProfiles: aiProfiles);
        Assert.True(await viewModel.OpenWorkspaceAsync(WorkspaceId));
        var chat = Assert.IsType<AgentChatViewModel>(viewModel.AgentChat);
        viewModel.IsAgentPanelVisible = true;

        bool CanDelete() => viewModel.ActiveCommandState.Single(fact => string.Equals(
            fact.Key,
            CommandStates.CanExecute(BuiltInCommands.DeleteAgentConversation),
            StringComparison.Ordinal)).Value;

        // Nothing saved yet: there is nothing to delete.
        Assert.False(CanDelete());
        Assert.Null(viewModel.CurrentAgentConversation);

        agentRuntime.SetSnapshot(WithSavedConversation(agentRuntime.Snapshot, provider.Id));
        await WaitForAsync(() => chat.ConversationDeletedByClear is not null);
        Assert.True(CanDelete());
        Assert.Equal(SavedConversationRunId, viewModel.CurrentAgentConversation?.RunId);

        // A hidden panel's conversation is not in front.
        viewModel.IsAgentPanelVisible = false;
        Assert.False(CanDelete());
        Assert.Null(viewModel.CurrentAgentConversation);

        // A busy agent keeps its conversation until it is done.
        viewModel.IsAgentPanelVisible = true;
        agentRuntime.SetSnapshot(agentRuntime.Snapshot with
        {
            State = GovernedAgentState.StreamingProvider,
        });
        await WaitForAsync(() => chat.IsBusy);
        Assert.False(CanDelete());
        Assert.Null(viewModel.CurrentAgentConversation);
    }

    private static GovernedAgentSnapshot WithSavedConversation(
        GovernedAgentSnapshot snapshot,
        AiProviderProfileId providerId) =>
        snapshot with
        {
            RunId = SavedConversationRunId,
            ProviderId = providerId,
            Conversations =
            [
                new GovernedAgentConversationSummary(
                    SavedConversationRunId,
                    "Plan the release",
                    providerId,
                    "model",
                    4,
                    new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero)),
            ],
        };

    private sealed class IdleApplicationCommandService : IApplicationCommandService
    {
        public bool IsQuickTerminalVisible => true;

        public Task ExecuteAsync(CommandId command, TopLevel origin) => Task.CompletedTask;

        public MenuShortcutResolver CreateShortcutResolver() => new([], []);
    }
}
