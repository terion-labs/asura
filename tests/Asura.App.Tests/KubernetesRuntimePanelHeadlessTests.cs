using System.ComponentModel;
using System.Threading.Channels;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class KubernetesRuntimePanelHeadlessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamespaceSelectionRemainsVisibleWhenDiscoveryCompletesAfterViewAttachment(bool startWithNodes)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var resources = new TaskCompletionSource<KubernetesResourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var namespaces = new TaskCompletionSource<KubernetesResourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new KubernetesUiSession
            {
                DiscoveryResources = [KubernetesUiSession.Pods, new("", "v1", "nodes", "Node", false, ["list"]),
                    new("", "v1", "namespaces", "Namespace", false, ["list"])],
                NamespacePage = namespaces.Task,
                ResourcePage = resources.Task,
            };
            var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1,
                "Cluster", "/config", "context", "configured");
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile,
                _ => ValueTask.FromResult<IKubernetesClientSession>(client),
                startWithNodes ? new(profile.Id, "configured", "nodes") : null);
            Assert.False(model.Initialization.IsCompleted);
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var picker = view.FindControl<Button>("NamespacePicker")!;
            var choices = view.FindControl<ListBox>("NamespaceList")!;
            Assert.Equal("configured", model.Namespace);
            Assert.Equal("configured", model.NamespaceSelection);
            resources.SetResult(new([], "1", null, false));
            namespaces.SetResult(new([
                new(new("", "v1", "namespaces", null, "another", "namespace-uid", "1"), "Namespace", "", "{}")], "1", null, false));
            await model.Initialization;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("another", model.FilteredNamespaceChoices, StringComparer.Ordinal);
            Assert.Equal("configured", model.Namespace);
            Assert.Equal("configured", model.NamespaceSelection);
            model.SelectedKind = model.Kinds.Single(kind => kind.Resource == "pods");
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.IsVisible);
            picker.Flyout!.ShowAt(picker);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("configured", choices.SelectedItem);
            model.NamespaceFilter = "all";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("All namespaces", Assert.Single(choices.Items.Cast<string>()));
            choices.SelectedItem = "All namespaces";
            choices.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.False(picker.Flyout.IsOpen);
            Assert.Equal(string.Empty, model.Namespace);
            Assert.Equal("All namespaces", model.NamespaceSelection);
            window.Close();
            return true;
        }, timeout.Token));
    }

    [Fact]
    public async Task TableSortSurvivesRowsReplacedByFilteringAndWatch()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var changes = Channel.CreateUnbounded<KubernetesWatchEvent>();
            var client = new KubernetesUiSession
            {
                ExtraFeatures = KubernetesSessionFeatures.Watch,
                WatchChanges = changes,
                ListItems = _ => [Pod("pod-a"), Pod("pod-b"), Pod("pod-c")],
            };
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
                new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = view.FindControl<DataGrid>("ResourceList")!;
            grid.Columns[0].Sort(ListSortDirection.Descending);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["pod-c", "pod-b", "pod-a"], Names());
            model.Filter = "pod-b";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["pod-b"], Names());
            model.Filter = string.Empty;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["pod-c", "pod-b", "pod-a"], Names());

            var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.PropertyChanged += OnChanged;
            try
            {
                await changes.Writer.WriteAsync(new(KubernetesWatchEventKind.Added, "11", Pod("pod-d")), timeout.Token);
                await updated.Task.WaitAsync(timeout.Token);
            }
            finally { model.PropertyChanged -= OnChanged; }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["pod-d", "pod-c", "pod-b", "pod-a"], Names());
            window.Close();
            return true;

            string[] Names() => [.. grid.CollectionView.Cast<KubernetesResourceRow>().Select(row => row.Name)];
            void OnChanged(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(model.Rows) && model.Rows.Count == 4) { updated.TrySetResult(); }
            }
        }, timeout.Token));

        static KubernetesResourceDocument Pod(string name) => KubernetesUiSession.Pod with
        { Reference = KubernetesUiSession.Pod.Reference with { Name = name, Uid = name } };
    }

    [Fact]
    public async Task DetailsDrawerSlidesOpenAndShut()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession { DiscoveryResources = [KubernetesUiSession.Pods] };
            var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context");
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile,
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var drawer = view.FindControl<Border>("Inspector")!;
            Assert.Equal(0, drawer.Bounds.Width);
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(drawer.IsAnimating(Layoutable.WidthProperty));
            Assert.Equal(480, model.InspectorWidth);
            model.SelectedResource = null;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(drawer.IsAnimating(Layoutable.WidthProperty));
            Assert.Equal(0, model.InspectorWidth);
            Assert.Equal(479, model.InspectorContentWidth);
            window.Close();
            return true;
        }, timeout.Token));
    }

    [Fact]
    public async Task NavigatorSlidesRatherThanSnaps()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession { DiscoveryResources = [KubernetesUiSession.Pods] };
            var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context");
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile,
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var navigator = view.FindControl<Border>("Navigator")!;
            Assert.Equal(190, navigator.Bounds.Width);
            model.ToggleNavigator();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(navigator.IsAnimating(Layoutable.WidthProperty));
            window.Close();
            return true;
        }, timeout.Token));
    }

    [Fact]
    public async Task ResourceInspectorBecomesASinglePaneInNarrowSplits()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession
            {
                ExtraFeatures = KubernetesSessionFeatures.Metrics | KubernetesSessionFeatures.MetricHistory,
                Metrics = _ => ValueTask.FromResult(new KubernetesMetricsSnapshot(KubernetesDataAvailability.Unavailable, [])),
                ListItems = request => string.Equals(request.ApiResource.Resource, "services", StringComparison.Ordinal) ? [] : [KubernetesUiSession.Pod],
            };
            var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1,
                "Production", "/config", "production", "restricted");
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile,
                _ => ValueTask.FromResult<Asura.Application.IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Resources["ShellSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(64, 24, 24, 24));
            var opaqueDrawerSurface = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            window.Resources["ShellPopupSurfaceBrush"] = opaqueDrawerSurface;
            // Layout assertions read settled widths; the slide itself has its own test.
            window.Classes.Add("motion-disabled");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var browser = view.FindControl<Grid>("ResourceBrowser")!;
            var inspector = view.FindControl<Border>("Inspector")!;
            Assert.True(browser.IsVisible);
            Assert.False(inspector.IsEnabled);
            Assert.Null(view.FindControl<DataGrid>("ResourceList")!.SelectedItem);
            Assert.True(model.IsNavigatorVisible);
            Assert.Equal(0, model.InspectorColumnWidth.Value);
            var namespacePicker = view.FindControl<Button>("NamespacePicker")!;
            var namespaceList = view.FindControl<ListBox>("NamespaceList")!;
            Assert.Equal("restricted", model.NamespaceSelection);
            namespacePicker.Flyout!.ShowAt(namespacePicker);
            Dispatcher.UIThread.RunJobs();
            Assert.True(namespacePicker.Flyout.IsOpen);
            Assert.Contains("All namespaces", namespaceList.Items.Cast<string>(), StringComparer.Ordinal);
            Assert.Contains("restricted", namespaceList.Items.Cast<string>(), StringComparer.Ordinal);
            namespaceList.SelectedItem = "All namespaces";
            namespaceList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.False(namespacePicker.Flyout.IsOpen);
            Assert.Equal(string.Empty, model.Namespace);
            Assert.Equal("All namespaces", model.NamespaceSelection);
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.True(browser.IsVisible);
            Assert.True(inspector.IsEnabled);
            Assert.Equal(480, model.InspectorColumnWidth.Value);
            Assert.Same(opaqueDrawerSurface, inspector.Background);
            var drawerBrush = Assert.IsAssignableFrom<ISolidColorBrush>(inspector.Background);
            Assert.Equal(byte.MaxValue, drawerBrush.Color.A);
            Assert.Equal(1, drawerBrush.Opacity);
            Assert.False(model.HasMetricsProviderChoices);
            Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible
                && text.Text?.Contains("No Prometheus or VictoriaMetrics query service", StringComparison.Ordinal) == true);
            model.SelectedResource = null;
            window.Width = 420;
            Dispatcher.UIThread.RunJobs();
            Assert.True(browser.IsVisible);
            Assert.False(inspector.IsEnabled);
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.False(browser.IsVisible);
            Assert.True(inspector.IsEnabled);
            Assert.True(view.FindControl<Button>("BackButton")!.IsVisible);
            Assert.InRange(inspector.Bounds.Width, 1, 420);
            view = new KubernetesRuntimePanelView { DataContext = model };
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            browser = view.FindControl<Grid>("ResourceBrowser")!;
            inspector = view.FindControl<Border>("Inspector")!;
            Assert.False(browser.IsVisible);
            Assert.True(inspector.IsEnabled);
            Assert.InRange(inspector.Bounds.Width, 1, 420);
            model.SelectedResource = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(browser.IsVisible);
            Assert.False(inspector.IsEnabled);
            window.Close();
            return true;
        }, timeout.Token));
    }

    /// <summary>
    /// The inspector is five stable views in one segmented control. A view
    /// that does not apply is dimmed rather than removed, and when the view
    /// showing stops applying the inspector steps back to Overview.
    /// </summary>
    [Fact]
    public async Task InspectorViewsAreFiveStableSegmentsThatDimInsteadOfDisappearing()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var deployments = new KubernetesApiResource("apps", "v1", "deployments", "Deployment", true, ["list", "get", "patch", "delete"]);
            var client = new KubernetesUiSession
            {
                AllowPatching = true,
                DiscoveryResources = [KubernetesUiSession.Pods, deployments],
                ListItems = request => request.ApiResource.Resource is "deployments"
                    ? [new(new("apps", "v1", "deployments", "restricted", "api", "deploy-uid", "10"), "Deployment", "Available", "{\"kind\":\"Deployment\"}")]
                    : [KubernetesUiSession.Pod],
            };
            var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context", "restricted");
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile,
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1400, Height = 700 };
            window.Classes.Add("motion-disabled");
            window.Show();
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();

            var segments = view.FindControl<Asura.App.Controls.SegmentedControl>("InspectorSegments")!;
            var host = view.FindControl<TabControl>("InspectorTabs")!;
            var radios = segments.GetVisualDescendants().OfType<RadioButton>().ToArray();
            Assert.Equal(["Overview", "Manifest", "Logs", "Ports", "Actions"], radios.Select(radio => radio.Content as string), StringComparer.Ordinal);
            Assert.Equal(0, segments.SelectedIndex);
            Assert.True(radios[2].IsEffectivelyEnabled);

            segments.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, host.SelectedIndex);
            Assert.Equal("Logs", Assert.IsType<TabItem>(host.SelectedItem).Header);

            model.SelectedKind = deployments;
            await model.SelectionLoading;
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.All(radios, radio => Assert.True(radio.IsVisible));
            Assert.False(radios[2].IsEffectivelyEnabled);
            Assert.True(radios[4].IsEffectivelyEnabled);
            Assert.Equal(0, segments.SelectedIndex);
            Assert.Equal(0, host.SelectedIndex);
            window.Close();
            return true;
        }, timeout.Token));
    }

    /// <summary>
    /// The resource table takes an extended selection, and its context menu
    /// offers the inspector's tools, copying, and Delete… last. Commands for
    /// one resource are dimmed while several are selected; copying takes them all.
    /// </summary>
    [Fact]
    public async Task ResourceTableSelectsSeveralRowsAndOffersAContextMenu()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession
            {
                AllowPatching = true,
                ExtraFeatures = KubernetesSessionFeatures.Exec,
                ListItems = _ => [Pod("pod-a"), Pod("pod-b"), Pod("pod-c")],
            };
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
                new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1400, Height = 700 };
            window.Classes.Add("motion-disabled");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = view.FindControl<DataGrid>("ResourceList")!;
            Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);

            grid.SelectedItem = model.Rows[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            var menu = view.CreateResourceContextMenu();
            Assert.Equal(
                ["Show Logs", "Open Shell", "Port Forward…", "Edit Manifest…", "-", "Copy Name", "-", "Delete…"],
                Headers(menu),
                StringComparer.Ordinal);
            Assert.All(menu.Items.OfType<MenuItem>().Where(item => !Equals(item.Header, "Port Forward…")), item => Assert.True(item.IsEnabled));

            grid.SelectedItems.Add(model.Rows[1]);
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, grid.SelectedItems.Count);
            menu = view.CreateResourceContextMenu();
            var items = menu.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header!, StringComparer.Ordinal);
            Assert.True(items["Copy Names"].IsEnabled);
            Assert.False(items["Show Logs"].IsEnabled);
            Assert.False(items["Delete…"].IsEnabled);

            grid.SelectedItem = model.Rows[2];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            menu = view.CreateResourceContextMenu();
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Delete…"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (var attempt = 0; attempt < 100 && client.Mutations.Count == 0; attempt++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            var review = Assert.Single(client.Mutations);
            Assert.Equal(KubernetesMutationKind.Delete, review.Kind);
            Assert.True(review.DryRun);
            Assert.Equal("pod-c", model.SelectedResource?.Reference.Name);
            Assert.True(model.HasOperationReview);
            Assert.Equal(4, view.FindControl<Asura.App.Controls.SegmentedControl>("InspectorSegments")!.SelectedIndex);
            window.Close();
            return true;

            static string[] Headers(ContextMenu menu) => [.. menu.Items.Select(item => item switch
            {
                MenuItem menuItem => menuItem.Header as string ?? string.Empty,
                Separator => "-",
                _ => "?",
            })];
        }, timeout.Token));

        static KubernetesResourceDocument Pod(string name) => KubernetesUiSession.Pod with
        { Reference = KubernetesUiSession.Pod.Reference with { Name = name, Uid = name } };
    }

    /// <summary>
    /// The header's mark names the cluster's state, and becomes a glyph where
    /// colour cannot carry it.
    /// </summary>
    [Fact]
    public async Task ConnectionStatusMarkGainsAGlyphWithoutColor()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
                new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
                _ => ValueTask.FromResult<IKubernetesClientSession>(new KubernetesUiSession()));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var dot = Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("KubernetesStatusDot"));
            var glyph = Assert.Single(
                view.GetVisualDescendants().OfType<FluentIcons.Avalonia.SymbolIcon>(),
                icon => icon.Classes.Contains("KubernetesStatusGlyph") && ReferenceEquals(icon.Parent, dot.Parent));
            Assert.Equal("Connected to context", AutomationProperties.GetName(dot));
            Assert.True(dot.IsEffectivelyVisible);
            Assert.False(glyph.IsEffectivelyVisible);

            window.Classes.Add("differentiate-without-color");
            Dispatcher.UIThread.RunJobs();
            Assert.False(dot.IsEffectivelyVisible);
            Assert.True(glyph.IsEffectivelyVisible);
            Assert.Equal(FluentIcons.Common.Symbol.CheckmarkCircle, glyph.Symbol);
            window.Close();
            return true;
        }, timeout.Token));
    }
}
