using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;

namespace Asura.App.Tests;

/// <summary>
/// What the agent surface tells assistive technology and keyboard users, and
/// the plain language its decision cards use.
/// </summary>
public sealed partial class AgentChatViewModelTests
{
    [Theory]
    [InlineData(AgentActionRisk.Observation, "Read only")]
    [InlineData(AgentActionRisk.Routine, "Routine")]
    [InlineData(AgentActionRisk.Mutation, "Makes changes")]
    [InlineData(AgentActionRisk.Destructive, "Can delete data")]
    [InlineData(AgentActionRisk.Privileged, "Needs elevated access")]
    public void Action_risk_reads_as_its_consequence(AgentActionRisk risk, string name) =>
        Assert.Equal(name, AgentPolicyPresentation.RiskName(risk));

    [Theory]
    [InlineData("Production API shell", "Production API shell")]
    [InlineData("Runbook — panel 01a0df93caed736baaa598b6c213744d — session browser-1", "Runbook")]
    [InlineData("Terminal — session ssh-7 — panel panel-1", "Terminal")]
    [InlineData("Browser · panel 01a0df93caed736baaa598b6c213744d", "Browser")]
    [InlineData("MCP server: Local tools", "MCP server: Local tools")]
    public void Card_target_drops_routing_identifiers(string targetTitle, string readable) =>
        Assert.Equal(readable, AgentTargetPresentation.ReadableTitle(targetTitle));

    [Fact]
    public void Panel_and_tab_identifiers_stay_out_of_the_action_arguments()
    {
        var approval = new AgentApprovalCardViewModel(
            new AgentApprovalId("approval-ids"),
            "panel.focus",
            "Focus panel",
            AgentActionRisk.Destructive,
            "Ask",
            "Logs — panel 01a0df93 — session s-1",
            "window/w · workspace/ws · tab/t · panel/01a0df93",
            "Workspace panel",
            "Not reported",
            [
                new AgentApprovalArgumentViewModel("panel_id", "01a0df93", false),
                new AgentApprovalArgumentViewModel("tab_id", "tab-1", false),
                new AgentApprovalArgumentViewModel("text", "ls", false),
            ],
            "in 5 minutes",
            TemporarilyYieldsTerminalInput: false);

        Assert.Equal("ls", Assert.Single(approval.ActionArguments).DisplayValue);
        Assert.Equal(3, approval.Arguments.Count);
        Assert.Equal("Logs", approval.TargetDisplayTitle);
        Assert.True(approval.IsHighRisk);
        Assert.Equal("Can delete data", approval.Risk);
        Assert.DoesNotContain("01a0df93", approval.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_announces_decisions_and_outcomes_but_not_the_stream()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var runId = new AgentRunId("run-announce");
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, target: Target()),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);
        Assert.Equal(string.Empty, viewModel.Announcement);

        runtime.Snapshot = Snapshot(
            state: GovernedAgentState.StreamingProvider,
            runId: runId,
            providerId: provider.Id,
            target: Target(),
            provisional: "Looking at the logs");
        runtime.RaiseChanged();
        Assert.Equal(string.Empty, viewModel.Announcement);

        var approval = new GovernedAgentApproval(
            new AgentApprovalId("approval-announce"),
            "terminal.send_text",
            "Send text to terminal",
            AgentActionRisk.Mutation,
            AgentPermission.Ask,
            Target(),
            new AgentApprovalPresentation(
                "Production API shell — panel panel-1 — session s-1",
                "api.example.test",
                "/srv/api",
                [new AgentApprovalArgument("text", "uptime")]),
            DateTimeOffset.UtcNow.AddMinutes(5),
            TemporarilyYieldsTerminalInput: false);
        runtime.Snapshot = Snapshot(
            state: GovernedAgentState.AwaitingApproval,
            runId: runId,
            providerId: provider.Id,
            target: Target(),
            pendingApproval: approval);
        runtime.RaiseChanged();
        Assert.Equal(
            "Approval required: Send text to terminal in Production API shell",
            viewModel.Announcement);

