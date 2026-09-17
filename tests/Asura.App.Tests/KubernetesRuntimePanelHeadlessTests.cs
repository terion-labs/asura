using System.ComponentModel;
using System.Threading.Channels;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
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
            var picker = view.FindControl<ComboBox>("NamespacePicker")!;
            Assert.Equal("configured", model.Namespace);
            Assert.Equal("configured", picker.SelectedItem);
            resources.SetResult(new([], "1", null, false));
            namespaces.SetResult(new([
                new(new("", "v1", "namespaces", null, "another", "namespace-uid", "1"), "Namespace", "", "{}")], "1", null, false));
            await model.Initialization;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("another", picker.Items.Cast<string>(), StringComparer.Ordinal);
            Assert.Equal("configured", model.Namespace);
            Assert.Equal("configured", picker.SelectedItem);
            model.SelectedKind = model.Kinds.Single(kind => kind.Resource == "pods");
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.True(picker.IsVisible);
            Assert.Equal("configured", picker.SelectedItem);
            picker.SelectedItem = "All namespaces";
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, model.Namespace);
            Assert.Equal("All namespaces", picker.SelectedItem);
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
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var browser = view.FindControl<Grid>("ResourceBrowser")!;
            var inspector = view.FindControl<Border>("Inspector")!;
            Assert.True(browser.IsVisible);
            Assert.False(inspector.IsVisible);
            Assert.Null(view.FindControl<DataGrid>("ResourceList")!.SelectedItem);
            Assert.True(model.IsNavigatorVisible);
            Assert.Equal(0, model.InspectorColumnWidth.Value);
            var namespacePicker = view.FindControl<ComboBox>("NamespacePicker")!;
            Assert.Equal("restricted", namespacePicker.SelectedItem);
            Assert.Contains("All namespaces", namespacePicker.Items.Cast<string>(), StringComparer.Ordinal);
            Assert.Contains("restricted", namespacePicker.Items.Cast<string>(), StringComparer.Ordinal);
            namespacePicker.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(namespacePicker.IsDropDownOpen);
            namespacePicker.SelectedItem = "All namespaces";
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, model.Namespace);
            Assert.Equal("All namespaces", namespacePicker.SelectedItem);
            namespacePicker.IsDropDownOpen = false;
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.True(browser.IsVisible);
            Assert.True(inspector.IsVisible);
            Assert.Equal(480, model.InspectorColumnWidth.Value);
            Assert.Same(opaqueDrawerSurface, inspector.Background);
            var drawerBrush = Assert.IsAssignableFrom<ISolidColorBrush>(inspector.Background);
            Assert.Equal(byte.MaxValue, drawerBrush.Color.A);
            Assert.Equal(1, drawerBrush.Opacity);
            Assert.False(model.HasMetricsProviderChoices);
            Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible
                && text.Text?.Contains("No eligible Prometheus service", StringComparison.Ordinal) == true);
            model.SelectedResource = null;
            window.Width = 420;
            Dispatcher.UIThread.RunJobs();
            Assert.True(browser.IsVisible);
            Assert.False(inspector.IsVisible);
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.False(browser.IsVisible);
            Assert.True(inspector.IsVisible);
            Assert.True(view.FindControl<Button>("BackButton")!.IsVisible);
            Assert.InRange(inspector.Bounds.Width, 1, 420);
            view = new KubernetesRuntimePanelView { DataContext = model };
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            browser = view.FindControl<Grid>("ResourceBrowser")!;
            inspector = view.FindControl<Border>("Inspector")!;
            Assert.False(browser.IsVisible);
            Assert.True(inspector.IsVisible);
            Assert.InRange(inspector.Bounds.Width, 1, 420);
            model.SelectedResource = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(browser.IsVisible);
            Assert.False(inspector.IsVisible);
            window.Close();
            return true;
        }, timeout.Token));
    }
}
