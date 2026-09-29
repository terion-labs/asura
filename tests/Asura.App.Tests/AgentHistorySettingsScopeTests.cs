using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quick_terminal_history_settings_replace_only_the_reset_owner(bool hasQuickSettingsOwner)
    {
        var provider = Provider("provider", "Provider", 0);
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var mainRuntime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
        using var oldRuntime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
        using var replacementRuntime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
        using var oldQuick = new AgentChatViewModel(oldRuntime, profiles, ImmediateUiThreadDispatcher.Instance);
        using var replacement = new AgentChatViewModel(replacementRuntime, profiles, ImmediateUiThreadDispatcher.Instance);
        using var main = MainWindowSettingsSaveFacadeTests.CreateAgentHistorySettings(mainRuntime, profiles);
        var mainChat = main.AgentChat;
        main.ShowSettings(SettingsPage.Agent, hasQuickSettingsOwner ? oldQuick : null);

        oldQuick.Dispose();
        main.ReplaceAgentHistoryOwner(oldQuick, replacement);

        Assert.Same(hasQuickSettingsOwner ? replacement : mainChat, main.AgentHistoryOwner);
        Assert.Same(mainChat, main.AgentChat);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quick_terminal_history_settings_preserve_the_main_chat_and_use_the_requested_owner(bool hasMainChat)
    {
        var provider = Provider("provider", "Provider", 0);
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var mainRuntime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
        using var quickRuntime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
        using var quickChat = new AgentChatViewModel(quickRuntime, profiles, ImmediateUiThreadDispatcher.Instance)
        {
            Prompt = "Keep the Quick Terminal draft",
        };
        using var main = MainWindowSettingsSaveFacadeTests.CreateAgentHistorySettings(
            hasMainChat ? mainRuntime : null, profiles);
        var originalMainChat = main.AgentChat;
        Assert.Equal(hasMainChat, originalMainChat is not null);

        main.ShowSettings(SettingsPage.Agent, quickChat);

        Assert.Same(quickChat, main.AgentHistoryOwner);
        Assert.Same(originalMainChat, main.AgentChat);
        Assert.Equal("Keep the Quick Terminal draft", quickChat.Prompt);

        main.ShowSettings(SettingsPage.Agent);

        Assert.Same(originalMainChat, main.AgentHistoryOwner);
        Assert.Same(originalMainChat, main.AgentChat);
    }
}
