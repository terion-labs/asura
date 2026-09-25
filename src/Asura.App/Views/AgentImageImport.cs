using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Platform.Storage;

namespace Asura.App.Views;

internal static class AgentImageImport
{
    public static async Task PickAsync(
        IStorageProvider storage,
        AgentChatViewModel agent,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Attach images",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
                        MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"],
                        AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "com.compuserve.gif", "org.webmproject.webp"],
                    },
                ],
            });
            if (files.Count > 0)
            {
                await AddFilesAsync(agent, files, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            agent.AttachmentError = exception.Message;
        }
    }

    public static async Task AddFilesAsync(
        AgentChatViewModel agent,
        IEnumerable<IStorageItem> files,
        CancellationToken cancellationToken)
    {
        RequireAvailable(agent);
        var images = new List<AgentImageAttachment>();
        foreach (var item in files)
        {
            if (agent.PendingImages.Count + images.Count >= AgentImageAttachment.MaximumPerMessage)
            {
                throw new InvalidOperationException("At most four images can be attached to one prompt.");
            }
            if (item is not IStorageFile file)
            {
                throw new InvalidOperationException("Choose image files, not folders.");
            }
            await using var stream = await file.OpenReadAsync();
            var bytes = await ReadBoundedImageAsync(stream, cancellationToken);
            images.Add(new AgentImageAttachment(file.Name, DetectImageMediaType(bytes), bytes));
        }
        RequireAvailable(agent);
        agent.AddPendingImages(images);
    }

    public static void RequireAvailable(AgentChatViewModel agent)
    {
        if (!agent.CanAttachImages)
        {
            throw new InvalidOperationException(
                agent.SelectedProvider?.SupportsImageInput != true
                    ? "The selected provider does not support images. Choose an image-capable provider."
                    : agent.IsBusy
                        ? "Wait for the current run to finish or stop it before attaching images."
                        : "At most four images can be attached to one prompt.");
        }
    }

    private static async Task<byte[]> ReadBoundedImageAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > AgentImageAttachment.MaximumBytes)
            {
                throw new InvalidOperationException(
                    "An attached image cannot exceed 4 MiB.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new InvalidOperationException("The selected image is empty.");
        }

        return buffer.ToArray();
    }

    private static string DetectImageMediaType(ReadOnlySpan<byte> content)
    {
        foreach (var mediaType in new[]
                 {
                     "image/png",
                     "image/jpeg",
                     "image/gif",
                     "image/webp",
                 })
        {
            try
            {
                _ = new AgentImageAttachment("image", mediaType, content);
                return mediaType;
            }
            catch (ArgumentException)
            {
            }
        }

        throw new InvalidOperationException(
            "The selected file is not a supported PNG, JPEG, GIF, or WebP image.");
    }

}
