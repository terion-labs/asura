using System.Runtime.InteropServices;

namespace Asura.App.Views;

/// <summary>
/// The Trash as macOS keeps it, through <c>NSFileManager</c>: the item lands in
/// the Trash of its own volume, Finder's Put Back knows where it came from, and
/// the manager reports the item's new location so Undo can bring it back.
/// </summary>
internal sealed class MacOsFileTrash : IFileTrash
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public string Name => "Trash";

    public TrashedItem MoveToTrash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var pool = objc_autoreleasePoolPush();
        try
        {
            var manager = SendId(objc_getClass("NSFileManager"), Selector("defaultManager"));
            var url = SendIdString(
                objc_getClass("NSURL"),
                Selector("fileURLWithPath:"),
                NewString(path));
            nint resulting = 0;
            nint error = 0;
            var moved = SendTrash(
                manager,
                Selector("trashItemAtURL:resultingItemURL:error:"),
                url,
                ref resulting,
                ref error);
            if (!moved)
            {
                throw new IOException(Describe(error)
                    ?? $"The Trash refused “{Path.GetFileName(path)}”.");
            }

            var trashedPath = resulting == 0
                ? null
                : ReadString(SendId(resulting, Selector("path")));
            return new TrashedItem(path, trashedPath);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public void PutBack(TrashedItem item) => FileTrash.MoveBack(item);

    private static nint NewString(string value) =>
        SendIdUtf8(objc_getClass("NSString"), Selector("stringWithUTF8String:"), value);

    private static string? Describe(nint error) =>
        error == 0
            ? null
            : ReadString(SendId(error, Selector("localizedDescription")));

    private static string? ReadString(nint value)
    {
        if (value == 0)
        {
            return null;
        }

        var utf8 = SendId(value, Selector("UTF8String"));
        return utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
    }

    private static nint Selector(string name) => sel_registerName(name);

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
    private static extern nint SendIdString(nint receiver, nint selector, nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdUtf8(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendTrash(
        nint receiver,
        nint selector,
        nint url,
        ref nint resultingUrl,
        ref nint error);
}
