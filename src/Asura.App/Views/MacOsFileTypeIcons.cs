using System.Runtime.InteropServices;
using Asura.Application;
using Asura.Application.Previews;
using Avalonia.Media.Imaging;

namespace Asura.App.Views;

/// <summary>
/// File icons from <c>NSWorkspace</c>, rendered once per kind and kept.
///
/// A local file is asked for by path, so a bundle or a folder with a custom
/// icon shows that icon; files are then remembered by extension, since asking
/// the disk again for every ".json" in a long listing buys nothing. A remote
/// item has no path on this machine and is asked for by its content type.
/// </summary>
internal static class MacOsFileTypeIcons
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string AppKitLibrary = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const nuint PngFileType = 4;

    /// <summary>
    /// Folders are remembered by path, and there can be many; past this the
    /// cache starts again rather than growing with every folder visited.
    /// </summary>
    private const int MaximumCachedIcons = 512;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// AppKit, loaded before its classes are looked up. The app's windowing
    /// layer has it loaded already; a process without windows does not, and
    /// would otherwise find no <c>NSWorkspace</c> to ask.
    /// </summary>
    private static readonly bool AppKitLoaded = NativeLibrary.TryLoad(AppKitLibrary, out _);

    public static Bitmap? For(FilePanelEntry entry, string? localPath)
    {
        var isDirectory = entry.Kind == FilePanelEntryKind.Directory;
        var extension = PreviewText.Extension(entry.Name);
        var key = (localPath, isDirectory) switch
        {
            ({ } path, true) => "path:" + path,
            (_, true) => "folder",
            _ => "type:" + extension,
        };
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Bitmap? icon = null;
        if (Png(entry, localPath) is { } png)
        {
            try
            {
                using var stream = new MemoryStream(png, writable: false);
                icon = new Bitmap(stream);
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidOperationException)
            {
                // An image the decoder will not take leaves the glyph in place.
            }
        }

        if (Cache.Count >= MaximumCachedIcons)
        {
            Cache.Clear();
        }

        Cache[key] = icon;
        return icon;
    }

    /// <summary>
    /// The icon as PNG bytes, or null where the system has none to give.
    /// Uncached: <see cref="For"/> keeps what it decodes.
    /// </summary>
    internal static byte[]? Png(FilePanelEntry entry, string? localPath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!AppKitLoaded)
        {
            return null;
        }

        var isDirectory = entry.Kind == FilePanelEntryKind.Directory;
        var pool = objc_autoreleasePoolPush();
        try
        {
            return RenderPng(localPath is not null
                ? IconForFile(localPath)
                : IconForType(isDirectory ? null : PreviewText.Extension(entry.Name)));
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static nint Workspace() =>
        SendId(objc_getClass("NSWorkspace"), Selector("sharedWorkspace"));

    private static nint IconForFile(string path) =>
        SendIdArgument(Workspace(), Selector("iconForFile:"), NewString(path));

    /// <summary>
    /// The icon for a kind of file: the type an extension names, or a folder
    /// when there is no extension to name because the item is one.
    /// </summary>
    private static nint IconForType(string? extension)
    {
        var typeClass = objc_getClass("UTType");
        if (typeClass == 0)
        {
            return 0;
        }

        var type = extension is null
            ? SendIdArgument(typeClass, Selector("typeWithIdentifier:"), NewString("public.folder"))
            : string.IsNullOrEmpty(extension)
                ? SendIdArgument(typeClass, Selector("typeWithIdentifier:"), NewString("public.data"))
                : SendIdArgument(typeClass, Selector("typeWithFilenameExtension:"), NewString(extension));
        return type == 0
            ? 0
            : SendIdArgument(Workspace(), Selector("iconForContentType:"), type);
    }

    /// <summary>
    /// The image drawn at <see cref="FileTypeIcons.PixelSize"/> pixels and
    /// handed over as PNG: the image picks its best representation for that
    /// size, so the result is the crisp rendition, not a scaled thumbnail.
    /// </summary>
    private static byte[]? RenderPng(nint image)
    {
        if (image == 0)
        {
            return null;
        }

        nint representation = 0;
        try
        {
            var rect = new CGRect(0, 0, FileTypeIcons.PixelSize, FileTypeIcons.PixelSize);
            var cgImage = SendCGImage(
                image,
                Selector("CGImageForProposedRect:context:hints:"),
                ref rect,
                0,
                0);
            if (cgImage == 0)
            {
                return null;
            }

            representation = SendIdArgument(
                SendId(objc_getClass("NSBitmapImageRep"), Selector("alloc")),
                Selector("initWithCGImage:"),
                cgImage);
            var properties = SendId(objc_getClass("NSDictionary"), Selector("dictionary"));
            var png = representation == 0
                ? 0
                : SendIdTypeProperties(
                    representation,
                    Selector("representationUsingType:properties:"),
                    PngFileType,
                    properties);
            if (png == 0)
            {
                return null;
            }

            var length = SendNUInt(png, Selector("length"));
            var bytes = SendId(png, Selector("bytes"));
            if (bytes == 0 || length == 0 || length > int.MaxValue)
            {
                return null;
            }

            var buffer = new byte[(int)length];
            Marshal.Copy(bytes, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            if (representation != 0)
            {
                SendVoid(representation, Selector("release"));
            }
        }
    }

    private static nint NewString(string value) =>
        SendIdUtf8(objc_getClass("NSString"), Selector("stringWithUTF8String:"), value);

    private static nint Selector(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect(double x, double y, double width, double height)
    {
        public readonly double X = x;
        public readonly double Y = y;
        public readonly double Width = width;
        public readonly double Height = height;
    }

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_autoreleasePoolPush")]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_autoreleasePoolPop")]
    private static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendId(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint SendNUInt(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdArgument(nint receiver, nint selector, nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdUtf8(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendCGImage(
        nint receiver,
        nint selector,
        ref CGRect proposedRect,
        nint context,
        nint hints);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdTypeProperties(
        nint receiver,
        nint selector,
        nuint type,
        nint properties);
}
