using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Saved conversations are deleted the macOS list way, from the row's context
/// menu or with Delete on the focused row, and not with a red trash on every
/// row. A damaged or unavailable conversation can't be opened, but its row
/// stays enabled so it can still be deleted.
/// </summary>
public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task Conversation_rows_delete_from_their_menu_and_the_Delete_key_even_when_damaged() =>
        RunAgentComposerHeadlessAsync(() =>
        {
            var provider = Provider("provider", "Provider", order: 0);
            var updated = new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);
            var available = new AgentRunId("run-available");
            var damaged = new AgentRunId("run-damaged");
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(
                    providerId: provider.Id,
                    target: Target(),
                    conversations:
                    [
                        new GovernedAgentConversationSummary(
                            available, "Plan the release", provider.Id, "model", 4, updated),
                        new GovernedAgentConversationSummary(
                            damaged,
                            "Broken notes",
                            provider.Id,
                            "model",
                            2,
                            updated,
                            GovernedAgentConversationAvailability.Corrupt),
                    ]),
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
            var opened = new List<object?>();
            var deleted = new List<object?>();
            view.OpenAgentConversationRequested += (sender, _) => opened.Add((sender as Control)?.Tag);
            view.DeleteAgentConversationRequested += (sender, _) => deleted.Add((sender as Control)?.Tag);

            try
            {
                window.Show();
                window.UpdateLayout();
                var history = Assert.IsType<Button>(view.FindControl<Button>("AgentConversationHistoryButton"));
                var flyout = Assert.IsType<Flyout>(history.Flyout);
                flyout.ShowAt(history);
                window.UpdateLayout();
                var rows = Assert.IsAssignableFrom<Control>(flyout.Content)
                    .GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("ConversationRow"))
                    .ToArray();
                Assert.Equal(2, rows.Length);
                var availableRow = Assert.Single(rows, row => Equals(row.Tag, available));
                var damagedRow = Assert.Single(rows, row => Equals(row.Tag, damaged));

                // No row carries a destructive control of its own.
                Assert.DoesNotContain(
                    Assert.IsAssignableFrom<Control>(flyout.Content)
                        .GetVisualDescendants()
                        .OfType<Button>(),
                    button => button.Classes.Contains("Danger") || button.Classes.Contains("DestructiveButton"));

                Assert.True(damagedRow.IsEnabled);
                Assert.True(damagedRow.Focusable);
                Assert.Contains("unavailable", damagedRow.Classes, StringComparer.Ordinal);
                Assert.DoesNotContain("unavailable", availableRow.Classes, StringComparer.Ordinal);

                foreach (var row in rows)
                {
                    var item = Assert.Single(
                        Assert.IsType<ContextMenu>(row.ContextMenu).Items.OfType<MenuItem>());
                    Assert.Equal("Delete Conversation…", item.Header);
                }

                // The File menu's item shares the row item's title; it
                // deletes the conversation the panel shows.
                Assert.Contains(
                    ApplicationMenuLayout.MenuBar.Single(menu => menu.Title == "File").Items.OfType<MenuCommandNode>(),
                    node => node.Command == BuiltInCommands.DeleteAgentConversation
                        && node.Title == AgentWorkspaceView.DeleteConversationTitle);

                // Pressing a damaged row opens nothing; pressing an available one does.
                damagedRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                availableRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([available], opened);

                // Delete and Backspace ask to delete either row.
                damagedRow.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Delete,
                });
                availableRow.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Back,
                });
                Assert.Equal([damaged, available], deleted);

                // So does the context menu's item.
                var damagedMenu = Assert.IsType<ContextMenu>(damagedRow.ContextMenu);
                damagedMenu.Open(damagedRow);
                window.UpdateLayout();
                var damagedDelete = Assert.Single(damagedMenu.Items.OfType<MenuItem>());
                Assert.True(damagedDelete.IsEffectivelyEnabled);
                damagedDelete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                damagedMenu.Close();
                Assert.Equal([damaged, available, damaged], deleted);
                var damagedItem = viewModel.Conversations.Single(item => item.RunId == damaged);
                Assert.Equal(
                    $"Broken notes, Damaged — can't open or export · {damagedItem.UpdatedAt}",
                    AutomationProperties.GetName(damagedRow));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
}
