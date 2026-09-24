using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class ErrorNoticeTests
{
    [Fact]
    public void EmptyUpdatesAndRepeatedFailuresDoNotRemoveOrDuplicateNotices()
    {
        var notices = new ErrorNoticeCollection();
        notices.Report("First failure", "Operation failed");
        notices.Report(null);
        notices.Report(" ");
        notices.Report("First failure", "Operation failed");
        notices.Report("Second failure", "Operation failed");

        Assert.Equal(2, notices.Items.Count);
        notices.Items[0].Dismiss();
        Assert.Equal("Second failure", Assert.Single(notices.Items).Message);
        Assert.True(notices.HasErrors);
        notices.Items[0].Dismiss();
        Assert.False(notices.HasErrors);
        notices.Report("First failure", "Operation failed");
        Assert.Single(notices.Items);
    }

    [Fact]
    public async Task SharedPanelShowsRetainedErrorsAndDismissesOnlyTheChosenMessage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            await session.Dispatch(async () =>
            {
                var owner = new NoticeOwner();
                var panel = new PanelChrome { DataContext = owner, Content = new TextBlock { Text = "Connected" } };
                var window = new Window { Content = panel, Width = 700, Height = 400 };
                try
                {
                    window.Show();
                    owner.ErrorNotices.Report("Connection failed");
                    owner.ErrorNotices.Report("Push failed");
                    window.UpdateLayout();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    var view = Assert.Single(panel.GetVisualDescendants().OfType<ErrorNoticesView>());
                    Assert.True(view.IsVisible);
                    var dismiss = view.GetVisualDescendants().OfType<Button>()
                        .Where(button => AutomationProperties.GetName(button) == "Dismiss error").ToArray();
                    Assert.Equal(2, dismiss.Length);
                    dismiss[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal("Push failed", Assert.Single(owner.ErrorNotices.Items).Message);

                    // Recreating a panel view must not acknowledge outstanding failures.
                    window.Content = new PanelChrome { DataContext = owner };
                    window.UpdateLayout();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    var remaining = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                        button => AutomationProperties.GetName(button) == "Dismiss error");
                    remaining.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(owner.ErrorNotices.HasErrors);
                    Assert.False(Assert.Single(window.GetVisualDescendants().OfType<ErrorNoticesView>()).IsVisible);
                }
                finally
                {
                    window.Close();
                }
                return true;
            }, timeout.Token);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private sealed class NoticeOwner : ObservableObject;
}
