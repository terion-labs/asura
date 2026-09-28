using System.Buffers.Binary;
using Asura.Core;

namespace Asura.Application;

/// <summary>A bounded viewport image and the exact coordinate frame observed during capture.</summary>
public sealed class BrowserScreenshot
{
    public BrowserScreenshot(AgentImageAttachment image, BrowserAutomationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(image.MediaType, "image/png", StringComparison.Ordinal) || image.Content.Length < 24
            || !image.Content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new ArgumentException("A screenshot must contain a PNG header.", nameof(image));
        }
        PixelWidth = BinaryPrimitives.ReadInt32BigEndian(image.Content.Slice(16, 4));
        PixelHeight = BinaryPrimitives.ReadInt32BigEndian(image.Content.Slice(20, 4));
        if (PixelWidth is <= 0 or > 16384 || PixelHeight is <= 0 or > 16384
            || (long)PixelWidth * PixelHeight > 64 * 1024 * 1024)
        {
            throw new ArgumentException("The screenshot dimensions exceed their bounds.", nameof(image));
        }
        Image = image;
        Binding = binding;
    }

    public AgentImageAttachment Image { get; }
    public BrowserAutomationBinding Binding { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
}
