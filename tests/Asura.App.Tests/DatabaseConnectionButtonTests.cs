using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The database panel's connection controls as the Redis panel has them: one
/// button that says what it will do, Reconnect out of the toolbar, and a
/// header mark that names the real connection state in words and, without
/// colour, in shape.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DatabaseConnectionButtonTests
{
    [Fact]
    public async Task One_button_connects_or_disconnects_and_reads_as_what_it_will_do()
    {
        using var panel = Panel(new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient());
        await panel.Initialization;

        Assert.True(panel.IsConnected);
        Assert.Equal("Disconnect", panel.ConnectionActionLabel);
        Assert.True(panel.ToggleConnectionCommand.CanExecute(null));
        Assert.True(panel.ReconnectCommand.CanExecute(null));

        panel.ToggleConnectionCommand.Execute(null);
        await WaitUntilAsync(() => !panel.IsConnected);
        Assert.Equal("Connect", panel.ConnectionActionLabel);
        Assert.False(panel.ReconnectCommand.CanExecute(null));

        panel.ToggleConnectionCommand.Execute(null);
        await WaitUntilAsync(() => panel.IsConnected);
        Assert.Equal("Disconnect", panel.ConnectionActionLabel);
    }

    [Fact]
    public void Without_a_target_the_button_has_nothing_to_connect_to()
    {
        using var panel = new DatabaseRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Database",
            new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient());

        Assert.Equal("Connect", panel.ConnectionActionLabel);
        Assert.False(panel.ToggleConnectionCommand.CanExecute(null));
        Assert.Equal(StatusTone.Neutral, panel.ConnectionStatusTone);
        Assert.Equal("No connection", panel.ConnectionStatusLabel);

        panel.ConnectionString = "Data Source=demo.db";
        Assert.True(panel.ToggleConnectionCommand.CanExecute(null));
        Assert.Equal("Not connected", panel.ConnectionStatusLabel);
    }

    [Fact]
    public async Task The_status_mark_follows_the_real_connection_state()
    {
        var client = new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient
        {
            FailWith = "connection refused",
        };
        using var panel = new DatabaseRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Database",
            client);
        panel.ConnectionString = "Data Source=demo.db";

        await panel.ConnectAsync();
        Assert.False(panel.IsConnected);
        Assert.Equal(StatusTone.Danger, panel.ConnectionStatusTone);
        Assert.Equal($"Could not connect to {panel.ConnectionDisplayName}", panel.ConnectionStatusLabel);

        client.FailWith = null;
        await panel.ConnectAsync();
        Assert.True(panel.IsConnected);
        Assert.Equal(StatusTone.Success, panel.ConnectionStatusTone);
        Assert.Equal($"Connected to {panel.ConnectionDisplayName}", panel.ConnectionStatusLabel);

        panel.Disconnect();
        Assert.Equal(StatusTone.Neutral, panel.ConnectionStatusTone);
        Assert.Equal("Not connected", panel.ConnectionStatusLabel);
    }

    [Fact]
    public Task The_toolbar_has_one_connection_button_named_by_its_label() =>
        RunHeadlessAsync(async () =>
        {
            using var panel = Panel(new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient());
            await panel.Initialization;
            var view = new DatabaseRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_200, Height = 760, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();

                var connection = view.FindControl<Button>("ConnectionButton")!;
                Assert.Equal("Disconnect", connection.Content);
                Assert.Equal("Disconnect", AutomationProperties.GetName(connection));
                var visible = view.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .ToArray();
                Assert.DoesNotContain(visible, button => Equals(button.Content, "Connect"));
                Assert.DoesNotContain(visible, button => Equals(button.Content, "Reconnect"));

                // Reconnect waits in the button's context menu.
                var menu = connection.ContextMenu!;
                menu.Open(connection);
                window.UpdateLayout();
                var reconnect = Assert.Single(
                    menu.Items.OfType<MenuItem>(),
                    item => Equals(item.Header, "Reconnect"));
                Assert.Same(panel.ReconnectCommand, reconnect.Command);
                Assert.True(reconnect.IsEffectivelyEnabled);
                menu.Close();

                // The edit action sits in a toolbar group.
                var group = Assert.Single(
                    view.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("ToolbarGroup"));
                var edit = Assert.IsType<Button>(group.Child);
                Assert.Contains("ToolbarGroupAction", edit.Classes, StringComparer.Ordinal);

                connection.Command!.Execute(null);
                await WaitUntilAsync(() => !panel.IsConnected);
                window.UpdateLayout();
                Assert.Equal("Connect", connection.Content);
                Assert.Equal("Connect", AutomationProperties.GetName(connection));
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task The_header_mark_names_the_state_and_shows_a_glyph_without_colour() =>
        RunHeadlessAsync(async () =>
        {
            using var panel = Panel(new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient());
            await panel.Initialization;
            var view = new DatabaseRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_200, Height = 760, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();

                var mark = Assert.Single(view.GetVisualDescendants().OfType<StatusMark>());
                Assert.Equal(StatusTone.Success, mark.Tone);
                Assert.Equal(panel.ConnectionStatusLabel, AutomationProperties.GetName(mark));
                Assert.Equal(panel.ConnectionStatusLabel, ToolTip.GetTip(mark));
                Assert.False(mark.Glyph.IsVisible);

                window.Resources["ShellDifferentiateWithoutColor"] = true;
                window.UpdateLayout();
                Assert.True(mark.Glyph.IsVisible);
                Assert.False(mark.Dot.IsVisible);
                Assert.Equal(Symbol.CheckmarkCircle, mark.Glyph.Symbol);

                panel.Disconnect();
                window.UpdateLayout();
                Assert.Equal(StatusTone.Neutral, mark.Tone);
                Assert.Equal("Not connected", AutomationProperties.GetName(mark));
                Assert.Equal(Symbol.Circle, mark.Glyph.Symbol);
            }
            finally
            {
                window.Close();
            }
        });

    private static DatabaseRuntimePanelViewModel Panel(Asura.Application.IDatabasePanelClient client) =>
        new(
            PanelInstanceId.New(),
            "Database",
            client,
            "sqlite",
            "Data Source=demo.db");

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
