using System.Runtime.InteropServices;
using Asura.Application;
using Asura.Application.Previews;
using Avalonia.Media.Imaging;

namespace Asura.App.Views;

/// <summary>
/// File icons from <c>NSWorkspace</c>, rendered once per kind and kept.
///
/// A local folder is asked for by path, so a bundle or a folder with a custom
/// icon shows that icon, and is remembered by that path. Every file, local or
/// remote, is asked for by the content type its extension names and is
/// remembered by that extension: the icon then depends only on what the cache
/// is keyed by, so the first "LICENSE" or ".json" in a listing cannot decide
/// how every later one looks.
/// </summary>
internal static class MacOsFileTypeIcons
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string AppKitLibrary = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const nuint PngFileType = 4;
    private const int CursorPointSize = 16;

    /// <summary>
    /// Folders are remembered by path, and there can be many; past this the
    /// cache starts again rather than growing with every folder visited.
    /// </summary>
    private const int MaximumCachedIcons = 512;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string?> KindCache = new(StringComparer.Ordinal);

    /// <summary>
    /// AppKit, loaded before its classes are looked up. The app's windowing
    /// layer has it loaded already; a process without windows does not, and
    /// would otherwise find no <c>NSWorkspace</c> to ask.
    /// </summary>
    private static readonly bool AppKitLoaded = NativeLibrary.TryLoad(AppKitLibrary, out _);

    public static Bitmap? For(FilePanelEntry entry, string? localPath)
    {
        var key = CacheKey(entry, localPath);
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
    /// What an icon is remembered by. It names exactly what the icon was asked
    /// for with: a local folder's path, or the content type of anything else.
    /// </summary>
    internal static string CacheKey(FilePanelEntry entry, string? localPath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (localPath, entry.Kind == FilePanelEntryKind.Directory) switch
        {
            ({ } path, true) => "path:" + path,
            (_, true) => "folder",
            _ => "type:" + PreviewText.Extension(entry.Name),
        };
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
            return RenderPng(localPath is not null && isDirectory
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

    /// <summary>
    /// What the system calls a kind of file — "Markdown document", "ZIP
    /// archive" — from the content type its extension names, the same words
    /// Finder's Kind column shows. Null when there is no extension, when the
    /// system has no type for it, or off macOS. Remembered per extension.
    /// </summary>
    internal static string? KindDescription(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (extension.Length == 0 || !AppKitLoaded)
        {
            return null;
        }

        if (KindCache.TryGetValue(extension, out var cached))
        {
            return cached;
        }

        string? description = null;
        var pool = objc_autoreleasePoolPush();
        try
        {
            var typeClass = objc_getClass("UTType");
            var type = typeClass == 0
                ? 0
                : SendIdArgument(typeClass, Selector("typeWithFilenameExtension:"), NewString(extension));
            // A dynamic type is the system saying it has never seen the
            // extension; its description would only be the extension again.
            var isDeclared = type != 0 && SendBool(type, Selector("isDeclared"));
            var text = isDeclared ? SendId(type, Selector("localizedDescription")) : 0;
            var utf8 = text == 0 ? 0 : SendId(text, Selector("UTF8String"));
            description = utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException)
        {
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }

        if (KindCache.Count >= MaximumCachedIcons)
        {
            KindCache.Clear();
        }

        description = string.IsNullOrWhiteSpace(description) ? null : Capitalize(description);
        KindCache[extension] = description;
        return description;
    }

    /// <summary>Finder capitalizes the kind's first letter; the type's description may not.</summary>
    private static string Capitalize(string value) =>
        char.IsLower(value[0])
            ? string.Concat(
                char.ToUpper(value[0], System.Globalization.CultureInfo.CurrentCulture).ToString(),
                value.AsSpan(1))
            : value;

    /// <summary>
    /// The system's closed-hand cursor as PNG bytes with its hot spot, for a
    /// view being dragged around. AppKit has the cursor; Avalonia names only
    /// the open hand, so the grabbing one is handed over as a picture. Null
    /// off macOS or where AppKit will not give it.
    /// </summary>
    internal static (byte[] Png, int HotX, int HotY)? ClosedHandCursor()
    {
        if (!AppKitLoaded)
        {
            return null;
        }

        var pool = objc_autoreleasePoolPush();
        try
        {
            var cursor = SendId(objc_getClass("NSCursor"), Selector("closedHandCursor"));
            if (cursor == 0)
            {
                return null;
            }

            var hotSpot = SendPoint(cursor, Selector("hotSpot"));
            // A cursor is sixteen points square, and a custom cursor's image
            // is taken a pixel to a point.
            var png = RenderPng(SendId(cursor, Selector("image")), CursorPointSize);
            return png is null
                ? null
                : (png,
                    (int)Math.Clamp(Math.Round(hotSpot.X), 0, CursorPointSize - 1),
                    (int)Math.Clamp(Math.Round(hotSpot.Y), 0, CursorPointSize - 1));
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
    private static byte[]? RenderPng(nint image, int pixelSize = FileTypeIcons.PixelSize)
    {
        if (image == 0)
        {
            return null;
        }

        nint representation = 0;
        try
        {
            var rect = new CGRect(0, 0, pixelSize, pixelSize);
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
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;
    }

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
    private static extern CGPoint SendPoint(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(nint receiver, nint selector);

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
