using System.Text;

namespace Exclr8Cef.WebView.Tests;

public sealed class DevToolsMessageBoundsTests
{
    [Fact]
    public void Oversized_events_never_reach_host_subscribers()
    {
        var browser = new CefBrowser(42);
        var events = 0;
        browser.DevToolsMessage += (_, _) => events++;
        Cef.DispatchDevToolsMessage(browser, true, 0, new byte[256 * 1024 + 1]);
        Assert.Equal(0, events);
        Cef.DispatchDevToolsMessage(browser, true, 0, "{\"method\":\"DOM.documentUpdated\"}"u8);
        Assert.Equal(1, events);
    }

    [Fact]
    public void Oversized_replies_deliver_a_small_error_instead_of_the_page_content()
    {
        var browser = new CefBrowser(42);
        DevToolsMessageEventArgs? observed = null;
        browser.DevToolsMessage += (_, args) => observed = args;
        Cef.DispatchDevToolsMessage(browser, false, 7, new byte[8 * 1024 * 1024 + 1]);
        Assert.NotNull(observed);
        Assert.False(observed.IsEvent);
        Assert.Equal(7, observed.MessageId);
        Assert.Contains("error", observed.Json);
        Assert.True(Encoding.UTF8.GetByteCount(observed.Json) < 256);
    }

    [Fact]
    public async Task Missing_native_reply_cannot_leave_the_host_waiting_forever()
    {
        var browser = new CefBrowser(42);
        var never = new TaskCompletionSource<string>();
        var bounded = browser.AwaitDevToolsReplyAsync(7, never.Task);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            bounded.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(bounded.IsCompleted);
        Assert.False(never.Task.IsCompleted);
    }
}
