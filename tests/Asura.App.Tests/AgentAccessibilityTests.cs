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

    [Theory]
    [InlineData("Kubernetes mutation", "Kubernetes")]
    [InlineData("Kubernetes observation", "Kubernetes")]
    [InlineData("Docker observation", "Docker")]
    [InlineData("Database read", "Database")]
    [InlineData("Git repository change", "Git repository")]
    [InlineData("Workspace 01a0df93caed736baaa598b6c213744d", "this workspace")]
    [InlineData("Panel 01a0df93caed736baaa598b6c213744d", "this panel")]
    [InlineData("Tab 01a0df93caed736baaa598b6c213744d", "this tab")]
    [InlineData("Connection session 01a0df93caed736baaa598b6c213744d", "this connection")]
    [InlineData("Selected panels in workspace 01a0df93caed736baaa598b6c213744d", "selected panels")]
    [InlineData("Tab 3", "Tab 3")]
    [InlineData("Workspace 2", "Workspace 2")]
    public void Card_target_is_a_place_not_a_category_or_identifier(string targetTitle, string human) =>
        Assert.Equal(human, AgentTargetPresentation.HumanTitle(targetTitle));

    [Theory]
    [InlineData("maximum_matches", "Maximum matches")]
    [InlineData("timeout_ms", "Timeout (ms)")]
    [InlineData("dry_run", "Dry run")]
    [InlineData("kind", "Type")]
    [InlineData("operation_detail", "Description")]
    [InlineData("delta_x", "Delta X")]
    public void Action_arguments_have_readable_names(string name, string displayName) =>
        Assert.Equal(displayName, new AgentApprovalArgumentViewModel(name, "value", false).DisplayName);

    [Theory]
    [InlineData("resource_ref")]
    [InlineData("container_ref")]
    [InlineData("connection_ref")]
    [InlineData("repository_identity")]
    [InlineData("operation")]
    [InlineData("uid")]
    [InlineData("resource_version")]
    [InlineData("document_revision")]
    [InlineData("reference")]
    [InlineData("attachment_id")]
    public void Opaque_references_stay_in_details(string name) =>
        Assert.True(new AgentApprovalArgumentViewModel(name, "ref:01a0df93", false).IsRoutingIdentifier);

    [Fact]
    public void Workspace_layout_approval_names_the_workspace_by_its_title()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var runId = new AgentRunId("run-layout");
        var workspace = new AgentTarget.Workspace(
            new WindowInstanceId("window-1"),
            new WorkspaceInstanceId("workspace-1"));
        var context = new[]
        {
            ContextItem("panel-1", "session-1", "API shell", "Deploy", true, true, false, []),
        };
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(
                state: GovernedAgentState.StreamingProvider,
                runId: runId,
                providerId: provider.Id,
                target: workspace,
                contextItems: context),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);

        // The composer's own title and arguments, as the runtime sends them.
        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.AwaitingApproval,
            PendingApproval = new GovernedAgentApproval(
                new AgentApprovalId("approval-layout"),
                "workspace.panel_split",
                "Split panel",
                AgentActionRisk.Mutation,
                AgentPermission.Ask,
                workspace,
                new AgentApprovalPresentation(
                    "Workspace workspace-1",
                    host: null,
                    workingDirectory: null,
                    [
                        new AgentApprovalArgument("panel_id", "panel-1"),
                        new AgentApprovalArgument("orientation", "Right"),
                        new AgentApprovalArgument("kind", "Terminal"),
                        new AgentApprovalArgument("connection_ref", "connection:7f3a"),
                    ]),
                DateTimeOffset.UtcNow.AddMinutes(5),
                TemporarilyYieldsTerminalInput: false),
        };
        runtime.RaiseChanged();

        var approval = Assert.IsType<AgentApprovalCardViewModel>(viewModel.PendingApproval);
        Assert.Equal("Production workspace", approval.TargetDisplayTitle);
        Assert.Equal("Approval required: Split panel in Production workspace", viewModel.Announcement);
        Assert.DoesNotContain("workspace-1", approval.AccessibleName, StringComparison.Ordinal);
        Assert.Equal(
            ["Split", "Type"],
            approval.ActionArguments.Select(argument => argument.DisplayName),
            StringComparer.Ordinal);
        // The exact identifiers are still there to check.
        Assert.Equal("Workspace workspace-1", approval.TargetTitle);
        Assert.Contains(approval.Arguments, argument => argument.Name == "connection_ref");

        // The run card that follows names the same place.
        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.RunningTool,
            PendingApproval = null,
            ActiveTool = new GovernedAgentToolActivity(
                "workspace.panel_split",
                "Split panel",
                AgentActionRisk.Mutation,
                "Workspace workspace-1"),
        };
        runtime.RaiseChanged();
        Assert.Equal("Production workspace", viewModel.ActiveTool?.TargetDisplayTitle);
    }

    [Fact]
    public void Kubernetes_control_approval_reads_without_jargon_or_tool_ids()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var runId = new AgentRunId("run-kubernetes");
        var context = new[]
        {
            ContextItem(
                "panel-1",
                "session-1",
                "Production cluster",
                "Ops",
                true,
                true,
                false,
                [],
                PanelKind.Kubernetes),
        };
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(
                state: GovernedAgentState.StreamingProvider,
                runId: runId,
                providerId: provider.Id,
                target: Target(),
                contextItems: context),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);

        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.AwaitingApproval,
            PendingApproval = new GovernedAgentApproval(
                new AgentApprovalId("approval-kubernetes"),
                "kubernetes.scale",
                "Scale workload",
                AgentActionRisk.Mutation,
                AgentPermission.Ask,
                Target(),
                new AgentApprovalPresentation(
                    "Kubernetes mutation",
                    "Production cluster",
                    workingDirectory: null,
                    [
                        new AgentApprovalArgument("panel_id", "panel-1"),
                        new AgentApprovalArgument("operation", "kubernetes.scale"),
                        new AgentApprovalArgument("operation_detail", "Scale api to 3 replicas"),
                        new AgentApprovalArgument("resource", "deployments"),
                        new AgentApprovalArgument("namespace", "default"),
                        new AgentApprovalArgument("name", "api"),
                        new AgentApprovalArgument("uid", "5d2c9a1e-0b7f-4c1a-9a51-0e0c1f8f4b11"),
                        new AgentApprovalArgument("resource_version", "81723"),
                        new AgentApprovalArgument("dry_run", "False"),
                    ]),
                DateTimeOffset.UtcNow.AddMinutes(5),
                TemporarilyYieldsTerminalInput: false),
        };
        runtime.RaiseChanged();

        var approval = Assert.IsType<AgentApprovalCardViewModel>(viewModel.PendingApproval);
        Assert.Equal("Production cluster", approval.TargetDisplayTitle);
        Assert.Equal("Approval required: Scale workload in Production cluster", viewModel.Announcement);
        Assert.DoesNotContain("mutation", approval.AccessibleName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["Description", "Resource", "Namespace", "Name", "Dry run"],
            approval.ActionArguments.Select(argument => argument.DisplayName),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            approval.ActionArguments,
            argument => argument.DisplayValue.Contains("kubernetes.", StringComparison.Ordinal));

        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.RunningTool,
            PendingApproval = null,
            ActiveTool = new GovernedAgentToolActivity(
                "kubernetes.scale",
                "Scale workload",
                AgentActionRisk.Mutation,
                "Kubernetes mutation",
                PanelId: new PanelInstanceId("panel-1")),
        };
        runtime.RaiseChanged();
        Assert.Equal("Production cluster", viewModel.ActiveTool?.TargetDisplayTitle);

        // Without a panel title to use, the run card falls back to a plain noun.
        Assert.Equal(
            "Kubernetes",
            new AgentToolActivityViewModel(
                "kubernetes.scale",
                "Scale workload",
                AgentActionRisk.Mutation,
                "Kubernetes mutation",
                CancellationRequested: false,
                PanelId: null).TargetDisplayTitle);
    }

    [Fact]
    public void A_repeated_approval_in_the_same_run_is_announced_again()
    {
        var provider = Provider("provider", "Provider", order: 0);
        var runId = new AgentRunId("run-repeat");
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(
                state: GovernedAgentState.StreamingProvider,
                runId: runId,
                providerId: provider.Id,
                target: Target()),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);
        var announced = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentChatViewModel.Announcement))
            {
                announced.Add(viewModel.Announcement);
            }
        };
        static GovernedAgentApproval Approval(string id) => new(
            new AgentApprovalId(id),
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
        const string Expected = "Approval required: Send text to terminal in Production API shell";

        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.AwaitingApproval,
            PendingApproval = Approval("approval-1"),
        };
        runtime.RaiseChanged();
        Assert.Equal([Expected], announced);

        // Approved: the command runs and the model streams again.
        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.RunningTool,
            PendingApproval = null,
        };
        runtime.RaiseChanged();
        runtime.Snapshot = runtime.Snapshot with { State = GovernedAgentState.StreamingProvider };
        runtime.RaiseChanged();

        // The second, word-for-word identical request is a change again.
        runtime.Snapshot = runtime.Snapshot with
        {
            State = GovernedAgentState.AwaitingApproval,
            PendingApproval = Approval("approval-2"),
        };
        runtime.RaiseChanged();
        Assert.Equal([Expected, string.Empty, Expected], announced);

        // Even when one request replaces another directly.
        runtime.Snapshot = runtime.Snapshot with { PendingApproval = Approval("approval-3") };
        runtime.RaiseChanged();
        Assert.Equal([Expected, string.Empty, Expected, string.Empty, Expected], announced);
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
        Assert.Equal(AgentChatViewModel.RunStartedAnnouncement, viewModel.Announcement);

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

        // A new run is announced, so the same outcome is a change again.
        runtime.Snapshot = runtime.Snapshot with { State = GovernedAgentState.StreamingProvider };
        runtime.RaiseChanged();
        Assert.Equal(AgentChatViewModel.RunStartedAnnouncement, viewModel.Announcement);

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
        Assert.False(viewModel.ClearDeletesConversation);

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
        Assert.True(viewModel.ClearDeletesConversation);
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
    public Task Approval_leaves_focus_in_a_composer_that_holds_unsent_text() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    state: GovernedAgentState.StreamingProvider,
                    runId: new AgentRunId("run-typing"),
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
            var window = new Window { Width = 800, Height = 900, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var prompt = view.FindControl<TextBox>("AgentChatPromptInput")!;
                Assert.True(prompt.Focus());
                prompt.Text = "also check the";

                runtime.Snapshot = runtime.Snapshot with
                {
                    State = GovernedAgentState.AwaitingApproval,
                    PendingApproval = new GovernedAgentApproval(
                        new AgentApprovalId("approval-typing"),
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
                        TemporarilyYieldsTerminalInput: false),
                };
                runtime.RaiseChanged();
                window.UpdateLayout();
                await Task.Delay(50);
                window.UpdateLayout();

                Assert.True(prompt.IsFocused);
                Assert.StartsWith("Approval required", viewModel.Announcement, StringComparison.Ordinal);
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

                var resting = composer.BorderBrush;
                Assert.NotNull(resting);

                window.DragDrop(center, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(center, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.Contains("dropTarget", composer.Classes, StringComparer.Ordinal);
                // The highlight border shows, not only the focus ring: the
                // resting border is a style the drop target style replaces.
                Assert.NotNull(composer.BorderBrush);
                Assert.NotSame(resting, composer.BorderBrush);

                // A drag cancelled over the composer (Esc) leaves at a point
                // inside it; the highlight still ends.
                window.DragDrop(center, RawDragEventType.DragLeave, data, DragDropEffects.Copy, RawInputModifiers.None);
                Assert.DoesNotContain("dropTarget", composer.Classes, StringComparer.Ordinal);
                Assert.Same(resting, composer.BorderBrush);

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
