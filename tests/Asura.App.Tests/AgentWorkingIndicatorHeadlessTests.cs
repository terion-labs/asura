using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task Sent_prompt_and_working_status_render_before_the_provider_replies() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            var history = Enumerable.Range(0, 30)
                .Select(index => new AgentChatMessage(
                    index % 2 == 0 ? AgentChatMessageRole.User : AgentChatMessageRole.Assistant,
                    $"Message {index}. Text that wraps across the narrow panel a couple of times."))
                .ToArray();
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    runId: new AgentRunId("run-working"),
                    providerId: provider.Id,
                    target: Target(),
                    messages: history),
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var viewModel = new AgentChatViewModel(
                runtime,
                profiles,
                ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView
            {
                DataContext = new AgentComposerHost(viewModel),
            };
            var window = new Window { Width = 400, Height = 600, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                runtime.Snapshot = Snapshot(
                    state: GovernedAgentState.StreamingProvider,
                    runId: new AgentRunId("run-working"),
                    providerId: provider.Id,
                    target: Target(),
                    messages: [.. history, new AgentChatMessage(AgentChatMessageRole.User, "Just sent")],
                    status: "Thinking…");
                runtime.RaiseChanged();

                _ = await WaitForVisualAsync<SelectableMarkdownDocument>(
                    view,
                    window,
                    block => block.Text.Contains("Just sent", StringComparison.Ordinal)
                        && block.IsEffectivelyVisible,
                    "the sent prompt");
                var working = await WaitForVisualAsync<TextBlock>(
                    view,
                    window,
                    text => AutomationProperties.GetName(text) == "AI agent working status"
                        && text.IsEffectivelyVisible,
                    "the working status in the transcript");
                Assert.Equal("Thinking…", working.Text);
                Assert.False(viewModel.ShowFooterStatus);

                runtime.Snapshot = runtime.Snapshot with
                {
                    ProvisionalReasoningSummary = "**Reading the request**",
                };
                runtime.RaiseChanged();
                _ = await WaitForVisualAsync<SelectableMarkdownDocument>(
                    view,
                    window,
                    block => block.Text.Contains("Reading the request", StringComparison.Ordinal)
                        && block.IsEffectivelyVisible,
                    "the reasoning as it streams");
                Assert.False(working.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });
}
