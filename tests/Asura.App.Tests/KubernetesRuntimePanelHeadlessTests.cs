using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class KubernetesRuntimePanelHeadlessTests
{
    [Fact]
    public async Task ResourceInspectorBecomesASinglePaneInNarrowSplits()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession();
            var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1,
                "Production", "/config", "production", "restricted");
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes", profile,
                _ => ValueTask.FromResult<Asura.Application.IKubernetesClientSession>(client));
            await model.Initialization;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var browser = view.FindControl<Grid>("ResourceBrowser")!;
            var inspector = view.FindControl<Border>("Inspector")!;
            Assert.True(browser.IsVisible);
            Assert.True(inspector.IsVisible);
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
