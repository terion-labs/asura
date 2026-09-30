using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task SourceLocksRenderForUserAndReasoningOnlyAssistantAndExplainTheirStateOnHover() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            var reference = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(providerId: provider.Id, messages:
                [
                    new(AgentChatMessageRole.User, reference.Placeholder, HiddenReferences: [reference], ChatMessageId: Guid.NewGuid().ToString("N")),
                    new(AgentChatMessageRole.Assistant, string.Empty, ReasoningSummary: reference.Placeholder,
                        HiddenReferences: [reference], ChatMessageId: Guid.NewGuid().ToString("N")),
                    new(AgentChatMessageRole.User, "No secret", DisclosedHiddenCount: 1, DisclosureDestination: "provider/model"),
                ]),
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Content = view, Width = 700, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var locks = view.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible
                    && AutomationProperties.GetName(button)?.StartsWith("This message's secrets", StringComparison.Ordinal) == true).ToArray();
                Assert.Equal(2, locks.Length);
                locks[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(model.Messages[0].ExposeSecretsOnNextRequest);
                Assert.False(model.Messages[1].ExposeSecretsOnNextRequest);
                Assert.Contains("next request", Assert.IsType<string>(ToolTip.GetTip(locks[0])), StringComparison.Ordinal);
                ToolTip.SetShowDelay(locks[0], 0);
                window.UpdateLayout();
                var position = locks[0].TranslatePoint(new Point(locks[0].Bounds.Width / 2, locks[0].Bounds.Height / 2), window);
                Assert.NotNull(position);
                window.MouseMove(position.Value);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                Assert.True(ToolTip.GetIsOpen(locks[0]));
                locks[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(model.Messages[0].ExposeSecretsOnNextRequest);
            }
            finally
            {
                window.Close();
            }
        }, typeof(AgentAttachmentHeadlessApplication));

    [Fact]
    public async Task MessageLocksSelectOnlyTheirSourcesAndResetAfterSend()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var first = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var second = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var draft = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var firstId = Guid.NewGuid().ToString("N");
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, messages:
            [
                new(AgentChatMessageRole.User, first.Placeholder, HiddenReferences: [first], ChatMessageId: firstId),
                new(AgentChatMessageRole.Assistant, second.Placeholder, HiddenReferences: [second], ChatMessageId: Guid.NewGuid().ToString("N")),
                new(AgentChatMessageRole.User, "No secret", DisclosedHiddenCount: 1, DisclosureDestination: "provider/model"),
            ]),
            DraftProtector = text => new(draft.Placeholder, [draft]),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
        viewModel.ToggleMessageSecrets(viewModel.Messages[0]);
        runtime.RaiseChanged();
        Assert.True(viewModel.Messages[0].ExposeSecretsOnNextRequest);
        Assert.False(viewModel.Messages[1].ExposeSecretsOnNextRequest);
        Assert.False(viewModel.Messages[2].HasHiddenSecrets);
        Assert.False(viewModel.Messages[2].HasDisclosureReceipt);
        viewModel.Prompt = "Continue";
        await viewModel.SendAsync(Target(), Policy(provider), default);
        Assert.Equal(new[] { first }, runtime.LastRequest!.DiscloseHiddenReferences);
        Assert.Equal(firstId, Assert.Single(runtime.LastRequest.DiscloseHiddenMessageIds), StringComparer.Ordinal);
        Assert.False(runtime.LastRequest.DiscloseDraftSecrets);
        Assert.All(viewModel.Messages, message => Assert.False(message.ExposeSecretsOnNextRequest));
        viewModel.Prompt = "Continue masked";
        await viewModel.SendAsync(Target(), Policy(provider), default);
        Assert.Empty(runtime.LastRequest!.DiscloseHiddenReferences);
        Assert.Empty(runtime.LastRequest.DiscloseHiddenMessageIds);
    }

    [Fact]
    public async Task RawSecretsToggleIncludesOnlyDraftOnce()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var first = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var second = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        var draft = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, messages: [new(AgentChatMessageRole.User,
                first.Placeholder + " " + second.Placeholder, HiddenReferences: [first, second], ChatMessageId: Guid.NewGuid().ToString("N"))]),
            DraftProtector = text => new(draft.Placeholder, [draft]),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
        viewModel.Prompt = "Use the secrets";
        Assert.False(viewModel.SendRawSecrets);
        Assert.False(viewModel.Messages[0].ExposeSecretsOnNextRequest);
        viewModel.SendRawSecrets = true;
        await viewModel.SendAsync(Target(), Policy(provider), default);
        Assert.Equal(new[] { draft }, runtime.LastRequest!.DiscloseHiddenReferences);
        Assert.Empty(runtime.LastRequest.DiscloseHiddenMessageIds);
        Assert.True(runtime.LastRequest.DiscloseDraftSecrets);
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
                reference.Placeholder, HiddenReferences: [reference], ChatMessageId: Guid.NewGuid().ToString("N"))]),
        };
        using var profiles = new StubProfileRuntime { Profiles = [firstProvider, secondProvider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
        viewModel.Prompt = "Continue";
        viewModel.SendRawSecrets = true;
        viewModel.ToggleMessageSecrets(viewModel.Messages[0]);
        viewModel.SelectedProvider = secondProvider;
        Assert.False(viewModel.SendRawSecrets);
        Assert.False(viewModel.Messages[0].ExposeSecretsOnNextRequest);
        viewModel.ToggleMessageSecrets(viewModel.Messages[0]);
        Assert.Contains("Second", viewModel.Messages[0].SecretDisclosureExplanation, StringComparison.Ordinal);
        viewModel.ToggleMessageSecrets(viewModel.Messages[0]);
        viewModel.SendRawSecrets = true;
        viewModel.ToggleMessageSecrets(viewModel.Messages[0]);
        runtime.Snapshot = runtime.Snapshot with { SelectedConversationRunId = new("fork-run") };
        runtime.RaiseChanged();
        Assert.False(viewModel.SendRawSecrets);
        Assert.False(viewModel.Messages[0].ExposeSecretsOnNextRequest);
    }
}
