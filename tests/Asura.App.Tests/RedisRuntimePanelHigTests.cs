using System.Globalization;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The Redis panel against the Mac's conventions: one connection button that
/// changes with the session, a key filter that is a search field, views
/// switched by a segmented control, one primary action in the value pane, and
/// sizes in the reader's own number format.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class RedisRuntimePanelHigTests
{
    [Fact]
    public async Task OneConnectionButtonConnectsOrDisconnects()
    {
        using var panel = RedisPanelFixtures.Panel(new RedisPanelFixtures.StubSession(null));
        await panel.Initialization;

        Assert.True(panel.IsConnected);
        Assert.Equal("Disconnect", panel.ConnectionActionLabel);
        Assert.Equal("Disconnect from Redis", panel.ConnectionActionDescription);
        Assert.True(panel.ToggleConnectionCommand.CanExecute(null));

        panel.ToggleConnectionCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsConnected);
        Assert.Equal("Connect", panel.ConnectionActionLabel);
        Assert.Equal("Connect to Redis", panel.ConnectionActionDescription);

        panel.ToggleConnectionCommand.Execute(null);
        await WaitUntilAsync(() => panel.IsConnected);
        Assert.Equal("Disconnect", panel.ConnectionActionLabel);
    }

    [Fact]
    public async Task AnEmptyKeyFilterListsEveryKey()
    {
        var session = new RedisPanelFixtures.StubSession(null);
        using var panel = RedisPanelFixtures.Panel(session);
        await panel.Initialization;

        Assert.Equal(string.Empty, panel.ScanPattern);
        Assert.False(panel.IsFilteringKeys);

        panel.ScanPattern = "user:*";
        panel.ScanCommand.Execute(null);
        await WaitUntilAsync(() => session.LastScanPattern == "user:*");
        Assert.Equal("user:*", session.LastScanPattern);
        Assert.True(panel.IsFilteringKeys);

        panel.ScanPattern = string.Empty;
        panel.ScanCommand.Execute(null);
        await WaitUntilAsync(() => session.LastScanPattern == "*");
        Assert.Equal("*", session.LastScanPattern);
        Assert.False(panel.IsFilteringKeys);
    }

    [Fact]
    public void SizesAndCountsUseTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("1,5 KB", RedisKeyItemViewModel.FormatBytes(1_536));
            Assert.Equal("1.000 B", RedisKeyItemViewModel.FormatBytes(1_000));
            Assert.Equal("1.234 keys", RedisCounts.Count(1_234, "key", "keys"));
            Assert.Equal("1 key", RedisCounts.Count(1, "key", "keys"));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("1.5 KB", RedisKeyItemViewModel.FormatBytes(1_536));
            Assert.Equal("2 MB", RedisKeyItemViewModel.FormatBytes(2 * 1024 * 1024));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public Task TheViewUsesTheSharedControlsAndOnePrimaryAction() =>
        RunHeadlessAsync(async () =>
        {
            var session = new RedisPanelFixtures.StubSession(null, type: "hash");
            using var panel = RedisPanelFixtures.Panel(session);
            await panel.Initialization;
            panel.SelectedKey = panel.Keys[0];
            panel.SelectedValueEntry = null;

            var view = new RedisRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 950, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();

                // One connection button, labelled with what it will do.
                var connection = view.FindControl<Button>("ConnectionButton")!;
                Assert.Equal("Disconnect", connection.Content);
                Assert.Equal("Disconnect from Redis", AutomationProperties.GetName(connection));
                Assert.DoesNotContain(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => button.IsEffectivelyVisible && Equals(button.Content, "Connect"));

                // The key filter is a search field.
                var filter = Assert.IsType<SearchField>(view.FindControl<TextBox>("KeyPatternBox"));
                Assert.Equal("Filter keys, e.g. user:*", filter.PlaceholderText);

                // The views are one segmented control that follows the panel.
                var views = view.GetVisualDescendants().OfType<SegmentedControl>().Single();
                var segments = views.GetVisualDescendants().OfType<RadioButton>().ToArray();
                Assert.Equal(["Browser", "Search", "Pub/Sub"], segments.Select(segment => segment.Content));
                Assert.True(segments[0].IsChecked);
                segments[2].IsChecked = true;
                Assert.Equal(RedisWorkspacePerspective.PubSub, panel.Perspective);
                panel.Perspective = RedisWorkspacePerspective.Search;
                Assert.Equal(1, views.SelectedIndex);
                panel.Perspective = RedisWorkspacePerspective.Browser;
                window.UpdateLayout();

                // With no field selected the entry form has no Save, so Add
                // Fields is the value area's one primary action.
                Assert.False(panel.ShowsEntrySave);
                var unselected = view.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .ToArray();
                var onlyPrimary = Assert.Single(unselected, button => button.Classes.Contains("PrimaryButton"));
                Assert.Equal("Add Fields", onlyPrimary.Content);

                panel.SelectedValueEntry = panel.ValueEntries[0];
                window.UpdateLayout();
                Assert.True(panel.ShowsEntrySave);

                // Save Field is the value area's one primary action; Add Fields
                // beside it is secondary.
                var visible = view.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .ToArray();
                var primary = Assert.Single(visible, button => button.Classes.Contains("PrimaryButton"));
                Assert.Equal("Save Field", primary.Content);
                var add = Assert.Single(visible, button => Equals(button.Content, "Add Fields"));
                Assert.Contains("SecondaryButton", add.Classes, StringComparer.Ordinal);

                // Deleting the key and removing a field look alike.
                var deleteKey = Assert.Single(visible, button => Equals(button.Content, "Delete Key"));
                var removeField = Assert.Single(visible, button => Equals(button.Content, "Remove Field"));
                Assert.Contains("DestructiveButton", deleteKey.Classes, StringComparer.Ordinal);
                Assert.Contains("DestructiveButton", removeField.Classes, StringComparer.Ordinal);

                // Pub/Sub has one primary action too: Subscribe. Publish and
                // Unsubscribe are push buttons of the same secondary style.
                panel.Perspective = RedisWorkspacePerspective.PubSub;
                window.UpdateLayout();
                var pubSub = view.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .ToArray();
                var subscribe = Assert.Single(pubSub, button => button.Classes.Contains("PrimaryButton"));
                Assert.Equal("Subscribe", subscribe.Content);
                foreach (var label in new[] { "Publish", "Unsubscribe" })
                {
                    var button = Assert.Single(pubSub, button => Equals(button.Content, label));
                    Assert.Contains("SecondaryButton", button.Classes, StringComparer.Ordinal);
                }
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task ReturnFiltersTheKeysAndClearingTheFieldListsThemAll() =>
        RunHeadlessAsync(async () =>
        {
            var session = new RedisPanelFixtures.StubSession(null, searchAvailable: true);
            using var panel = RedisPanelFixtures.Panel(session);
            await panel.Initialization;

            var view = new RedisRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 950, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();

                // Typing alone does not reach the server; Return does.
                var filter = view.FindControl<TextBox>("KeyPatternBox")!;
                filter.Focus();
                filter.Text = "user:*";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("*", session.LastScanPattern);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await WaitUntilAsync(() => session.LastScanPattern == "user:*");
                Assert.True(panel.IsFilteringKeys);

                // Escape clears the field, and a cleared filter lists every key.
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(string.Empty, filter.Text);
                await WaitUntilAsync(() => session.LastScanPattern == "*");
                Assert.False(panel.IsFilteringKeys);

                // Return in the search query runs the query.
                panel.Perspective = RedisWorkspacePerspective.Search;
                panel.SearchIndex = "idx:users";
                window.UpdateLayout();
                var query = view.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => string.Equals(
                        AutomationProperties.GetName(box),
                        "Redis search query",
                        StringComparison.Ordinal));
                query.Focus();
                query.Text = "@name:ada";
                Dispatcher.UIThread.RunJobs();
                Assert.Null(session.LastSearchQuery);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await WaitUntilAsync(() => session.LastSearchQuery == "@name:ada");
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The panel did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                async () =>
                {
                    await assertion();
                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
