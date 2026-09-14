using Asura.Application;
using Asura.Browser;
using Avalonia.Controls;
using Avalonia.Headless;
using Exclr8Cef;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class BrowserPopupRoutingHeadlessTests
{
    [Theory]
    [InlineData(Cef.WindowOpenDisposition.NewForegroundTab, "_blank", "https://docs.example.test/new-tab")]
    [InlineData(Cef.WindowOpenDisposition.NewBackgroundTab, "_blank", "https://docs.example.test/new-tab")]
    [InlineData(Cef.WindowOpenDisposition.NewWindow, "_blank", "https://docs.example.test/new-tab")]
    [InlineData(Cef.WindowOpenDisposition.NewForegroundTab, "", "https://docs.example.test/new-tab")]
    [InlineData(Cef.WindowOpenDisposition.NewBackgroundTab, "", "https://docs.example.test/new-tab")]
    [InlineData(Cef.WindowOpenDisposition.NewWindow, "", "https://docs.example.test/new-tab")]
    [InlineData(Cef.WindowOpenDisposition.NewForegroundTab, "_BLANK", "about:blank")]
    public async Task Target_blank_links_request_a_shell_tab_without_adopting_an_overlay(
        Cef.WindowOpenDisposition disposition, string targetFrameName, string targetUrl)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessUnitTestSession.StartNew(
            typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                using var view = new CefBrowserView();
                var children = Assert.IsType<Grid>(view.View).Children;
                var originalChildCount = children.Count;
                var requests = new List<BrowserNewTabRequestedEventArgs>();
                view.NewTabRequested += (_, args) => requests.Add(args);
                // Tab routing must not touch the reserved native child. A missing
                // child makes accidental adoption fail without starting CEF in this test.
                var popup = new HostPopupEventArgs(
                    null!, targetUrl, targetFrameName, disposition, true);

                view.OnHostPopup(null, popup);

                var request = Assert.Single(requests);
                Assert.Equal(new Uri(popup.TargetUrl), request.Address.Value);
                Assert.True(request.UserGesture);
                Assert.False(popup.IsHosted);
                Assert.Equal(originalChildCount, children.Count);
                return Task.FromResult(true);
            },
            timeout.Token));
    }
}
