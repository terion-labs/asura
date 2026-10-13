using Asura.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Asura.App.Tests;

/// <summary>
/// Avalonia.Native drops a window to its next fallback level when it is
/// handed the hint it already has, so the guard must not hand it again.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class WindowTransparencyHintHeadlessTests
{
    [Fact]
    public async Task A_hint_the_window_already_has_is_not_requested_again()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var window = new Window();
            try
            {
                window.Show();
                var requests = 0;
                window.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TopLevel.TransparencyLevelHintProperty)
                    {
                        requests++;
                    }
                };

                // The headless platform reports None whatever it is asked,
                // so None is the hint it can be seen to satisfy.
                WindowTransparencyHint.Request(window, [WindowTransparencyLevel.None]);
                Assert.Equal(1, requests);
                Assert.Equal(WindowTransparencyLevel.None, window.ActualTransparencyLevel);

                // An equal list, as a window builds afresh each time its
                // appearance is republished.
                WindowTransparencyHint.Request(window, [WindowTransparencyLevel.None]);
                Assert.Equal(1, requests);

                WindowTransparencyHint.Request(window, [WindowTransparencyLevel.Transparent]);
                Assert.Equal(2, requests);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }
}
