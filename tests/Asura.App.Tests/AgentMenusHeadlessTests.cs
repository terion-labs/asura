using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
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
    public Task Agent_menus_name_each_model_row_and_say_which_is_current() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            using var menu = await OpenModelMenuAsync();

            // A drawn check alone is silent to VoiceOver: each row names its
            // model and provider, and the row in use says so in its name and
            // its item status, wherever the selection has moved.
            menu.Models.SelectedIndex = 1;
            menu.Window.UpdateLayout();
            var rows = menu.Models.GetRealizedContainers().OfType<ListBoxItem>().ToArray();
            Assert.Equal(
                ["Default model, Provider, current model", "Fast model, Provider"],
                rows.Select(row => ControlAutomationPeer.CreatePeerForElement(row).GetName()),
                StringComparer.Ordinal);
            Assert.Equal(
                ["Current model", string.Empty],
                rows.Select(row => AutomationProperties.GetItemStatus(row) ?? string.Empty),
                StringComparer.Ordinal);
        });

    [Fact]
    public Task Agent_menus_choose_a_clicked_model_but_not_a_clicked_star() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            using var menu = await OpenModelMenuAsync();
            var fast = ModelRow(menu.Models, "model-fast");

            // A click on the star marks a favorite and leaves the menu open.
            var star = Assert.Single(
                fast.GetVisualDescendants().OfType<Button>(),
                button => button.Tag is AgentModelPickerItemViewModel);
            ReleaseLeftButton(Assert.IsType<SymbolIcon>(star.GetVisualDescendants().OfType<SymbolIcon>().First()));
            Assert.Equal(0, menu.Chosen);
            Assert.True(menu.Picker.Flyout!.IsOpen);

            // A click anywhere else on the row, here its name, chooses it.
            ReleaseLeftButton(Assert.Single(
                fast.GetVisualDescendants().OfType<TextBlock>(),
                block => string.Equals(block.Text, "Fast model", StringComparison.Ordinal)));
            await menu.Selection;

            Assert.Equal(1, menu.Chosen);
            Assert.False(menu.Picker.Flyout.IsOpen);
            Assert.Equal("model-fast", menu.ViewModel.SelectedModel?.Id);
        });

    [Fact]
    public Task Agent_menus_move_from_the_model_filter_into_the_list_on_Down() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            using var menu = await OpenModelMenuAsync();
            var filter = Assert.IsType<SearchField>(menu.View.FindControl<SearchField>("AgentModelFilter"));

            // The menu opened on the model in use; Down puts the keyboard on
            // that row rather than leaving it in the field.
            filter.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Down,
            });
            menu.Window.UpdateLayout();
            var current = ModelRow(menu.Models, "model");
            Assert.True(current.IsFocused);
            Assert.Equal(0, menu.Chosen);

            // With nothing selected, Down starts at the first row.
            menu.Models.SelectedItem = null;
            filter.Focus();
            filter.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Down,
            });
            menu.Window.UpdateLayout();
            Assert.Equal(0, menu.Models.SelectedIndex);
            Assert.True(ModelRow(menu.Models, "model").IsFocused);
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
                ("ShellTextScaleReferenceSize", 13d),
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

    private static async Task<OpenModelMenu> OpenModelMenuAsync()
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
        var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(
                providerId: provider.Id,
                target: Target(),
                effectivePolicy: Policy(provider)),
        };
        var profiles = new StubProfileRuntime { Profiles = [provider] };
        var viewModel = new AgentChatViewModel(
            runtime,
            profiles,
            ImmediateUiThreadDispatcher.Instance);
        var view = new AgentWorkspaceView
        {
            DataContext = new AgentComposerHost(viewModel),
        };
        var window = new Window { Width = 700, Height = 900, Content = view };
        var menu = new OpenModelMenu(runtime, profiles, viewModel, view, window);
        view.SelectAgentModelRequested += (sender, _) =>
        {
            menu.Chosen++;
            menu.Selection = viewModel.SelectModelAsync(
                Assert.IsType<AiProviderModelDescriptor>(Assert.IsAssignableFrom<Control>(sender).Tag),
                CancellationToken.None);
        };
        window.Show();
        window.UpdateLayout();
        await Task.Yield();
        menu.Picker.Flyout!.ShowAt(menu.Picker);
        window.UpdateLayout();
        return menu;
    }

    private static ListBoxItem ModelRow(ListBox models, string id) =>
        Assert.IsType<ListBoxItem>(models.ContainerFromItem(Assert.Single(
            models.Items.OfType<AgentModelPickerItemViewModel>(),
            row => string.Equals(row.Id, id, StringComparison.Ordinal))));

    private static void ReleaseLeftButton(Control target)
    {
        var root = Assert.IsAssignableFrom<Avalonia.Visual>(TopLevel.GetTopLevel(target));
        target.RaiseEvent(new PointerReleasedEventArgs(
            target,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
            root,
            default,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            Avalonia.Input.KeyModifiers.None,
            MouseButton.Left));
    }

    private sealed class OpenModelMenu(
        StubGovernedRuntime runtime,
        StubProfileRuntime profiles,
        AgentChatViewModel viewModel,
        AgentWorkspaceView view,
        Window window) : IDisposable
    {
        public AgentChatViewModel ViewModel { get; } = viewModel;

        public AgentWorkspaceView View { get; } = view;

        public Window Window { get; } = window;

        public Button Picker => Assert.IsType<Button>(View.FindControl<Button>("AgentModelPickerButton"));

        public ListBox Models => Assert.IsType<ListBox>(View.FindControl<ListBox>("AgentModelList"));

        public int Chosen { get; set; }

        public Task Selection { get; set; } = Task.CompletedTask;

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            profiles.Dispose();
            runtime.Dispose();
        }
    }

    private static string[] CheckedRows(ListBox models) =>
    [
        .. models.GetVisualDescendants()
            .OfType<SymbolIcon>()
            .Where(mark => string.Equals(mark.Name, "CurrentModelMark", StringComparison.Ordinal)
                && mark.IsEffectivelyVisible)
            .Select(mark => ((AgentModelPickerItemViewModel)mark.DataContext!).DisplayName),
    ];
}
