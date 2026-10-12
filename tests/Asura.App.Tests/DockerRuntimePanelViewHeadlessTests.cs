using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Asura.Docker;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using DockerListItem = Asura.App.Controls.ListItem;
using IdentityTile = Asura.App.Controls.IdentityTile;
using SegmentedControl = Asura.App.Controls.SegmentedControl;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class DockerRuntimePanelViewHeadlessTests
{
    [Fact]
    public Task DockerResourceListsFillTheirAvailableWidth() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 1_200,
                Height = 700,
                Content = view,
            };

            try
            {
                window.Show();
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return view.GetVisualDescendants()
                        .OfType<Button>()
                        .Any(button => button.Classes.Contains("DockerResourceRow"));
                });

                var familyNavigation = view.FindControl<ListBox>("DockerFamilyNavigation")!;
                var familyButtons = familyNavigation.GetVisualDescendants()
                    .OfType<ListBoxItem>()
                    .Where(item => item.Classes.Contains("DockerNav"))
                    .ToArray();
                Assert.Equal(4, familyButtons.Length);
                Assert.All(
                    familyButtons,
                    button => Assert.InRange(
                        familyNavigation.Bounds.Width - button.Bounds.Width,
                        0,
                        2));

                var stackList = view.FindControl<ItemsControl>("ContainerStackList")!;
                var containerResourceHost = view.FindControl<ScrollViewer>(
                    "ContainerResourceHost")!;
                var flatResourceHost = view.FindControl<ListBox>("FlatResourceHost")!;
                Assert.Equal(
                    Colors.Transparent,
                    Assert.IsAssignableFrom<ISolidColorBrush>(containerResourceHost.Background).Color);
                Assert.Equal(
                    Colors.Transparent,
                    Assert.IsAssignableFrom<ISolidColorBrush>(flatResourceHost.Background).Color);
                Assert.Equal(default, containerResourceHost.BorderThickness);
                Assert.Equal(default, flatResourceHost.BorderThickness);
                Assert.Equal(default, containerResourceHost.Padding);
                Assert.Equal(default, flatResourceHost.Padding);
                var stack = Assert.Single(
                    stackList.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Classes.Contains("DockerStack"));
                var row = Assert.Single(
                    stack.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("DockerResourceRow"));
                var containerList = Assert.IsType<ItemsControl>(
                    row.GetVisualAncestors().OfType<ItemsControl>().First());
                var stackContainer = Assert.Single(
                    stack.GetVisualAncestors().OfType<ContentPresenter>(),
                    presenter => ReferenceEquals(presenter.DataContext, stack.DataContext));
                var rowContainer = Assert.Single(
                    row.GetVisualAncestors().OfType<ContentPresenter>(),
                    presenter => ReferenceEquals(presenter.DataContext, row.DataContext));

                Assert.InRange(
                    stackContainer.Bounds.Width,
                    stackList.Bounds.Width - 1,
                    stackList.Bounds.Width + 1);
                Assert.InRange(
                    rowContainer.Bounds.Width,
                    containerList.Bounds.Width - 1,
                    containerList.Bounds.Width + 1);
                Assert.InRange(
                    rowContainer.Bounds.Width - row.Bounds.Width,
                    1,
                    16);
                var rowRightInStack = row.TranslatePoint(
                    new Point(row.Bounds.Width, 0),
                    stack);
                Assert.NotNull(rowRightInStack);
                Assert.InRange(
                    stack.Bounds.Width - rowRightInStack.Value.X,
                    1,
                    16);
                var rowContent = Assert.Single(
                    row.GetVisualDescendants().OfType<DockerListItem>(),
                    item => item.Classes.Contains("DockerResourceListItem"));
                // Read while attached: the row inset comes from a dynamic
                // resource now, and a detached element resolves it to zero.
                var rowContentPadding = rowContent.ContentPadding;
                var iconTile = Assert.Single(
                    row.GetVisualDescendants().OfType<IdentityTile>());
                var title = Assert.Single(
                    rowContent.GetVisualDescendants().OfType<TextBlock>(),
                    text => string.Equals(text.Name, "PART_Title", StringComparison.Ordinal));
                var titleOrigin = title.TranslatePoint(default, rowContent);
                var iconOrigin = iconTile.TranslatePoint(default, rowContent);
                Assert.NotNull(titleOrigin);
                Assert.NotNull(iconOrigin);
                Assert.InRange(
                    iconOrigin.Value.Y - titleOrigin.Value.Y,
                    2,
                    8);

                var stackToggle = Assert.Single(
                    stack.GetVisualDescendants().OfType<Button>(),
                    button => string.Equals(AutomationProperties.GetName(button)
, "Collapse demo", StringComparison.Ordinal));
                var stackViewModel = Assert.IsType<DockerContainerStackViewModel>(
                    stackToggle.DataContext);
                stackToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.False(stackViewModel.IsExpanded);
                Assert.False(containerList.IsVisible);
                Assert.Equal("Expand demo", AutomationProperties.GetName(stackToggle));
                stackToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.True(stackViewModel.IsExpanded);
                Assert.True(containerList.IsVisible);

                var stopStack = Assert.Single(
                    stack.GetVisualDescendants().OfType<Button>(),
                    button => string.Equals(AutomationProperties.GetName(button), "Stop demo", StringComparison.Ordinal));
                Assert.Equal("Stop every container in demo", ToolTip.GetTip(stopStack));
                Assert.True(stopStack.Bounds.Width >= 28 && stopStack.Bounds.Height >= 28);
                var stopStackCenter = stopStack.TranslatePoint(
                    new Point(stopStack.Bounds.Width / 2, stopStack.Bounds.Height / 2),
                    window);
                Assert.NotNull(stopStackCenter);
                window.MouseDown(stopStackCenter.Value, MouseButton.Left);
                window.MouseUp(stopStackCenter.Value, MouseButton.Left);
                await WaitUntilAsync(() =>
                    client.ContainerActions.Count == 1 && !panel.IsRefreshing);
                Assert.Equal(
                    ("container", DockerContainerAction.Stop),
                    client.ContainerActions[0]);

                var containerActions = Assert.Single(
                    view.GetVisualDescendants().OfType<StackPanel>(),
                    candidate => candidate.Classes.Contains("DockerContainerActions"));
                // Restart, and moving the shell to a tab; the latter shows
                // only with the shell view.
                var actionButtons = containerActions.Children.OfType<Button>().ToArray();
                Assert.Equal(2, actionButtons.Length);
                Assert.False(actionButtons[^1].IsVisible);
                actionButtons = [.. actionButtons.Where(button => button.IsVisible)];
                Assert.All(
                    actionButtons,
                    button => Assert.InRange(
                        Math.Abs(button.Bounds.Height - actionButtons[^1].Bounds.Height),
                        0,
                        1));
                panel.SelectSection(DockerPanelSection.Volumes);
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return view.GetVisualDescendants()
                        .OfType<ListBoxItem>()
                        .Any(item => item.IsVisible);
                });
                var resourceList = Assert.Single(
                    view.GetVisualDescendants().OfType<ListBox>(),
                    list => list.Classes.Contains("DockerResources"));
                var resourceItem = Assert.Single(
                    resourceList.GetVisualDescendants().OfType<ListBoxItem>());
                Assert.InRange(
                    resourceList.Bounds.Width - resourceItem.Bounds.Width,
                    1,
                    16);
                var flatRowContent = Assert.Single(
                    resourceItem.GetVisualDescendants().OfType<DockerListItem>(),
                    item => item.Classes.Contains("DockerResourceListItem"));
                var flatIconTile = Assert.Single(
                    resourceItem.GetVisualDescendants().OfType<IdentityTile>());
                Assert.Equal(rowContentPadding, flatRowContent.ContentPadding);
                Assert.InRange(
                    Math.Abs(rowContent.Bounds.Height - flatRowContent.Bounds.Height),
                    0,
                    1);
                Assert.Equal(iconTile.TileSize, flatIconTile.TileSize);
                Assert.InRange(
                    Math.Abs(iconTile.Bounds.Width - flatIconTile.Bounds.Width),
                    0,
                    1);

                foreach (var section in new[]
                         {
                             DockerPanelSection.Images,
                             DockerPanelSection.Networks,
                         })
                {
                    panel.SelectSection(section);
                    await WaitUntilAsync(() =>
                    {
                        window.UpdateLayout();
                        return resourceList.GetVisualDescendants()
                            .OfType<ListBoxItem>()
                            .Any(item => item.IsEffectivelyVisible);
                    });
                    var sectionItem = Assert.Single(
                        resourceList.GetVisualDescendants().OfType<ListBoxItem>());
                    var sectionRow = Assert.Single(
                        sectionItem.GetVisualDescendants().OfType<DockerListItem>(),
                        item => item.Classes.Contains("DockerResourceListItem"));
                    var sectionIcon = Assert.Single(
                        sectionItem.GetVisualDescendants().OfType<IdentityTile>());

                    Assert.Equal(rowContentPadding, sectionRow.ContentPadding);
                    Assert.InRange(
                        Math.Abs(rowContent.Bounds.Height - sectionRow.Bounds.Height),
                        0,
                        1);
                    Assert.Equal(iconTile.TileSize, sectionIcon.TileSize);
                }
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// A stack's containers were drawn at the same left edge as the stack
    /// header above them, so the list read as one flat column. They sit one
    /// indent step in, the way an open folder's rows do in a Finder list.
    /// </summary>
    [Fact]
    public Task StackContainersAreIndentedUnderTheirStack() =>
        RunHeadlessAsync(async () =>
        {
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                new PagedLogDockerClient(),
                BuiltInConnections.Local);
            await panel.Initialization;
            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_200, Height = 700, Content = view };

            try
            {
                window.Show();
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return view.GetVisualDescendants()
                        .OfType<Button>()
                        .Any(button => button.Classes.Contains("DockerResourceRow") && button.Bounds.Width > 0);
                });

                var stack = Assert.Single(
                    view.GetVisualDescendants().OfType<Grid>(),
                    grid => grid.Classes.Contains("DockerStack"));
                var header = Assert.Single(
                    stack.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("DockerStackHeaderSurface"));
                var row = Assert.Single(
                    stack.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("DockerResourceRow"));
                var headerLeft = header.TranslatePoint(default, stack)!.Value.X;
                var rowLeft = row.TranslatePoint(default, stack)!.Value.X;
                var headerRight = header.TranslatePoint(new Point(header.Bounds.Width, 0), stack)!.Value.X;
                var rowRight = row.TranslatePoint(new Point(row.Bounds.Width, 0), stack)!.Value.X;

                Assert.InRange(rowLeft - headerLeft, 16, 20);
                // Indented on the leading side only: the row still ends where
                // its stack's header ends.
                Assert.Equal(headerRight, rowRight, 1);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// The resource types are a source list, and a source-list row is the
    /// Finder sidebar's 28-point band: on their padding alone the rows came to
    /// about 26 points, under the Git sidebar beside them.
    /// </summary>
    [Fact]
    public Task ResourceTypeRowsAreFinderSidebarHeight() =>
        RunHeadlessAsync(async () =>
        {
            FontManager.Current.AddFontCollection(new Asura.App.Controls.AsuraTerminalFontCollection());
            Assert.IsType<App>(Avalonia.Application.Current).ApplyApplicationResources(
                EffectiveAppearanceResourceMapper.Map(ThemePreference.Default.Resolve(
                    new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null))));
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                new PagedLogDockerClient(),
                BuiltInConnections.Local);
            await panel.Initialization;
            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_200, Height = 700, Content = view };

            try
            {
                window.Show();
                window.UpdateLayout();

                var rows = view.FindControl<ListBox>("DockerFamilyNavigation")!
                    .GetVisualDescendants()
                    .OfType<ListBoxItem>()
                    .Where(item => item.Classes.Contains("DockerNav"))
                    .ToArray();
                Assert.Equal(4, rows.Length);
                Assert.All(rows, row => Assert.True(
                    row.Bounds.Height >= 28,
                    $"A resource type row is {row.Bounds.Height} points tall."));
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task ContainerLifecycleButtonsDispatchFromPointerClicks() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 1_200,
                Height = 700,
                Content = view,
            };

            try
            {
                window.Show();
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return view.GetVisualDescendants()
                        .OfType<Button>()
                        .Any(button => AutomationProperties.GetName(button)
                            == "Restart container"
                            && button.IsEffectivelyVisible
                            && button.Bounds.Width > 0
                            && button.Bounds.Height > 0);
                });

                var restart = Assert.Single(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Restart container");
                var start = Assert.Single(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Start container");
                var stop = Assert.Single(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Stop container");
                Assert.False(start.IsEnabled);
                Assert.False(start.IsEffectivelyVisible);
                Assert.True(stop.IsEffectivelyVisible);
                Assert.True(restart.IsEnabled);
                Assert.NotNull(restart.Command);

                var stopCenter = stop.TranslatePoint(
                    new Point(stop.Bounds.Width / 2, stop.Bounds.Height / 2),
                    window);
                Assert.NotNull(stopCenter);
                window.MouseDown(stopCenter.Value, MouseButton.Left);
                window.MouseUp(stopCenter.Value, MouseButton.Left);
                await WaitUntilAsync(() =>
                    client.ContainerActions.Count == 1 && !panel.IsRefreshing);
                Assert.Equal(
                    ("container", DockerContainerAction.Stop),
                    client.ContainerActions[0]);

                var restartCenter = restart.TranslatePoint(
                    new Point(restart.Bounds.Width / 2, restart.Bounds.Height / 2),
                    window);
                Assert.NotNull(restartCenter);
                window.MouseDown(restartCenter.Value, MouseButton.Left);
                window.MouseUp(restartCenter.Value, MouseButton.Left);

                await WaitUntilAsync(() =>
                    client.ContainerActions.Count == 2 && !panel.IsRefreshing);
                Assert.Equal(
                    ("container", DockerContainerAction.Restart),
                    client.ContainerActions[1]);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task VolumeSizeIndicatorTracksTheUsageRequestLifetime() =>
        RunHeadlessAsync(async () =>
        {
            var volumeUsage = new TaskCompletionSource<IReadOnlyList<DockerVolumeUsage>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new PagedLogDockerClient
            {
                VolumeUsageCompletion = volumeUsage,
            };
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;
            panel.SelectSection(DockerPanelSection.Volumes);

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 1_200,
                Height = 700,
                Content = view,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                var indicator = Assert.Single(
                    view.GetVisualDescendants()
                        .OfType<ProgressBar>(),
                    progress => AutomationProperties.GetName(progress)
                        == "Loading Docker resources");
                Assert.True(indicator.IsVisible);
                Assert.Contains("Loading sizes", panel.ResourceSummary, StringComparison.Ordinal);

                volumeUsage.SetResult(
                    [new DockerVolumeUsage("app-data", "64 MB", 64_000_000)]);
                await panel.VolumeUsageLoading;
                window.UpdateLayout();

                Assert.False(indicator.IsVisible);
                Assert.Equal(ByteSize.Format(64_000_000), Assert.Single(panel.Resources).Subtitle);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task RuntimeUnavailableStateOffersDockerInstallationGuide() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient
            {
                SnapshotError = new DockerError(
                    DockerErrorCode.RuntimeUnavailable,
                    "Docker is not installed in this environment, or its daemon is not running.",
                    true),
            };
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 1_200,
                Height = 700,
                Content = view,
            };

            try
            {
                var requested = false;
                view.DockerInstallGuideRequested += (_, _) => requested = true;
                window.Show();
                window.UpdateLayout();

                var button = view.FindControl<Button>("DockerInstallGuideButton");
                Assert.NotNull(button);
                Assert.True(button.IsEffectivelyVisible);
                Assert.Equal("Install Docker", button.Content);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(requested);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task DetailTabsCollapseFromTheDetailHeaderWidth() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 800,
                Height = 700,
                Content = view,
            };

            try
            {
                window.Show();
                var detailHeader = view.FindControl<Grid>("DockerDetailHeader")!;
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return detailHeader.Classes.Contains("compactDetails");
                });
                var tabHost = Assert.Single(
                    detailHeader.GetVisualDescendants().OfType<SegmentedControl>(),
                    panel => panel.Classes.Contains("DockerDetailTabs"));
                var menuButton = Assert.Single(
                    detailHeader.GetVisualDescendants().OfType<Button>(),
                    button => string.Equals(AutomationProperties.GetName(button)
, "Resource view", StringComparison.Ordinal));
                Assert.False(tabHost.IsEffectivelyVisible);
                Assert.True(menuButton.IsEffectivelyVisible);
                var menuButtonCenter = menuButton.TranslatePoint(
                    new Point(menuButton.Bounds.Width / 2, menuButton.Bounds.Height / 2),
                    window);
                Assert.NotNull(menuButtonCenter);
                window.MouseDown(menuButtonCenter.Value, MouseButton.Left);
                window.MouseUp(menuButtonCenter.Value, MouseButton.Left);
                await WaitUntilAsync(() => menuButton.Flyout?.IsOpen == true);
                Assert.True(menuButton.Flyout?.IsOpen);
                // The pop-up's menu checks the current view, and a view that
                // does not apply is there but dimmed.
                var views = Assert.IsType<MenuFlyout>(menuButton.Flyout).Items.OfType<MenuItem>().ToArray();
                Assert.Equal(
                    ["Info", "Logs", "Shell", "Files", "JSON"],
                    views.Select(item => item.Header as string),
                    StringComparer.Ordinal);
                Assert.All(views, item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));
                Assert.Equal([true, false, false, false, false], views.Select(item => item.IsChecked));
                views[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(panel.IsLogsDetail);
                Assert.True(views[1].IsChecked);
                Assert.False(views[0].IsChecked);
                menuButton.Flyout!.IsOpen = false;

                window.Width = 1_200;
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return !detailHeader.Classes.Contains("compactDetails");
                });
                Assert.True(tabHost.IsEffectivelyVisible);
                Assert.False(menuButton.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task InfoValuesRestoreTheirAvailableWidthAfterCompactLayout() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 1_200,
                Height = 700,
                Content = view,
            };

            try
            {
                window.Show();
                var value = await WaitForInfoValueAsync(view, window);
                var initialWideWidth = value.Bounds.Width;
                Assert.True(initialWideWidth > 400);

                window.Width = 560;
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return view.Classes.Contains("narrowPanel")
                        && value.Bounds.Width < initialWideWidth;
                });

                window.Width = 1_200;
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return !view.Classes.Contains("narrowPanel")
                        && value.Bounds.Width > 400;
                });

                Assert.InRange(
                    Math.Abs(value.Bounds.Width - initialWideWidth),
                    0,
                    2);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task InitialLogsSettleAtTheEndAndRepeatedTopScrollsLoadPreviousPages() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;
            panel.SelectDetail(DockerPanelDetail.Logs);
            await WaitUntilAsync(() => panel.LogRows.Count == 500);

            var view = new DockerRuntimePanelView
            {
                DataContext = panel,
            };
            var window = new Window
            {
                Width = 1_200,
                Height = 700,
                Content = view,
            };

            try
            {
                window.Show();
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    var candidate = view
                        .FindControl<ListBox>("LogList")?
                        .GetVisualDescendants()
                        .OfType<ScrollViewer>()
                        .FirstOrDefault();
                    return candidate is not null
                        && candidate.Extent.Height > candidate.Viewport.Height
                        && IsAtEnd(candidate);
                });

                var logList = view.FindControl<ListBox>("LogList")!;
                var scrollViewer = Assert.Single(
                    logList
                        .GetVisualDescendants()
                        .OfType<ScrollViewer>());
                await Task.Delay(100);
                window.UpdateLayout();
                Assert.True(IsAtEnd(scrollViewer));

                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
                var translatedLogCenter = logList.TranslatePoint(
                    new Point(logList.Bounds.Width / 2, logList.Bounds.Height / 2),
                    window);
                Assert.NotNull(translatedLogCenter);
                window.MouseWheel(
                    translatedLogCenter.Value,
                    new Vector(0, 1),
                    RawInputModifiers.None);
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return client.OlderPageReadCount == 1;
                });
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return panel.LogRows.Count == 1_000 && scrollViewer.Offset.Y > 80;
                });

                Assert.Equal(1_000, panel.LogRows.Count);
                Assert.True(panel.HasOlderLogs);
                Assert.True(scrollViewer.Offset.Y > 80);

                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
                window.MouseWheel(
                    translatedLogCenter.Value,
                    new Vector(0, 1),
                    RawInputModifiers.None);
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return client.OlderPageReadCount == 2;
                });
                await WaitUntilAsync(() =>
                {
                    window.UpdateLayout();
                    return panel.LogRows.Count == 1_500 && scrollViewer.Offset.Y > 80;
                });

                Assert.Equal(1_500, panel.LogRows.Count);
                Assert.False(panel.HasOlderLogs);
                Assert.True(scrollViewer.Offset.Y > 80);
            }
            finally
            {
                window.Close();
            }
        });

    private static bool IsAtEnd(ScrollViewer scrollViewer)
    {
        var maximumOffset = Math.Max(
            0,
            scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        return scrollViewer.Offset.Y >= maximumOffset - 1;
    }

    private static async Task<TextBlock> WaitForInfoValueAsync(
        DockerRuntimePanelView view,
        Window window)
    {
        TextBlock? value = null;
        await WaitUntilAsync(() =>
        {
            window.UpdateLayout();
            value = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(textBlock =>
                    textBlock.Classes.Contains("DockerPropertyValue")
                    && textBlock.IsEffectivelyVisible
                    && textBlock.Bounds.Width > 0);
            return value is not null;
        });
        return value!;
    }

    /// <summary>
    /// The views are a segmented control: choosing a segment shows that view,
    /// a change the view model makes moves the segment with it, and a view
    /// that does not apply to the selection stays in place, dimmed.
    /// </summary>
    [Fact]
    public Task DetailSegmentsFollowTheViewModelBothWays() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var segments = view.FindControl<SegmentedControl>("DockerDetailSegments")!;
                var radios = segments.GetVisualDescendants().OfType<RadioButton>().ToArray();
                Assert.Equal(5, radios.Length);
                Assert.True(radios[0].IsChecked);

                radios[4].IsChecked = true;
                Assert.True(panel.IsJsonDetail);
                Assert.Equal(4, segments.SelectedIndex);

                panel.SelectDetail(DockerPanelDetail.Logs);
                window.UpdateLayout();
                Assert.Equal(1, segments.SelectedIndex);
                Assert.True(radios[1].IsChecked);

                panel.SelectSection(DockerPanelSection.Networks);
                window.UpdateLayout();
                Assert.Equal(0, segments.SelectedIndex);
                Assert.Equal(5, segments.GetVisualDescendants().OfType<RadioButton>().Count());
                Assert.All(radios, radio => Assert.True(radio.IsVisible));
                Assert.Equal(
                    [true, false, false, false, true],
                    radios.Select(radio => radio.IsEffectivelyEnabled));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// The resource types are a list whose selected row is the current type,
    /// so a screen reader hears which one is selected.
    /// </summary>
    [Fact]
    public Task ResourceTypesAreASelectableList() =>
        RunHeadlessAsync(async () =>
        {
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                new PagedLogDockerClient(),
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var list = view.FindControl<ListBox>("DockerFamilyNavigation")!;
                Assert.Equal(0, list.SelectedIndex);
                Assert.Equal("Containers, 1", AutomationProperties.GetName(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0))));

                list.SelectedIndex = 1;
                Assert.True(panel.IsImagesSection);
                panel.SelectSection(DockerPanelSection.Volumes);
                Assert.Equal(2, list.SelectedIndex);
                Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(2)).IsSelected);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// Panel ▸ Docker repeats the resource menu: each item there is a
    /// menu-bar command with the same title, available exactly when the
    /// context menu's copy is, and run by the panel itself.
    /// </summary>
    [Fact]
    public Task TheMenuBarRunsTheResourceMenuOnTheSelection() =>
        RunHeadlessAsync(async () =>
        {
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                new PagedLogDockerClient(),
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var target = (IPanelCommandTarget)view;
                var container = Assert.Single(panel.Resources);
                panel.SelectResource(container);
                var menu = view.CreateResourceContextMenu(panel, container);
                var items = menu.Items.OfType<MenuItem>().ToArray();
                PanelContextMenuAssert.ItemsAreInTheMenuBar(items);
                Assert.All(items, item => Assert.Equal(item.IsEnabled, target.CanExecute((CommandId)item.Tag!)));

                // Start and Stop sit side by side in the menu bar; only the
                // one the context menu shows is available.
                Assert.True(target.CanExecute(BuiltInCommands.DockerStop));
                Assert.False(target.CanExecute(BuiltInCommands.DockerStart));
                Assert.False(target.CanExecute(BuiltInCommands.DockerResume));
                Assert.False(target.CanExecute(BuiltInCommands.KubernetesShowLogs));

                await target.ExecuteAsync(BuiltInCommands.DockerShowLogs);
                Assert.True(panel.IsLogsDetail);
                await target.ExecuteAsync(BuiltInCommands.DockerShowJson);
                Assert.True(panel.IsJsonDetail);
                var shellRequested = false;
                view.ShellRequested += (_, _) => shellRequested = true;
                await target.ExecuteAsync(BuiltInCommands.DockerOpenShell);
                Assert.True(shellRequested);

                panel.SelectSection(DockerPanelSection.Volumes);
                var volume = Assert.Single(panel.Resources);
                PanelContextMenuAssert.ItemsAreInTheMenuBar(
                    view.CreateResourceContextMenu(panel, volume).Items.OfType<MenuItem>());
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// Panel ▸ Docker repeats a stack heading's menu for the stack of the
    /// selected container: the same titles, available exactly when the
    /// heading's copy is, and run on that stack.
    /// </summary>
    [Fact]
    public Task TheMenuBarRunsTheStackMenuOnTheSelectedContainersStack() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var target = (IPanelCommandTarget)view;
                var stack = Assert.Single(panel.ContainerStacks);
                panel.SelectResource(Assert.Single(stack.Containers));

                var items = view.CreateStackContextMenu(panel, stack).Items.OfType<MenuItem>().ToArray();
                PanelContextMenuAssert.ItemsAreInTheMenuBar(items);
                Assert.All(items, item => Assert.Equal(item.IsEnabled, target.CanExecute((CommandId)item.Tag!)));

                // Pause Running and Resume Paused sit side by side in the menu
                // bar; with nothing paused, only Pause is available.
                Assert.True(target.CanExecute(BuiltInCommands.DockerStackPause));
                Assert.False(target.CanExecute(BuiltInCommands.DockerStackResume));
                Assert.False(target.CanExecute(BuiltInCommands.DockerStackStart));
                Assert.Contains(target.States, fact => fact.Key == CommandStates.IsDockerStackCollapsed && !fact.Value);

                await target.ExecuteAsync(BuiltInCommands.DockerStackRestart);
                Assert.Contains(("container", DockerContainerAction.Restart), client.ContainerActions);

                stack = Assert.Single(panel.ContainerStacks);
                await target.ExecuteAsync(BuiltInCommands.DockerStackToggle);
                Assert.False(stack.IsExpanded);
                Assert.Contains(target.States, fact => fact.Key == CommandStates.IsDockerStackCollapsed && fact.Value);
                Assert.Equal(
                    "Expand Stack",
                    view.CreateStackContextMenu(panel, stack).Items.OfType<MenuItem>().Last().Header);

                // Another section's selection has no stack.
                panel.SelectSection(DockerPanelSection.Images);
                panel.SelectResource(Assert.Single(panel.Resources));
                Assert.False(target.CanExecute(BuiltInCommands.DockerStackToggle));
                Assert.Empty(target.States);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// A secondary click on a container selects it and offers what the
    /// toolbar offers: its views, its lifecycle, and copying its name.
    /// </summary>
    [Fact]
    public Task ResourcesOfferAContextMenuThatMirrorsTheToolbar() =>
        RunHeadlessAsync(async () =>
        {
            var client = new PagedLogDockerClient();
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                client,
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_400, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var container = Assert.Single(panel.Resources);
                var menu = view.CreateResourceContextMenu(panel, container);
                Assert.Equal(
                    ["Show Logs", "Browse Files", "Open Shell in New Tab", "-", "Stop", "Restart", "Pause", "-", "Copy Name", "Copy ID"],
                    Headers(menu),
                    StringComparer.Ordinal);
                var stop = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Stop"));
                Assert.Same(panel.StopCommand, stop.Command);

                var shellRequested = false;
                view.ShellRequested += (_, _) => shellRequested = true;
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Open Shell in New Tab"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(shellRequested);
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Show Logs"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(panel.IsLogsDetail);

                // A right-click is the pointer's way to ask for that menu: it
                // selects the row under it first, as Finder does.
                var row = Assert.Single(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("DockerResourceRow"));
                var args = new ContextRequestedEventArgs();
                row.RaiseEvent(args);
                Assert.True(args.Handled);
                Assert.Same(container, panel.SelectedResource);

                panel.SelectSection(DockerPanelSection.Volumes);
                var volume = Assert.Single(panel.Resources);
                Assert.Equal(
                    ["Browse Files", "Show JSON", "-", "Copy Name"],
                    Headers(view.CreateResourceContextMenu(panel, volume)),
                    StringComparer.Ordinal);
                panel.SelectSection(DockerPanelSection.Networks);
                Assert.Equal(
                    ["Show JSON", "-", "Copy Name", "Copy ID"],
                    Headers(view.CreateResourceContextMenu(panel, Assert.Single(panel.Resources))),
                    StringComparer.Ordinal);

                panel.SelectSection(DockerPanelSection.Containers);
                var stack = Assert.Single(panel.ContainerStacks);
                Assert.Equal(
                    ["Start All", "Stop All", "Restart Running", "Pause Running", "-", "Collapse Stack"],
                    Headers(view.CreateStackContextMenu(panel, stack)),
                    StringComparer.Ordinal);
            }
            finally
            {
                window.Close();
            }

            static string[] Headers(ContextMenu menu) => [.. menu.Items.Select(item => item switch
            {
                MenuItem menuItem => menuItem.Header as string ?? string.Empty,
                Separator => "-",
                _ => "?",
            })];
        });

    /// <summary>
    /// The engine's status mark is a dot named for the engine's state, and
    /// where colour cannot carry that state it becomes a glyph with a shape
    /// of its own.
    /// </summary>
    [Fact]
    public Task EngineStatusMarkGainsAGlyphWithoutColor() =>
        RunHeadlessAsync(async () =>
        {
            using var panel = new DockerRuntimePanelViewModel(
                PanelInstanceId.New(),
                "Docker",
                new PagedLogDockerClient(),
                BuiltInConnections.Local);
            await panel.Initialization;

            var view = new DockerRuntimePanelView { DataContext = panel };
            var window = new Window { Width = 1_200, Height = 700, Content = view };
            try
            {
                window.Show();
                window.UpdateLayout();
                var dot = Assert.Single(
                    view.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("DockerEngineStatusDot"));
                var glyph = Assert.Single(
                    view.GetVisualDescendants().OfType<FluentIcons.Avalonia.SymbolIcon>(),
                    icon => icon.Classes.Contains("DockerEngineStatusGlyph"));
                Assert.StartsWith("Docker engine connected", AutomationProperties.GetName(dot), StringComparison.Ordinal);
                Assert.True(dot.IsEffectivelyVisible);
                Assert.False(glyph.IsEffectivelyVisible);
                var success = Assert.IsAssignableFrom<ISolidColorBrush>(dot.Background).Color;
                Assert.True(dot.TryFindResource("ShellSuccessBrush", dot.ActualThemeVariant, out var successToken));
                Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(successToken).Color, success);

                window.Classes.Add("differentiate-without-color");
                window.UpdateLayout();
                Assert.False(dot.IsEffectivelyVisible);
                Assert.True(glyph.IsEffectivelyVisible);
                Assert.Equal(FluentIcons.Common.Symbol.CheckmarkCircle, glyph.Symbol);
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The Docker log view did not reach the expected state in time.");
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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

    private sealed class PagedLogDockerClient : IDockerEngineClient
    {
        public int OlderPageReadCount { get; private set; }

        public List<(string ContainerId, DockerContainerAction Action)> ContainerActions { get; } = [];

        public DockerError? SnapshotError { get; init; }

        public TaskCompletionSource<IReadOnlyList<DockerVolumeUsage>>? VolumeUsageCompletion
        {
            get;
            init;
        }

        public ValueTask<DockerResult<DockerEngineSnapshot>> ReadSnapshotAsync(
            ConnectionProfile connection,
            CancellationToken cancellationToken)
        {
            if (SnapshotError is not null)
            {
                return ValueTask.FromResult<DockerResult<DockerEngineSnapshot>>(
                    new DockerResult<DockerEngineSnapshot>.Failure(SnapshotError));
            }

            return ValueTask.FromResult<DockerResult<DockerEngineSnapshot>>(
                new DockerResult<DockerEngineSnapshot>.Success(new DockerEngineSnapshot(
                    new DockerEngineSummary("29.4.0", "linux", "arm64", "1.52"),
                    [new DockerContainerSummary(
                        "container",
                        "api",
                        "demo/api:latest",
                        "running",
                        "Up 1 hour",
                        string.Empty,
                        "1 hour ago",
                        "—",
                        "—",
                        "—",
                        "—",
                        "demo",
                        "api")],
                    [new DockerImageSummary(
                        "image",
                        "demo/api",
                        "latest",
                        "184 MB",
                        "2 days ago")],
                    [new DockerVolumeSummary(
                        "app-data",
                        "local",
                        "local",
                        "/var/lib/docker/volumes/app-data/_data")],
                    [new DockerNetworkSummary(
                        "network",
                        "app",
                        "bridge",
                        "local",
                        "today")],
                    DateTimeOffset.UtcNow)));
        }

        public async ValueTask<DockerResult<IReadOnlyList<DockerVolumeUsage>>> ReadVolumeUsageAsync(
            ConnectionProfile connection,
            CancellationToken cancellationToken)
        {
            var usage = VolumeUsageCompletion is null
                ? []
                : await VolumeUsageCompletion.Task.WaitAsync(cancellationToken);
            return new DockerResult<IReadOnlyList<DockerVolumeUsage>>.Success(usage);
        }

        public ValueTask<DockerResult<DockerResourceInspection>> InspectAsync(
            ConnectionProfile connection,
            DockerResourceReference resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerResourceInspection>>(
                new DockerResult<DockerResourceInspection>.Success(
                    new DockerResourceInspection(
                        resource,
                        [new DockerInspectionProperty(
                            "Id",
                            "7d012249fbede52f16b253f3aad9b0afbdcacf669a4e29ebf0415dc72eebc334")],
                        "{}")));

        public ValueTask<DockerResult<DockerContainerLogPage>> ReadContainerLogsAsync(
            ConnectionProfile connection,
            DockerContainerLogRequest request,
            CancellationToken cancellationToken)
        {
            if (request.BeforeTimestamp is not null)
            {
                OlderPageReadCount++;
                var isFirstOlderPage = OlderPageReadCount == 1;
                var start = isFirstOlderPage ? 500 : 0;
                return ValueTask.FromResult<DockerResult<DockerContainerLogPage>>(
                    new DockerResult<DockerContainerLogPage>.Success(new DockerContainerLogPage(
                        CreateLines(start, 501),
                        isFirstOlderPage,
                        Timestamp(start),
                        Timestamp(start + 500))));
            }

            return ValueTask.FromResult<DockerResult<DockerContainerLogPage>>(
                new DockerResult<DockerContainerLogPage>.Success(new DockerContainerLogPage(
                    CreateLines(1_000, 500),
                    true,
                    Timestamp(1_000),
                    Timestamp(1_499))));
        }

        public ValueTask<DockerResult<bool>> DownloadContainerLogsAsync(
            ConnectionProfile connection,
            string containerId,
            Stream destination,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<bool>>(new DockerResult<bool>.Success(true));

        public ValueTask<DockerResult<string>> ResolveContainerShellAsync(
            ConnectionProfile connection,
            string containerId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<string>>(
                new DockerResult<string>.Success("/bin/sh"));

        public ValueTask<DockerResult<DockerFileListing>> ListFilesAsync(
            ConnectionProfile connection,
            DockerResourceReference resource,
            string path,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerFileListing>>(
                new DockerResult<DockerFileListing>.Success(
                    new DockerFileListing(resource, path, [])));

        public ValueTask<DockerResult<bool>> RunContainerActionAsync(
            ConnectionProfile connection,
            string containerId,
            DockerContainerAction action,
            CancellationToken cancellationToken)
        {
            ContainerActions.Add((containerId, action));
            return ValueTask.FromResult<DockerResult<bool>>(new DockerResult<bool>.Success(true));
        }

        private static IReadOnlyList<DockerContainerLogLine> CreateLines(int start, int count) =>
            [.. Enumerable.Range(start, count).Select(index => new DockerContainerLogLine(Timestamp(index), $"row {index}"))];

        private static string Timestamp(int index) =>
            $"2026-08-10T12:{index / 60:00}:{index % 60:00}.000000000Z";
    }
}
