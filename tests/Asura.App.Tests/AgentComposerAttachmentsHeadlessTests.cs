using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public Task Composer_pastes_a_bitmap_as_a_png_attachment() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", 0, supportsImageInput: true);
            using var runtime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Width = 420, Height = 700, Content = view };
            try
            {
                window.Show();
                using var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
                await window.Clipboard!.SetBitmapAsync(bitmap);
                Assert.IsType<TextBox>(view.FindControl<TextBox>("AgentChatPromptInput")).Paste();
                await WaitUntilAsync(() => model.HasPendingImages || model.HasAttachmentError);
                Assert.Empty(model.AttachmentError);
                var image = Assert.Single(model.PendingImages);
                Assert.Equal("image/png", image.MediaType);
                using var content = new MemoryStream(image.Content.ToArray());
                using var decoded = new Bitmap(content);
                Assert.Equal(new PixelSize(2, 2), decoded.PixelSize);
            }
            finally
            {
                window.Close();
            }
        }, typeof(AgentAttachmentHeadlessApplication));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Composer_image_file_import_is_visible_removable_and_sent(bool clipboard) =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", 0, supportsImageInput: true);
            using var runtime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Width = 420, Height = 700, Content = view };
            var directory = Directory.CreateTempSubdirectory("asura-composer-");
            try
            {
                window.Show();
                await File.WriteAllBytesAsync(System.IO.Path.Combine(directory.FullName, "test.png"), [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
                using var file = await window.StorageProvider.TryGetFileFromPathAsync(System.IO.Path.Combine(directory.FullName, "test.png"));
                Assert.NotNull(file);
                var prompt = Assert.IsType<TextBox>(view.FindControl<TextBox>("AgentChatPromptInput"));
                if (clipboard)
                {
                    await window.Clipboard!.SetFilesAsync([file]);
                    prompt.Paste();
                    await WaitUntilAsync(() => model.HasPendingImages);
                }
                else
                {
                    await AgentImageImport.AddFilesAsync(model, [file], CancellationToken.None);
                }
                window.UpdateLayout();
                Assert.True(Assert.IsType<ItemsControl>(view.FindControl<ItemsControl>("AgentPendingImages")).IsEffectivelyVisible);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == "test.png");
                Assert.True(model.CanSend);
                var remove = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Remove attached image test.png");
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(model.PendingImages);
                await AgentImageImport.AddFilesAsync(model, [file], CancellationToken.None);
                await model.SendAsync(Target(), Policy(provider), CancellationToken.None);
                Assert.Equal("test.png", Assert.Single(Assert.IsType<GovernedAgentPrompt>(runtime.LastRequest).Images).FileName);
                Assert.Empty(model.PendingImages);
            }
            finally
            {
                window.Close();
                directory.Delete(recursive: true);
            }
        });

    [Fact]
    public Task Composer_pastes_text_and_reports_invalid_files_without_losing_draft_images() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", 0, supportsImageInput: true);
            using var runtime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id) };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance) { Prompt = "replace me" };
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Width = 420, Height = 700, Content = view };
            var directory = Directory.CreateTempSubdirectory("asura-composer-");
            try
            {
                window.Show();
                var prompt = Assert.IsType<TextBox>(view.FindControl<TextBox>("AgentChatPromptInput"));
                prompt.SelectAll();
                await window.Clipboard!.SetTextAsync("pasted text\nsecond line");
                prompt.Paste();
                await WaitUntilAsync(() => model.Prompt == "pasted text\nsecond line");
                await File.WriteAllBytesAsync(System.IO.Path.Combine(directory.FullName, "keep.png"), [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
                using var valid = await window.StorageProvider.TryGetFileFromPathAsync(System.IO.Path.Combine(directory.FullName, "keep.png"));
                Assert.NotNull(valid);
                await AgentImageImport.AddFilesAsync(model, [valid], CancellationToken.None);
                await File.WriteAllBytesAsync(System.IO.Path.Combine(directory.FullName, "invalid.png"), [1, 2, 3]);
                using var invalid = await window.StorageProvider.TryGetFileFromPathAsync(System.IO.Path.Combine(directory.FullName, "invalid.png"));
                Assert.NotNull(invalid);
                await window.Clipboard!.SetFilesAsync([invalid]);
                prompt.Paste();
                await WaitUntilAsync(() => model.HasAttachmentError);
                Assert.Equal("keep.png", Assert.Single(model.PendingImages).FileName);
                window.UpdateLayout();
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == model.AttachmentError);
                Assert.Equal("pasted text\nsecond line", model.Prompt);
            }
            finally
            {
                window.Close();
                directory.Delete(recursive: true);
            }
        });

}

public static class AgentAttachmentHeadlessApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Asura.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