        runtime.Snapshot = Snapshot(
            state: GovernedAgentState.Ready,
            runId: runId,
            providerId: provider.Id,
            target: Target(),
            messages: [new AgentChatMessage(AgentChatMessageRole.Assistant, "Uptime is 4 days.")]);
        runtime.RaiseChanged();
        Assert.Equal("Response complete", viewModel.Announcement);

        // A new run clears the region, so the same outcome is heard again.
        runtime.Snapshot = runtime.Snapshot with { State = GovernedAgentState.StreamingProvider };
        runtime.RaiseChanged();
        Assert.Equal(string.Empty, viewModel.Announcement);

        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.Failed,
            Status = "The provider rate-limited this request.",
        };
        runtime.RaiseChanged();
        Assert.StartsWith(viewModel.FailureHeading, viewModel.Announcement, StringComparison.Ordinal);
        Assert.EndsWith("The provider rate-limited this request.", viewModel.Announcement, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_raw_secrets_help_says_what_happens_to_the_next_message()
    {
        var provider = Provider("provider", "Provider", order: 0);
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, target: Target()),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Secrets in this message stay hidden from the model.", viewModel.SendRawSecretsHelp);
        viewModel.SendRawSecrets = true;

        Assert.Contains(nameof(AgentChatViewModel.SendRawSecretsHelp), changed, StringComparer.Ordinal);
        Assert.StartsWith("Secrets in this message go to Provider / ", viewModel.SendRawSecretsHelp, StringComparison.Ordinal);
        Assert.EndsWith("Turns off after sending.", viewModel.SendRawSecretsHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_names_the_saved_conversation_it_would_delete()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var currentRun = new AgentRunId("run-current");
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(
                state: GovernedAgentState.Failed,
                runId: currentRun,
                providerId: provider.Id,
                status: "The response failed."),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);

        // A first turn that failed before anything was saved discards nothing
        // anyone could reopen.
        Assert.True(viewModel.HasFailedTurn);
        Assert.Null(viewModel.ConversationDeletedByClear);
        Assert.Equal("Clear Failed Run", viewModel.ClearFailedRunLabel);

        runtime.Snapshot = runtime.Snapshot with
        {
            Conversations =
            [
                new GovernedAgentConversationSummary(
                    currentRun,
                    "Plan the release",
                    provider.Id,
                    "model",
                    4,
                    new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero)),
            ],
        };
        runtime.RaiseChanged();

        Assert.Equal("Plan the release", viewModel.ConversationDeletedByClear?.Title);
        Assert.Equal("Delete Conversation…", viewModel.ClearFailedRunLabel);
        Assert.Contains("Plan the release", viewModel.ClearFailedRunAccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_window_percentage_is_written_out()
    {
        var provider = Provider("provider", "Provider", order: 0);
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id, target: Target()),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);

        Assert.Equal(
            $"{viewModel.ContextWindowPercent.ToString("0", System.Globalization.CultureInfo.CurrentCulture)}%",
            viewModel.ContextWindowPercentLabel);
    }

    [Fact]
    public Task Context_gauge_reports_its_level_to_assistive_technology() =>
        RunAgentComposerHeadlessAsync(() =>
        {
            var gauge = new ContextWindowDonut { Width = 44, Height = 44, Percentage = 62.5 };
            var window = new Window { Width = 100, Height = 100, Content = gauge };
            try
            {
                window.Show();
                var peer = ControlAutomationPeer.CreatePeerForElement(gauge);
                var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);

                Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
                Assert.True(range.IsReadOnly);
                Assert.Equal(0, range.Minimum);
                Assert.Equal(100, range.Maximum);
                Assert.Equal(62.5, range.Value);
                gauge.Percentage = 140;
                Assert.Equal(100, range.Value);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Approval_takes_focus_only_when_focus_is_not_elsewhere(bool focusElsewhere) =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            var runId = new AgentRunId("run-focus");
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    state: GovernedAgentState.StreamingProvider,
                    runId: runId,
                    providerId: provider.Id,
                    target: Target(),
                    messages: [new AgentChatMessage(AgentChatMessageRole.User, "Restart the API")]),
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var viewModel = new AgentChatViewModel(
                runtime,
                profiles,
                ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(viewModel) };
            var elsewhere = new TextBox { Width = 200 };
            var window = new Window
            {
                Width = 800,
                Height = 900,
                Content = new DockPanel { Children = { elsewhere, view } },
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                if (focusElsewhere)
                {
                    Assert.True(elsewhere.Focus());
                }

                runtime.Snapshot = runtime.Snapshot with
                {
                    State = GovernedAgentState.AwaitingApproval,
                    PendingApproval = new GovernedAgentApproval(
                        new AgentApprovalId("approval-focus"),
                        "terminal.send_text",
                        "Send text to terminal",
                        AgentActionRisk.Mutation,
                        AgentPermission.Ask,
                        Target(),
                        new AgentApprovalPresentation(
                            "Production API shell — panel panel-1 — session s-1",
                            "api.example.test",
                            "/srv/api",
                            [new AgentApprovalArgument("text", "sudo systemctl restart api")]),
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        TemporarilyYieldsTerminalInput: false),
                };
                runtime.RaiseChanged();
                var card = view.FindControl<AgentApprovalCardView>("AgentApprovalCard")!
                    .FindControl<SurfaceCard>("AgentApprovalRequest")!;
                for (var attempt = 0; attempt < 50 && !card.IsFocused && !focusElsewhere; attempt++)
                {
                    window.UpdateLayout();
                    await Task.Delay(10);
                }

                // Give a focus move that should not happen the same chance.
                window.UpdateLayout();
                await Task.Delay(50);
                Assert.Equal(!focusElsewhere, card.IsFocused);
                Assert.Equal(focusElsewhere, elsewhere.IsFocused);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Send_raw_secrets_checkbox_meets_the_minimum_hit_target() =>
        RunAgentComposerHeadlessAsync(() =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(providerId: provider.Id, target: Target()),
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Width = 420, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var option = view.FindControl<CheckBox>("AgentSendRawSecrets")!;
                var help = view.FindControl<TextBlock>("AgentSendRawSecretsHelp")!;

                Assert.True(option.IsVisible);
                Assert.True(option.Bounds.Height >= 20, $"Hit target is {option.Bounds.Height} pt tall.");
                Assert.True(option.Bounds.Width >= 20, $"Hit target is {option.Bounds.Width} pt wide.");
                Assert.True(help.IsVisible);
                Assert.Equal(model.SendRawSecretsHelp, help.Text);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    [Fact]
    public Task Files_dropped_on_the_composer_attach_to_the_next_message() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", 0, supportsImageInput: false);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(providerId: provider.Id),
                SupportsFileAttachments = true,
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Width = 420, Height = 700, Content = view };
            var directory = Directory.CreateTempSubdirectory("asura-composer-drop-");
            try
            {
                window.Show();
                window.UpdateLayout();
                var path = Path.Combine(directory.FullName, "notes.txt");
                byte[] bytes = [1, 2, 3, 4];
                await File.WriteAllBytesAsync(path, bytes);
                using var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
                Assert.NotNull(file);
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateFile(file));
                var composer = view.FindControl<Border>("AgentComposer")!;
                var center = composer.TranslatePoint(
                    new Point(composer.Bounds.Width / 2, composer.Bounds.Height / 2),
                    window)!.Value;

                window.DragDrop(center, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(center, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.Contains("dropTarget", composer.Classes, StringComparer.Ordinal);

                window.DragDrop(center, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.DoesNotContain("dropTarget", composer.Classes, StringComparer.Ordinal);
                await WaitUntilAsync(() => model.HasPendingFiles);

                var attached = Assert.Single(model.PendingFiles);
                Assert.Equal("notes.txt", attached.FileName);
                Assert.Equal(bytes, runtime.ImportedFiles[attached.Id]);
            }
            finally
            {
                window.Close();
                directory.Delete(recursive: true);
            }
        });
}
