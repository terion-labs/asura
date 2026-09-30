using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
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
                Assert.Equal("<secret>", Assert.IsType<TextBlock>(button.Content).Text);
                Assert.Equal(FontStyle.Italic, Assert.IsType<TextBlock>(button.Content).FontStyle);
                if (kind == "prose")
                {
                    // A short inline spoiler must fit in the same single line as
                    // the surrounding text, without growing its 20 DIP line box.
                    var paragraph = Assert.Single(preview.GetVisualDescendants().OfType<SelectableTextBlock>(), control => control.IsEffectivelyVisible);
                    Assert.InRange(paragraph.Bounds.Height, 1, 21);
                    var position = button.TranslatePoint(new Point(), paragraph);
                    Assert.NotNull(position);
                    Assert.InRange(position.Value.Y, 0, paragraph.Bounds.Height - button.Bounds.Height + 0.5);
                    var label = Assert.IsType<TextBlock>(button.Content);
                    Assert.Equal(paragraph.TextLayout.TextLines[0].Baseline,
                        position.Value.Y + label.TextLayout.TextLines[0].Baseline, precision: 1);
                }
                Assert.DoesNotContain(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == RevealRuntime.Original);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                Assert.Equal(1, runtime.RevealCount);
                Assert.Equal(RevealRuntime.Value, Assert.IsType<TextBlock>(button.Content).Text, StringComparer.Ordinal);
                Assert.Equal(FontStyle.Normal, Assert.IsType<TextBlock>(button.Content).FontStyle);
                Assert.Equal("Hide revealed content", AutomationProperties.GetName(button), StringComparer.Ordinal);
                Assert.DoesNotContain(RevealRuntime.Original, preview.Text, StringComparison.Ordinal);
                host.IsVisible = false;
                Assert.Equal("<secret>", Assert.IsType<TextBlock>(button.Content).Text);
                Assert.Equal(FontStyle.Italic, Assert.IsType<TextBlock>(button.Content).FontStyle);
                Assert.Null(button.ContextMenu);
                host.IsVisible = true;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, CancellationToken.None);
                Assert.Equal(RevealRuntime.Value, Assert.IsType<TextBlock>(button.Content).Text, StringComparer.Ordinal);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("<secret>", Assert.IsType<TextBlock>(button.Content).Text);
                Assert.Null(button.ContextMenu);
                window.Content = null;
                Assert.Equal("<secret>", Assert.IsType<TextBlock>(button.Content).Text);
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
                Assert.Equal("<secret>", Assert.IsType<TextBlock>(button.Content).Text);
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
        public const string Value = "**literal** [not a link](https://example.test)";
        public const string Original = "password=\"" + Value + "\"";
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
