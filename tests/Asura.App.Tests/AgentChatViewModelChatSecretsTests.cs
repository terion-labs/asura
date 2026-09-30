using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public async Task OnlyCheckedOriginalsGoIntoNextRequestAndSelectionClearsAfterSend()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var first = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var second = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, messages: [new(AgentChatMessageRole.User,
                first.Placeholder + " " + second.Placeholder, HiddenReferences: [first, second])]),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
        viewModel.Prompt = "Use one original";
        Assert.Equal(2, viewModel.HiddenContentChoices.Count);
        Assert.All(viewModel.HiddenContentChoices, choice => Assert.False(choice.Include));
        viewModel.HiddenContentChoices[0].Include = true;
        await viewModel.SendAsync(Target(), Policy(provider), default);
        Assert.Equal(first, Assert.Single(runtime.LastRequest!.DiscloseHiddenReferences));
        Assert.All(viewModel.HiddenContentChoices, choice => Assert.False(choice.Include));
        Assert.Contains("Provider", viewModel.HiddenDisclosureDestination, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAndConversationChangesResetDisclosureSelection()
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
        viewModel.HiddenContentChoices[0].Include = true;
        viewModel.SelectedProvider = secondProvider;
        Assert.False(viewModel.HiddenContentChoices[0].Include);
        viewModel.HiddenContentChoices[0].Include = true;
        runtime.Snapshot = runtime.Snapshot with { SelectedConversationRunId = new("fork-run") };
        runtime.RaiseChanged();
        Assert.False(viewModel.HiddenContentChoices[0].Include);
    }
}
