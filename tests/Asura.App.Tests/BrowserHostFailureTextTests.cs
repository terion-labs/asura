using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.Tests;

public sealed class BrowserHostFailureTextTests
{
    [Fact]
    public void AnEngineFailureNeverShowsTheEnginesExceptionText()
    {
        var text = BrowserPresentationHost.HostFailureMessage(
            HostError.Create(HostErrorCode.EngineFailed, "CefSharp: Object reference not set to an instance of an object."));

        Assert.Equal("The browser couldn’t finish that action. Try again.", text);
    }

    [Fact]
    public void AClosedPanelSaysToOpenANewOne()
    {
        var text = BrowserPresentationHost.HostFailureMessage(
            HostError.Create(HostErrorCode.SessionClosed, "The browser session is closed."));

        Assert.StartsWith("This browser panel closed.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("session", text, StringComparison.OrdinalIgnoreCase);
    }
}
