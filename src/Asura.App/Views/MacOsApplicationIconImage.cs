using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Asura.App.Views;

/// <summary>
/// The icon macOS shows for the running app, which follows the bundle's asset
/// catalog, light or dark.
/// </summary>
internal static class MacOsApplicationIconImage
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nuint PngFileType = 4;

    private static readonly Uri BundledIconUri = new("avares://Asura.App/Assets/AppIcon.png");

    /// <summary>
    /// The running app's icon on macOS. An unbundled run has only the generic
    /// executable icon, which reads as a folder in About and in alerts, so it
    /// falls back to the bundled Asura artwork. Null on other hosts, whose
    /// alerts draw a glyph instead.
    /// </summary>
    public static Bitmap? Read()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        return HasBundleIcon() ? ReadSystemIcon() ?? Bundled() : Bundled();
    }

    /// <summary>The Asura artwork shipped in the app, on every host.</summary>
    public static Bitmap? Bundled()
    {
        try
        {
            using var stream = AssetLoader.Open(BundledIconUri);
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the main bundle names an icon. Without one, NSApp's icon is
    /// the system's generic one.
    /// </summary>
    private static bool HasBundleIcon()
    {
        var bundle = SendId(objc_getClass("NSBundle"), Selector("mainBundle"));
        return bundle != 0
            && (InfoValue(bundle, "CFBundleIconName") != 0 || InfoValue(bundle, "CFBundleIconFile") != 0);
    }

    private static nint InfoValue(nint bundle, string key)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(key);
        try
        {
            var name = SendIdArgument(objc_getClass("NSString"), Selector("stringWithUTF8String:"), utf8);
            return name == 0 ? 0 : SendIdArgument(bundle, Selector("objectForInfoDictionaryKey:"), name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static Bitmap? ReadSystemIcon()
    {
        var application = SendId(objc_getClass("NSApplication"), Selector("sharedApplication"));
        var image = SendId(application, Selector("applicationIconImage"));
        var tiff = image == 0 ? 0 : SendId(image, Selector("TIFFRepresentation"));
        var representation = tiff == 0
            ? 0
            : SendIdArgument(objc_getClass("NSBitmapImageRep"), Selector("imageRepWithData:"), tiff);
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
        using var stream = new MemoryStream(buffer, writable: false);
        return new Bitmap(stream);
    }

    private static nint Selector(string name) => sel_registerName(name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendId(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint SendNUInt(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdArgument(nint receiver, nint selector, nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdTypeProperties(
        nint receiver,
        nint selector,
        nuint type,
        nint properties);
}
