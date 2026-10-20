using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Asura.Docker;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Typing a name in a Docker or Kubernetes list selects the first row that
/// starts with it and keeps focus there, as in any Mac list. Before, no list
/// in the app answered to typing at all.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ContainerTypeSelectHeadlessTests
{
    [Fact]
    public Task Typing_a_name_selects_the_matching_Docker_image() => RunHeadlessAsync(async () =>
    {
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(), "Docker", new NamedResourcesDockerClient(), BuiltInConnections.Local);
        await panel.Initialization;
        panel.SelectSection(DockerPanelSection.Images);
        var view = new DockerRuntimePanelView { DataContext = panel };
        var window = new Window { Width = 1_200, Height = 700, Content = view };
        try
        {
            window.Show();
            var list = view.FindControl<ListBox>("FlatResourceHost")!;
            await WaitUntilAsync(() =>
            {
                window.UpdateLayout();
                return list.GetVisualDescendants().OfType<ListBoxItem>().Count() == 3;
            });
            Assert.True(list.IsTextSearchEnabled);
            list.GetVisualDescendants().OfType<ListBoxItem>().First().Focus();

            window.KeyTextInput("b");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("bitnami/redis:7", Assert.IsType<DockerResourceItemViewModel>(list.SelectedItem).Title);

            window.KeyTextInput("u");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("busybox:1", Assert.IsType<DockerResourceItemViewModel>(list.SelectedItem).Title);
            Assert.Same(list.SelectedItem, panel.SelectedResource);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Typing_a_name_selects_and_focuses_the_matching_Docker_container() => RunHeadlessAsync(async () =>
    {
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(), "Docker", new NamedResourcesDockerClient(), BuiltInConnections.Local);
        await panel.Initialization;
        var view = new DockerRuntimePanelView { DataContext = panel };
        var window = new Window { Width = 1_200, Height = 700, Content = view };
        try
        {
            window.Show();
            var rows = Array.Empty<Button>();
            await WaitUntilAsync(() =>
            {
                window.UpdateLayout();
                rows = [.. view.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("DockerResourceRow"))];
                return rows.Length == 3;
            });
            rows[0].Focus();

            window.KeyTextInput("w");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("web", panel.SelectedResource?.Title);
            window.KeyTextInput("o");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal("worker", panel.SelectedResource?.Title);
            var focused = Assert.IsType<Button>(window.FocusManager?.GetFocusedElement());
            Assert.Same(panel.SelectedResource, focused.DataContext);
            Assert.Contains("selected", focused.Classes, StringComparer.Ordinal);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// On the shell view a click opens a shell in the container clicked, but a
    /// typed name passes over other containers on the way: "wo" matches web
    /// before worker. Only the container the typing stops at gets a shell.
    /// </summary>
    [Fact]
    public Task Typing_on_the_shell_view_opens_a_shell_only_where_the_typing_stops() => RunHeadlessAsync(async () =>
    {
        using var panel = new DockerRuntimePanelViewModel(
            PanelInstanceId.New(), "Docker", new NamedResourcesDockerClient(), BuiltInConnections.Local);
        await panel.Initialization;
        panel.SelectResource(panel.ContainerStacks.SelectMany(stack => stack.Containers)
            .Single(container => string.Equals(container.Title, "api", StringComparison.Ordinal)));
        panel.SelectDetail(DockerPanelDetail.Shell);
        var view = new DockerRuntimePanelView { DataContext = panel };
        var requested = new List<string?>();
        view.InlineShellRequested += (_, shellPanel) => requested.Add(shellPanel.SelectedResource?.Title);
        var window = new Window { Width = 1_200, Height = 700, Content = view };
        try
        {
            window.Show();
            var rows = Array.Empty<Button>();
            await WaitUntilAsync(() =>
            {
                window.UpdateLayout();
                rows = [.. view.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("DockerResourceRow"))];
                return rows.Length == 3;
            });
            rows[0].Focus();

            window.KeyTextInput("w");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("web", panel.SelectedResource?.Title);
            window.KeyTextInput("o");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("worker", panel.SelectedResource?.Title);
            Assert.True(panel.IsShellDetail);
            Assert.Empty(requested);

            await WaitUntilAsync(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return requested.Count > 0;
            });
            Assert.Equal(["worker"], requested);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public async Task Typing_a_name_in_the_Kubernetes_navigator_shows_that_resource_type()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession
            {
                DiscoveryResources =
                [
                    KubernetesUiSession.Pods,
                    new("apps", "v1", "deployments", "Deployment", true, ["list", "get"]),
                    new("apps", "v1", "daemonsets", "DaemonSet", true, ["list", "get"]),
                ],
            };
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
                new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var navigation = view.FindControl<ItemsControl>("NavigationList")!;
            Row("Pods").Focus();

            window.KeyTextInput("d");
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("deployments", model.SelectedKind?.Resource);

            window.KeyTextInput("a");
            await model.SelectionLoading;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("daemonsets", model.SelectedKind?.Resource);
            Assert.Same(Row("Daemon Sets"), window.FocusManager?.GetFocusedElement());
            window.Close();
            return true;

            Button Row(string title) => navigation.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.DataContext is KubernetesNavigationItem item
                    && string.Equals(item.Title, title, StringComparison.Ordinal));
        }, timeout.Token));
    }

    [Fact]
    public async Task Typing_a_name_in_the_Kubernetes_table_selects_the_matching_row()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession
            {
                ListItems = _ => [Pod("api-1"), Pod("web-1"), Pod("worker-1")],
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
            grid.Focus();

            window.KeyTextInput("w");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("web-1", Assert.IsType<KubernetesResourceRow>(grid.SelectedItem).Name);

            window.KeyTextInput("O");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("worker-1", Assert.IsType<KubernetesResourceRow>(grid.SelectedItem).Name);
            window.Close();
            return true;
        }, timeout.Token));

        static KubernetesResourceDocument Pod(string name) => KubernetesUiSession.Pod with
        { Reference = KubernetesUiSession.Pod.Reference with { Name = name, Uid = name } };
    }

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

        Assert.Fail("The panel did not reach the expected state in time.");
    }

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(
                async () =>
                {
                    await assertion();
                    return true;
                },
                timeout.Token));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>A Docker engine with a few containers and images to type at.</summary>
    private sealed class NamedResourcesDockerClient : IDockerEngineClient
    {
        public ValueTask<DockerResult<DockerEngineSnapshot>> ReadSnapshotAsync(
            ConnectionProfile connection,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerEngineSnapshot>>(
                new DockerResult<DockerEngineSnapshot>.Success(new DockerEngineSnapshot(
                    new DockerEngineSummary("29.4.0", "linux", "arm64", "1.52"),
                    [Container("api"), Container("web"), Container("worker")],
                    [Image("alpine", "3"), Image("bitnami/redis", "7"), Image("busybox", "1")],
                    [],
                    [],
                    DateTimeOffset.UtcNow)));

        public ValueTask<DockerResult<IReadOnlyList<DockerVolumeUsage>>> ReadVolumeUsageAsync(
            ConnectionProfile connection,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<IReadOnlyList<DockerVolumeUsage>>>(
                new DockerResult<IReadOnlyList<DockerVolumeUsage>>.Success([]));

        public ValueTask<DockerResult<DockerResourceInspection>> InspectAsync(
            ConnectionProfile connection,
            DockerResourceReference resource,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerResourceInspection>>(
                new DockerResult<DockerResourceInspection>.Success(
                    new DockerResourceInspection(resource, [], "{}")));

        public ValueTask<DockerResult<DockerContainerLogPage>> ReadContainerLogsAsync(
            ConnectionProfile connection,
            DockerContainerLogRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerContainerLogPage>>(
                new DockerResult<DockerContainerLogPage>.Success(
                    new DockerContainerLogPage([], false, null, null)));

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
            ValueTask.FromResult<DockerResult<string>>(new DockerResult<string>.Success("/bin/sh"));

        public ValueTask<DockerResult<DockerFileListing>> ListFilesAsync(
            ConnectionProfile connection,
            DockerResourceReference resource,
            string path,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<DockerFileListing>>(
                new DockerResult<DockerFileListing>.Success(new DockerFileListing(resource, path, [])));

        public ValueTask<DockerResult<bool>> RunContainerActionAsync(
            ConnectionProfile connection,
            string containerId,
            DockerContainerAction action,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<DockerResult<bool>>(new DockerResult<bool>.Success(true));

        private static DockerContainerSummary Container(string name) => new(
            name, name, $"demo/{name}:latest", "running", "Up 1 hour", string.Empty,
            "1 hour ago", "—", "—", "—", "—", "demo", name);

        private static DockerImageSummary Image(string repository, string tag) => new(
            $"{repository}-id", repository, tag, "10 MB", "2 days ago");
    }
}
