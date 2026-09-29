using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Asura.App.Views;

internal static class AgentAttachmentImport
{
    public static async Task PickAsync(
        Func<FilePickerOpenOptions, Task<IReadOnlyList<IStorageFile>>> pickFiles,
        AgentChatViewModel agent,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await pickFiles(new FilePickerOpenOptions
            {
                Title = "Attach files",
                AllowMultiple = true,
                FileTypeFilter = [FilePickerFileTypes.All],
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
        if (!agent.CanAttachFiles) { throw new InvalidOperationException("Wait for the current run to finish before attaching files."); }
        var images = new List<AgentImageAttachment>();
        var attachments = new List<AgentFileAttachment>();
        foreach (var item in files)
        {
            if (agent.PendingImages.Count + agent.PendingFiles.Count + images.Count + attachments.Count >= AgentFileAttachment.MaximumPerMessage)
            {
                throw new InvalidOperationException("At most eight attachments can be added to one prompt.");
            }
            if (item is not IStorageFile file) { throw new InvalidOperationException("Choose files, not folders."); }
            await using var stream = await file.OpenReadAsync();
            var bytes = await ReadBoundedFileAsync(stream, cancellationToken);
            var mediaType = DetectImageMediaType(bytes);
            if (mediaType is not null && agent.SelectedProvider?.SupportsImageInput == true)
            {
                images.Add(new AgentImageAttachment(file.Name, mediaType, bytes));
            }
            else
            {
                attachments.Add(await agent.ImportFileAsync(file.Name, bytes, cancellationToken));
            }
        }
        if (!agent.CanAttachFiles) { throw new InvalidOperationException("The composer changed while importing files. Attach them again."); }
        // Validate both batches before changing the draft so a failed mixed selection does not partially attach.
        _ = AgentFileAttachment.CopyBatch(agent.PendingFiles.Concat(attachments));
        if (agent.PendingFiles.Count + agent.PendingImages.Count + attachments.Count + images.Count > AgentFileAttachment.MaximumPerMessage)
        {
            throw new InvalidOperationException("At most eight attachments can be added to one prompt.");
        }
        if (agent.PendingImages.Count + images.Count > AgentImageAttachment.MaximumPerMessage
            || agent.PendingImages.Sum(image => (long)image.Content.Length) + images.Sum(image => (long)image.Content.Length)
                > AgentImageAttachment.MaximumTotalBytesPerMessage)
        {
            throw new InvalidOperationException("At most four images totalling 8 MiB can be attached to one prompt.");
        }
        if (images.Count > 0) { agent.AddPendingImages(images); }
        if (attachments.Count > 0) { agent.AddPendingFiles(attachments); }
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

    internal static AgentImageAttachment EncodePastedImage(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var size = bitmap.PixelSize;
        // Match the ordinary image preview budget so normal high-resolution
        // screenshots remain usable without permitting unbounded PNG work.
        if (size.Width <= 0 || size.Height <= 0
            || (long)size.Width * size.Height > OrdinaryImagePreviewDecoder.MaximumSourcePixels)
        {
            throw new InvalidOperationException("The pasted image is too large. Resize it before pasting again.");
        }

        using var output = new PastedImageBuffer();
        bitmap.Save(output);
        if (output.ExceededLimit)
        {
            throw new InvalidOperationException("The pasted image exceeds 4 MiB. Resize it or attach it as a file.");
        }
        return new AgentImageAttachment("Pasted image.png", "image/png",
            output.GetBuffer().AsSpan(0, checked((int)output.Length)));
    }

    // The native encoder can continue writing after the byte budget is reached.
    // Discard its remaining output and report the limit after returning from
    // native code, rather than throwing through a native stream callback.
    internal sealed class PastedImageBuffer : MemoryStream
    {
        public bool ExceededLimit { get; private set; }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (ExceededLimit || Position + buffer.Length > AgentImageAttachment.MaximumBytes)
            {
                ExceededLimit = true;
                return;
            }
            var end = checked((int)(Position + buffer.Length));
            EnsureOutputCapacity(end);
            if (end > Length) { base.SetLength(end); }
            // MemoryStream's span overload routes derived streams through the
            // virtual byte-array overload. Copy directly to avoid that recursion.
            buffer.CopyTo(GetBuffer().AsSpan(checked((int)Position), buffer.Length));
            Position = end;
        }

        public override void WriteByte(byte value)
        {
            Span<byte> buffer = [value];
            Write(buffer);
        }

        public override void SetLength(long value)
        {
            if (value > AgentImageAttachment.MaximumBytes)
            {
                ExceededLimit = true;
                return;
            }
            if (value >= 0) { EnsureOutputCapacity(checked((int)value)); }
            base.SetLength(value);
        }

        private void EnsureOutputCapacity(int required)
        {
            if (required > Capacity)
            {
                Capacity = Math.Min(AgentImageAttachment.MaximumBytes,
                    Math.Max(required, Math.Max(256, Capacity * 2)));
            }
        }
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
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

            if (buffer.Length + read > AgentFileAttachment.MaximumBytes)
            {
                throw new InvalidOperationException(
                    "An attachment cannot exceed 50 MiB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string? DetectImageMediaType(ReadOnlySpan<byte> content)
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

        return null;
    }

}
