using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class MarkdownChatSecretsHeadlessTests
{
    [Theory]
    [InlineData("prose")]
    [InlineData("code")]
    [InlineData("link")]
    [InlineData("large")]
    public async Task SpoilersRevealLiteralTextOnlyAfterClickAndHideAgain(string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(AgentAttachmentHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var reference = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
            var runtime = new RevealRuntime();
            var markdown = kind switch
            {
                "code" => "```sh\n" + reference.Placeholder + "\n```",
                "link" => "[connection](" + reference.Placeholder + ")",
                "large" => "```text\n" + reference.Placeholder + "\n" + new string('a', 20 * 1024) + "\n```",
                _ => "Before " + reference.Placeholder + " after",
            };
            var preview = new MarkdownPreviewView
            {
                Text = markdown,
                ContinuousSelection = true,
                HiddenReferences = [reference],
                SecretRuntime = runtime
            };
            var host = new Border { Child = preview };
            var window = new Window { Content = host, Width = 640, Height = 400 };
            try
            {
                window.Show();
                await SettleAsync(preview, timeout.Token);
                var button = Assert.Single(preview.GetVisualDescendants().OfType<Button>(), control =>
                    AutomationProperties.GetName(control)?.StartsWith("Reveal hidden content", StringComparison.Ordinal) == true);
                Assert.Equal(0, runtime.RevealCount);
                Assert.DoesNotContain(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == RevealRuntime.Original);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                Assert.Equal(1, runtime.RevealCount);
                Assert.Equal(RevealRuntime.Original, Assert.IsType<TextBlock>(button.Content).Text, StringComparer.Ordinal);
                Assert.Equal("Hide revealed content", AutomationProperties.GetName(button), StringComparer.Ordinal);
                Assert.DoesNotContain(RevealRuntime.Original, preview.Text, StringComparison.Ordinal);
                host.IsVisible = false;
                Assert.IsType<string>(button.Content);
                Assert.Null(button.ContextMenu);
                host.IsVisible = true;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                Assert.Equal(RevealRuntime.Original, Assert.IsType<TextBlock>(button.Content).Text, StringComparer.Ordinal);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsType<string>(button.Content);
                Assert.Null(button.ContextMenu);
                window.Content = null;
                Assert.IsType<string>(button.Content);
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token);
    }

    [Fact]
    public async Task DelayedRevealCannotPopulateDetachedWorkspace()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(AgentAttachmentHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var reference = new ChatHiddenReference(Guid.NewGuid().ToString("N"));
            var runtime = new RevealRuntime { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var preview = new MarkdownPreviewView { Text = reference.Placeholder, HiddenReferences = [reference], SecretRuntime = runtime };
            var window = new Window { Content = preview, Width = 640, Height = 400 };
            try
            {
                window.Show();
                await SettleAsync(preview, timeout.Token);
                var button = Assert.Single(preview.GetVisualDescendants().OfType<Button>());
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Content = null;
                runtime.Release.TrySetResult();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                Assert.IsType<string>(button.Content);
                Assert.Null(button.ContextMenu);
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token);
    }

    private static async Task SettleAsync(MarkdownPreviewView preview, CancellationToken cancellationToken)
    {
        while (!preview.IsPresentationReady)
        {
            cancellationToken.ThrowIfCancellationRequested();
            preview.UpdateLayout();
            await Task.Delay(10, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
        }
        preview.UpdateLayout();
    }

    private sealed class RevealRuntime : IAgentChatSecretRuntime
    {
        public const string Original = "password=**literal** [not a link](https://example.test)";
        public int RevealCount { get; private set; }
        public TaskCompletionSource? Release { get; init; }
        public ProtectedChatText ProtectDraft(string text) => new(text, []);
        public async ValueTask<SecretVaultResult<string>> RevealChatSecretAsync(ChatHiddenReference reference, CancellationToken cancellationToken)
        {
            RevealCount++;
            if (Release is { } release)
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            return SecretVaultResult<string>.Succeed(Original);
        }
    }
}
