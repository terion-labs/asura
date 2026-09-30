using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public async Task RawSecretsToggleIncludesConversationAndDraftOnce()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var first = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var second = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var draft = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, messages: [new(AgentChatMessageRole.User,
                first.Placeholder + " " + second.Placeholder, HiddenReferences: [first, second])]),
            DraftProtector = text => new(draft.Placeholder, [draft]),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
        viewModel.Prompt = "Use the secrets";
        Assert.False(viewModel.SendRawSecrets);
        viewModel.SendRawSecrets = true;
        await viewModel.SendAsync(Target(), Policy(provider), default);
        Assert.Equal(new[] { first, second, draft }, runtime.LastRequest!.DiscloseHiddenReferences);
        Assert.False(viewModel.SendRawSecrets);
        Assert.Contains("Provider", viewModel.SendRawSecretsExplanation, StringComparison.Ordinal);
        viewModel.Prompt = "Continue normally";
        await viewModel.SendAsync(Target(), Policy(provider), default);
        Assert.Empty(runtime.LastRequest!.DiscloseHiddenReferences);
    }

    [Fact]
    public void ProviderAndConversationChangesTurnOffRawSecrets()
    {
        var firstProvider = Provider("first", "First", order: 0);
        var secondProvider = Provider("second", "Second", order: 1);
        var reference = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: firstProvider.Id, messages: [new(AgentChatMessageRole.User,
                reference.Placeholder, HiddenReferences: [reference])]),
        };
        using var profiles = new StubProfileRuntime { Profiles = [firstProvider, secondProvider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
        viewModel.Prompt = "Continue";
        viewModel.SendRawSecrets = true;
        viewModel.SelectedProvider = secondProvider;
        Assert.False(viewModel.SendRawSecrets);
        viewModel.SendRawSecrets = true;
        runtime.Snapshot = runtime.Snapshot with { SelectedConversationRunId = new("fork-run") };
        runtime.RaiseChanged();
        Assert.False(viewModel.SendRawSecrets);
    }
}
