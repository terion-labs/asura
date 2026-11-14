namespace Asura.Browser.Tests;

public sealed class DownloadsAwaitingLocationTests
{
    [Fact]
    public void A_save_dialog_is_let_through_only_for_a_download_waiting_for_its_location()
    {
        var waiting = new DownloadsAwaitingLocation();
        Assert.False(waiting.TryTake());

        waiting.Add(1);
        waiting.Add(1);
        Assert.True(waiting.TryTake());
        Assert.False(waiting.TryTake());
    }

    [Fact]
    public void A_download_that_ends_first_no_longer_opens_a_save_dialog()
    {
        var waiting = new DownloadsAwaitingLocation();
        waiting.Add(1);
        waiting.Add(2);

        waiting.Forget(1);

        Assert.True(waiting.TryTake());
        Assert.False(waiting.TryTake());
    }
}
