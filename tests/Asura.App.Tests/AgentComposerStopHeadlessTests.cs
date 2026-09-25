using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task Composer_stop_keeps_draft_and_allows_continuing_with_another_model() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var otherModel = new AiProviderModelDescriptor("model-fast", "Fast model");
            var provider = Provider("provider", "Provider", order: 0,
                models: [new AiProviderModelDescriptor("model", "Default model"), otherModel]);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    state: GovernedAgentState.StreamingProvider,
                    runId: new AgentRunId("run-stop-model"),
                    providerId: provider.Id,
                    target: Target(),
                    messages: [new AgentChatMessage(AgentChatMessageRole.User, "Inspect it.")],
                    steeringAvailable: true),
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var viewModel = new AgentChatViewModel(
                runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(viewModel) };
            var window = new Window { Width = 420, Height = 900, Content = view };
            Task stopRequest = Task.CompletedTask;
            view.CancelAgentChatRequested += (_, _) =>
                stopRequest = viewModel.StopAsync(CancellationToken.None);

            try
            {
                window.Show();
                window.UpdateLayout();
                var stop = Assert.IsType<Button>(view.FindControl<Button>("AgentStopButton"));
                var send = Assert.IsType<Button>(view.FindControl<Button>("AgentSendButton"));
                var picker = Assert.IsType<Button>(view.FindControl<Button>("AgentModelPickerButton"));
                var composer = Assert.IsType<Border>(view.FindControl<Border>("AgentComposer"));
                Assert.Contains(composer, stop.GetVisualAncestors());
                Assert.True(stop.IsEffectivelyVisible);
                Assert.True(stop.IsEffectivelyEnabled);
                Assert.False(send.IsEffectivelyVisible);
                Assert.False(picker.IsEffectivelyEnabled);

                viewModel.Prompt = "Continue with the faster model.";
                window.UpdateLayout();
                Assert.True(send.IsEffectivelyVisible);
                Assert.True(stop.IsEffectivelyVisible);
                Assert.True(send.Bounds.Right <= stop.Bounds.Left);

                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await stopRequest;
                Assert.Equal(1, runtime.StopCount);
                runtime.Snapshot = runtime.Snapshot with { State = GovernedAgentState.Cancelling };
                runtime.RaiseChanged();
                await WaitUntilAsync(() => viewModel.State == GovernedAgentState.Cancelling);
                window.UpdateLayout();
                Assert.True(stop.IsEffectivelyVisible);
                Assert.False(stop.IsEffectivelyEnabled);
                Assert.False(picker.IsEffectivelyEnabled);

                runtime.Snapshot = runtime.Snapshot with { State = GovernedAgentState.Cancelled };
                runtime.RaiseChanged();
                await WaitUntilAsync(() => viewModel.State == GovernedAgentState.Cancelled);
                window.UpdateLayout();
                Assert.False(stop.IsEffectivelyVisible);
                Assert.True(send.IsEffectivelyEnabled);
                Assert.True(picker.IsEffectivelyEnabled);
                Assert.Equal("Continue with the faster model.", viewModel.Prompt);

                await viewModel.SelectModelAsync(otherModel, CancellationToken.None);
                Assert.Equal("Inspect it.", Assert.Single(viewModel.Messages).Content);
                await viewModel.SendAsync(Target(), Policy(provider), CancellationToken.None);
                var request = Assert.IsType<GovernedAgentPrompt>(runtime.LastRequest);
                Assert.Equal("model-fast", Assert.IsType<AgentPolicy>(request.Policy).Model);
                Assert.Equal("Continue with the faster model.", request.Message);
                Assert.Equal(0, runtime.ClearCount);
            }
            finally
            {
                window.Close();
            }
        });
}
