namespace Asura.Browser.Tests;

public sealed class HostedBrowserPopupTests
{
    [Theory]
    [InlineData("")]
    [InlineData("about:blank")]
    [InlineData("https://login.example.test/start")]
    [InlineData("http://127.0.0.1/callback")]
    public void User_popups_allow_blank_and_supported_navigation_without_recreation(string target)
    {
        Assert.True(CefBrowserView.CanOpenPopup(false, true, CefBrowserContentPolicy.Ordinary, true, target));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@example.test")]
    [InlineData("not a URL")]
    public void Popup_creation_cannot_bypass_the_supported_address_boundary(string target)
    {
        Assert.False(CefBrowserView.CanOpenPopup(false, true, CefBrowserContentPolicy.Ordinary, true, target));
    }

    [Fact]
    public void Unowned_non_user_and_restricted_preview_popups_stay_closed()
    {
        Assert.False(CefBrowserView.CanOpenPopup(true, true, CefBrowserContentPolicy.Ordinary, true, ""));
        Assert.False(CefBrowserView.CanOpenPopup(false, false, CefBrowserContentPolicy.Ordinary, true, ""));
        Assert.False(CefBrowserView.CanOpenPopup(false, true, CefBrowserContentPolicy.Ordinary, false, ""));
        Assert.False(CefBrowserView.CanOpenPopup(false, true, CefBrowserContentPolicy.RestrictedLocalPreview, true, ""));
    }
}
