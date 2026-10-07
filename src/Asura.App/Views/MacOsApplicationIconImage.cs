using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace Asura.App.Views;

/// <summary>
/// The icon macOS shows for the running app, which follows the bundle's asset
/// catalog, light or dark.
/// </summary>
internal static class MacOsApplicationIconImage
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nuint PngFileType = 4;

    public static Bitmap? Read()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

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
