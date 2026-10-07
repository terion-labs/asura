using Asura.App;
using Asura.Desktop;

namespace Asura.Architecture.Tests;

public sealed class DesktopWindowLifecycleTests
{
    [Fact]
    public void Startup_failure_alert_puts_quit_leading_and_try_again_as_the_trailing_default()
    {
        var actions = StartupFailureAlert.Actions;

        Assert.Equal(
            [
                StartupFailureActionKind.Quit,
                StartupFailureActionKind.OpenRecoveryWorkspace,
                StartupFailureActionKind.TryAgain,
            ],
            actions.Select(action => action.Kind));
        Assert.Equal(
            ["Quit", "Open Recovery Workspace", "Try Again"],
            actions.Select(action => action.Label),
            StringComparer.Ordinal);
        Assert.True(actions[0].IsCancel);
        Assert.Equal(0, actions[0].Column);
        Assert.Equal(actions.Max(action => action.Column), actions[^1].Column);
        Assert.Single(actions, action => action.IsDefault);
        Assert.True(actions[^1].IsDefault);
        Assert.Single(actions, action => action.IsCancel);
    }

    [Fact]
    public void Window_placements_round_trip_through_their_file()
    {
        var directory = Directory.CreateTempSubdirectory("asura-window-placement-");
        try
        {
            var store = new FileWindowPlacementStore(
                Path.Combine(directory.FullName, "window-placement.json"));
            var placements = new WindowPlacementSet(
                [
                    new WindowPlacement(2, -1440, 25, 1200, 800, WindowPlacementState.Normal),
                    new WindowPlacement(0, 80, 60, 1440, 900, WindowPlacementState.Maximized),
                ],
                new WindowPlacement(1, 120, 100, 900, 700, WindowPlacementState.Normal));

            Assert.Equal(WindowPlacementSet.Empty.Windows, store.Load().Windows);
            store.Save(placements);
            var loaded = store.Load();

            Assert.Equal(placements.Windows, loaded.Windows);
            Assert.Equal(placements.LastClosed, loaded.LastClosed);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Damaged_or_invalid_placements_are_dropped_rather_than_failing_startup()
    {
        var directory = Directory.CreateTempSubdirectory("asura-window-placement-");
        try
        {
            var path = Path.Combine(directory.FullName, "window-placement.json");
            var store = new FileWindowPlacementStore(path);

            File.WriteAllText(path, "{ not json");
            Assert.Empty(store.Load().Windows);

            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": 1,
                  "windows": [
                    { "slot": 0, "x": 10, "y": 20, "width": 800, "height": 600, "state": "Normal" },
                    { "slot": 99, "x": 10, "y": 20, "width": 800, "height": 600, "state": "Normal" },
                    { "slot": 1, "x": 10, "y": 20, "width": -5, "height": 600, "state": "Normal" }
                  ]
                }
                """);
            var loaded = Assert.Single(store.Load().Windows);
            Assert.Equal(0, loaded.Slot);

            File.WriteAllText(path, """{ "schemaVersion": 7, "windows": [] }""");
            Assert.Empty(store.Load().Windows);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
