using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task Chat_images_preserve_aspect_ratio_and_open_a_zoomable_preview() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", 0);
            var landscape = ChatImage("landscape.png", 1200, 600);
            var portrait = ChatImage("portrait.png", 200, 400);
            using var runtime = new StubGovernedRuntime
            {
                Snapshot = Snapshot(providerId: provider.Id, messages:
                    [new(AgentChatMessageRole.User, "See these images", Images: [landscape, portrait])]),
            };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Content = view, Width = 700, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var previews = view.GetVisualDescendants().OfType<AgentChatImagePreview>().ToArray();
                Assert.Equal(2, previews.Length);
                foreach (var preview in previews)
                {
                    var thumbnail = preview.FindControl<Image>("Thumbnail")!;
                    await WaitUntilAsync(() => thumbnail.Source is not null);
                    window.UpdateLayout();
                    var source = Assert.IsAssignableFrom<Bitmap>(thumbnail.Source);
                    var original = ReferenceEquals(preview.Attachment, landscape) ? new Avalonia.Size(1200, 600) : new Avalonia.Size(200, 400);
                    Assert.InRange(source.PixelSize.Width, 1, 540);
                    Assert.InRange(thumbnail.Bounds.Width, 1, 270);
                    Assert.InRange(thumbnail.Bounds.Height, 1, 160);
                    Assert.Equal(original.Width / original.Height, thumbnail.Bounds.Width / thumbnail.Bounds.Height, precision: 2);
                }
                var first = previews[0];
                var open = first.FindControl<Button>("OpenPreview")!;
                var center = open.TranslatePoint(new Avalonia.Point(open.Bounds.Width / 2, open.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                var popup = first.FindControl<Avalonia.Controls.Primitives.Popup>("PreviewPopup")!;
                Assert.True(popup.IsOpen);
                var enlarged = first.FindControl<ZoomableImageView>("EnlargedImage")!;
                await WaitUntilAsync(() => enlarged.Source is not null);
                Assert.Equal(1200, enlarged.Source!.PixelSize.Width);
                var scale = enlarged.Scale;
                enlarged.ZoomIn();
                Assert.True(enlarged.Scale > scale);
                first.FindControl<Button>("ClosePreview")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(popup.IsOpen);
                Assert.Null(enlarged.Source);
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(popup.IsOpen);
                await WaitUntilAsync(() => enlarged.Source is not null);
                view.IsVisible = false;
                Assert.Null(enlarged.Source);
                Assert.Null(first.FindControl<Image>("Thumbnail")!.Source);
                view.IsVisible = true;
                window.UpdateLayout();
                await WaitUntilAsync(() => first.FindControl<Image>("Thumbnail")!.Source is not null);
                window.Content = null;
                Assert.All(previews, preview => Assert.Null(preview.FindControl<Image>("Thumbnail")!.Source));
            }
            finally { window.Close(); }
        }, typeof(AgentAttachmentHeadlessApplication));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Unavailable_or_invalid_chat_images_have_a_readable_fallback(bool invalid) =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var attachment = invalid ? new AgentImageAttachment("broken.png", "image/png",
                [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]) : null;
            var preview = new AgentChatImagePreview { Attachment = new("broken.png", "image/png", 8, attachment) };
            var window = new Window { Content = preview, Width = 400, Height = 400 };
            try
            {
                window.Show();
                window.UpdateLayout();
                await WaitUntilAsync(() => preview.FindControl<TextBlock>("Fallback")!.Text?.Contains("unavailable", StringComparison.Ordinal) == true);
                Assert.False(preview.FindControl<Button>("OpenPreview")!.IsEnabled);
                Assert.Null(preview.FindControl<Image>("Thumbnail")!.Source);
            }
            finally { window.Close(); }
        }, typeof(AgentAttachmentHeadlessApplication));

    [Fact]
    public void Chat_preview_payload_is_omitted_from_serialized_presentation_metadata()
    {
        var image = ChatImage("image.png", 20, 10);
        var json = System.Text.Json.JsonSerializer.Serialize(image);
        Assert.DoesNotContain("Attachment", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(image.Attachment!.Content), json, StringComparison.Ordinal);
    }

    private static AgentChatImage ChatImage(string name, int width, int height)
    {
        using var source = new SKBitmap(width, height);
        source.Erase(SKColors.CornflowerBlue);
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        var attachment = new AgentImageAttachment(name, "image/png", encoded.ToArray());
        return new(name, "image/png", attachment.Content.Length, attachment);
    }
}
