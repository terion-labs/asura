using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia;
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

    [Fact]
    public Task A_running_action_card_keeps_clear_of_the_status_line_below_it() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            // Enough history to fill the panel, so the transcript is scrolled
            // to its end and the card is the last thing above the status.
            var provider = Provider("provider", "Provider", order: 0);
            var history = Enumerable.Range(0, 30)
                .Select(index => new AgentChatMessage(
                    index % 2 == 0 ? AgentChatMessageRole.User : AgentChatMessageRole.Assistant,
                    $"Message {index}. Text that wraps across the narrow panel a couple of times."))
                .ToArray();
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    state: GovernedAgentState.RunningTool,
                    runId: new AgentRunId("run-tool"),
                    providerId: provider.Id,
                    target: Target(),
                    messages: history,
                    status: "Running Wait for terminal state…",
                    activeTool: new GovernedAgentToolActivity(
                        "terminal.wait",
                        "Wait for terminal state",
                        AgentActionRisk.Routine,
                        "Terminal")),
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
                var card = await WaitForVisualAsync<Border>(
                    view,
                    window,
                    border => AutomationProperties.GetName(border) == "AI agent tool activity"
                        && border.IsEffectivelyVisible,
                    "the running action's card");
                var status = await WaitForVisualAsync<TextBlock>(
                    view,
                    window,
                    text => AutomationProperties.GetName(text) == "AI agent status"
                        && text.IsEffectivelyVisible,
                    "the status line");

                var transcript = view.FindControl<ScrollViewer>("AgentChatTranscript")!;
                Assert.True(transcript.Extent.Height > transcript.Viewport.Height, "the transcript should overflow");
                transcript.ScrollToEnd();
                window.UpdateLayout();
                var cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), view)!.Value.Y;
                var statusTop = status.TranslatePoint(default, view)!.Value.Y;
                var gap = (double)view.FindResource("ShellSpaceSm")!;
                Assert.True(
                    statusTop - cardBottom >= gap,
                    $"card ends at {cardBottom}, status starts at {statusTop}");
            }
            finally
            {
                window.Close();
            }
        });
}
