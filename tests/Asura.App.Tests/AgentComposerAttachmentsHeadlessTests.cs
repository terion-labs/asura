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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_draft_is_restored_only_when_the_failed_send_did_not_commit(bool committed)
    {
        var provider = Provider("provider", "Provider", 0);
        using var runtime = new StubGovernedRuntime
        {
            SendResult = new(false, "agent_provider_failed", "Provider unavailable.", InitialPromptCommitted: committed),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance) { Prompt = "Read it" };
        var file = new AgentFileAttachment(Guid.NewGuid().ToString("N"), "notes.txt", 12);
        model.AddPendingFiles([file]);
        await model.SendAsync(Target(), Policy(provider), CancellationToken.None);
        if (committed) { Assert.Empty(model.PendingFiles); }
        else { Assert.Equal(file, Assert.Single(model.PendingFiles)); }
    }

    [Theory]
    [InlineData(false, "notes.txt")]
    [InlineData(true, "archive.zip")]
    [InlineData(true, "document.pdf")]
    [InlineData(false, "unknown.format")]
    public Task Composer_attaches_arbitrary_files_by_picker_import_and_clipboard(bool clipboard, string name) =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var provider = Provider("provider", "Provider", 0, supportsImageInput: false);
            using var runtime = new StubGovernedRuntime { Snapshot = Snapshot(providerId: provider.Id), SupportsFileAttachments = true };
            using var profiles = new StubProfileRuntime { Profiles = [provider] };
            using var model = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance);
            var view = new AgentWorkspaceView { DataContext = new AgentComposerHost(model) };
            var window = new Window { Width = 420, Height = 700, Content = view };
            var directory = Directory.CreateTempSubdirectory("asura-composer-files-");
            try
            {
                window.Show();
                var path = System.IO.Path.Combine(directory.FullName, name);
                byte[] bytes = [0, 255, 128, 23];
                await File.WriteAllBytesAsync(path, bytes);
                using var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
                Assert.NotNull(file);
                if (clipboard)
                {
                    await window.Clipboard!.SetFilesAsync([file]);
                    view.FindControl<TextBox>("AgentChatPromptInput")!.Paste();
                    await WaitUntilAsync(() => model.HasPendingFiles);
                }
                else
                {
                    FilePickerOpenOptions? options = null;
                    await AgentAttachmentImport.PickAsync(request =>
                    {
                        options = request;
                        return Task.FromResult<IReadOnlyList<IStorageFile>>([file]);
                    }, model, CancellationToken.None);
                    Assert.NotNull(options);
                    Assert.Equal("Attach files", options.Title);
                    Assert.True(options.AllowMultiple);
                    Assert.Same(FilePickerFileTypes.All, Assert.Single(options.FileTypeFilter!));
                }
                var attached = Assert.Single(model.PendingFiles);
                Assert.Equal(bytes, runtime.ImportedFiles[attached.Id]);
                Assert.True(model.CanSend);
                window.UpdateLayout();
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == name);
                var remove = view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Tag, attached));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(model.PendingFiles);
                await AgentAttachmentImport.AddFilesAsync(model, [file], CancellationToken.None);
                File.Delete(path);
                await model.SendAsync(Target(), Policy(provider), CancellationToken.None);
                Assert.Equal(name, Assert.Single(runtime.LastRequest!.Files).FileName);
                Assert.Empty(model.PendingFiles);
            }
            finally { window.Close(); directory.Delete(recursive: true); }
        });

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
                    await AgentAttachmentImport.AddFilesAsync(model, [file], CancellationToken.None);
                }
                window.UpdateLayout();
                Assert.True(Assert.IsType<ItemsControl>(view.FindControl<ItemsControl>("AgentPendingImages")).IsEffectivelyVisible);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == "test.png");
                Assert.True(model.CanSend);
                var remove = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Remove attached image test.png");
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(model.PendingImages);
                await AgentAttachmentImport.AddFilesAsync(model, [file], CancellationToken.None);
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
                await AgentAttachmentImport.AddFilesAsync(model, [valid], CancellationToken.None);
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
