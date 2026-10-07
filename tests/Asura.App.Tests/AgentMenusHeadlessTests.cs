using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The agent composer's choices are menus: the approval mode is a pop-up
/// menu with the current mode checked, the model menu checks the model in
/// use and chooses on Return, and a queued message's actions sit in its
/// options menu. Each used to be a popover of buttons, which arrow keys did
/// not walk and VoiceOver read as buttons.
/// </summary>
public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task Agent_menus_check_the_approval_mode_in_use() =>
        RunAgentComposerHeadlessAsync(async () =>
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
            var view = new AgentWorkspaceView
            {
                DataContext = new AgentComposerHost(viewModel),
            };
            var window = new Window { Width = 700, Height = 900, Content = view };

            try
            {
                window.Show();
                window.UpdateLayout();
                var access = Assert.IsType<Button>(view.FindControl<Button>("AgentAccessModeButton"));
                var menu = Assert.IsType<MenuFlyout>(access.Flyout);
                var modes = menu.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(
                    ["Ask for Approval", "Full Access"],
                    modes.Select(item => item.Header as string),
                    StringComparer.Ordinal);
                Assert.All(modes, item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));

                menu.ShowAt(access);
                window.UpdateLayout();
                Assert.True(modes[0].IsChecked);
                Assert.False(modes[1].IsChecked);
                Assert.Equal(modes[0].Header, viewModel.AccessModeLabel);
                menu.Hide();

                await viewModel.SelectFullAccessAsync(CancellationToken.None);
                menu.ShowAt(access);
                window.UpdateLayout();

                // The button always reads as the item that is checked.
                Assert.False(modes[0].IsChecked);
                Assert.True(modes[1].IsChecked);
                Assert.Equal(modes[1].Header, viewModel.AccessModeLabel);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Agent_menus_check_the_model_in_use_and_choose_on_Return() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider(
                "provider",
                "Provider",
                order: 0,
                models:
                [
                    new AiProviderModelDescriptor("model", "Default model"),
                    new AiProviderModelDescriptor("model-fast", "Fast model"),
                ]);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    providerId: provider.Id,
                    target: Target(),
                    effectivePolicy: Policy(provider)),
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
            var window = new Window { Width = 700, Height = 900, Content = view };

            try
            {
                Task selection = Task.CompletedTask;
                view.SelectAgentModelRequested += (sender, _) => selection = viewModel.SelectModelAsync(
                    Assert.IsType<AiProviderModelDescriptor>(Assert.IsAssignableFrom<Control>(sender).Tag),
                    CancellationToken.None);
                window.Show();
                window.UpdateLayout();
                var picker = Assert.IsType<Button>(view.FindControl<Button>("AgentModelPickerButton"));
                picker.Flyout!.ShowAt(picker);
                window.UpdateLayout();

                var filter = Assert.IsType<SearchField>(view.FindControl<SearchField>("AgentModelFilter"));
                Assert.Equal("Filter models", filter.PlaceholderText);
                var models = Assert.IsType<ListBox>(view.FindControl<ListBox>("AgentModelList"));
                Assert.Equal(SelectionMode.Single, models.SelectionMode);

                // The model in use is checked, and the menu opens on it.
                var current = Assert.Single(
                    models.Items.OfType<AgentModelPickerItemViewModel>(),
                    item => item.IsCurrent);
                Assert.Equal("model", current.Id);
                Assert.Same(current, models.SelectedItem);
                Assert.Equal(
                    ["Default model"],
                    CheckedRows(models),
                    StringComparer.Ordinal);

                // Typing narrows the list; Return in the field chooses the
                // first match and closes the menu.
                viewModel.ModelSearch = "fast";
                window.UpdateLayout();
                Assert.Single(models.Items);
                filter.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                });
                await selection;

                Assert.False(picker.Flyout.IsOpen);
                Assert.Equal("model-fast", viewModel.SelectedModel?.Id);
                viewModel.ModelSearch = string.Empty;
                picker.Flyout.ShowAt(picker);
                window.UpdateLayout();
                Assert.Equal(
                    ["Fast model"],
                    CheckedRows(models),
                    StringComparer.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Agent_menus_put_Edit_Message_in_the_queued_message_options() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    state: GovernedAgentState.StreamingProvider,
                    runId: new AgentRunId("run-queued-menu"),
                    providerId: provider.Id,
                    target: Target()) with
                {
                    QueuedFollowUpCount = 1,
                    QueuedFollowUps =
                    [
                        new GovernedAgentQueuedFollowUp(
                            new AgentQueuedFollowUpId("queued-follow-up"),
                            "Then summarize.",
                            AgentReasoningEffort.Automatic,
                            GovernedAgentFollowUpDelivery.FollowUp),
                    ],
                },
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
            var window = new Window { Width = 700, Height = 900, Content = view };

            try
            {
                window.Show();
                window.UpdateLayout();
                var options = Assert.Single(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => string.Equals(
                        AutomationProperties.GetName(button),
                        "Queued agent message options",
                        StringComparison.Ordinal));

                // The Mac's "more" glyph: an ellipsis in a circle, not the
                // vertical dots of another platform.
                Assert.Equal(Symbol.MoreCircle, Assert.IsType<SymbolIcon>(options.Content).Symbol);
                var menu = Assert.IsType<MenuFlyout>(options.Flyout);
                menu.ShowAt(options);
                window.UpdateLayout();
                var edit = Assert.Single(menu.Items.OfType<MenuItem>());
                Assert.Equal("Edit Message", edit.Header);

                var queued = Assert.Single(viewModel.QueuedFollowUps);
                Assert.Same(queued.BeginEditCommand, edit.Command);
                edit.Command!.Execute(edit.CommandParameter);
                await Task.Yield();
                Assert.True(queued.IsEditing);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Agent_composer_controls_grow_with_text_and_do_not_clip_at_double_size() =>
        RunAgentComposerHeadlessAsync(() =>
        {
            var provider = Provider(
                "provider",
                "Provider",
                order: 0,
                supportedReasoningEfforts:
                [
                    AgentReasoningEffort.Automatic,
                    AgentReasoningEffort.High,
                ]);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(providerId: provider.Id, target: Target()),
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
            var window = new Window { Width = 1000, Height = 1400, Content = view };

            // 200% text: every text token the composer reads, doubled.
            foreach (var (key, size) in new[]
            {
                ("ShellBaseFontSize", 13d),
                ("ControlContentThemeFontSize", 13d),
                ("ShellFontSize10", 10d),
                ("ShellFontSize11", 11d),
                ("ShellFontSize13", 13d),
                ("ShellSecondaryFontSize", 11d),
                ("ShellIconSizeXs", 10d),
                ("ShellIconSizeSm", 13d),
                ("ShellIconSizeMd", 16d),
            })
            {
                window.Resources[key] = size * 2;
            }

            try
            {
                window.Show();
                window.UpdateLayout();
                var toolbar = Assert.IsType<Grid>(view.FindControl<Grid>("AgentComposerToolbar"));
                var buttons = toolbar.Children
                    .OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .ToArray();
                Assert.NotEmpty(buttons);
                Assert.All(buttons, button => Assert.True(
                    button.Bounds.Height >= 64,
                    $"{AutomationProperties.GetName(button)} is {button.Bounds.Height} high at 200% text."));

                var composer = Assert.IsAssignableFrom<Control>(toolbar.Parent);
                var clipped = composer.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(control => control.IsEffectivelyVisible
                        && control is TextBlock or SymbolIcon or CheckBox or ComboBox
                        && control.Bounds.Height + 0.5
                            < control.DesiredSize.Height - control.Margin.Top - control.Margin.Bottom)
                    .Select(control => $"{control.GetType().Name} {AutomationProperties.GetName(control)}"
                        + $" {control.Bounds.Height} < {control.DesiredSize.Height}")
                    .ToArray();
                Assert.Empty(clipped);
                Assert.Contains(
                    composer.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.IsEffectivelyVisible && block.FontSize >= 22);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    private static string[] CheckedRows(ListBox models) =>
    [
        .. models.GetVisualDescendants()
            .OfType<SymbolIcon>()
            .Where(mark => string.Equals(mark.Name, "CurrentModelMark", StringComparison.Ordinal)
                && mark.IsEffectivelyVisible)
            .Select(mark => ((AgentModelPickerItemViewModel)mark.DataContext!).DisplayName),
    ];
}
