using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media;

namespace Asura.App.Views;

/// <summary>
/// Reads AppKit's semantic colours rather than copying their values by hand.
///
/// The palette in <see cref="EffectiveAppearanceResourceMapper"/> keeps literal
/// fallbacks, but on macOS the window background, label, separator, control and
/// selection colours come from <c>NSColor</c>, resolved once for Aqua and once
/// for Dark Aqua so an explicit Light or Dark setting still gets the matching
/// set. They are read again whenever AppKit posts
/// <c>NSSystemColorsDidChangeNotification</c>, which covers accent, highlight
/// and contrast changes made in System Settings.
///
/// The same pass records whether the accent is Multicolor. AppKit has no API
/// for that: Multicolor is the absence of the <c>AppleAccentColor</c> default,
/// and Avalonia reports it as plain blue.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsSystemPaletteSource : IHostSystemPaletteSource
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string AppKitLibrary =
        "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string ObserverClassName = "AsuraSystemColorsObserver";
    private const string ObserverSelector = "asuraSystemColorsChanged:";
    private const string NotificationSymbol = "NSSystemColorsDidChangeNotification";

    private static readonly object NativeClassGate = new();
    private static readonly ConcurrentDictionary<nint, WeakReference<MacOsSystemPaletteSource>>
        Sources = new();
    private static readonly NotificationCallback Callback = OnNativeNotification;
    private static nint _observerClass;
    private static nint _appKitHandle;

    private nint _notificationCenter;
    private nint _observer;
    private bool _stopped;

    public HostSystemPalette Current { get; private set; } = HostSystemPalette.None;

    public event EventHandler? Changed;

    public void Start()
    {
        if (_stopped || _observer != 0)
        {
            return;
        }

        try
        {
            EnsureAppKitLoaded();
            _notificationCenter = SendId(
                objc_getClass("NSNotificationCenter"),
                Selector("defaultCenter"));
            _observer = SendId(GetObserverClass(), Selector("new"));
            if (_notificationCenter == 0 || _observer == 0)
            {
                DisposeNativeObserver();
                return;
            }

            Sources[_observer] = new WeakReference<MacOsSystemPaletteSource>(this);
            SendVoid(
                _notificationCenter,
                Selector("addObserver:selector:name:object:"),
                _observer,
                Selector(ObserverSelector),
                ReadNotificationName(),
                0);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            DisposeNativeObserver();
        }

        Refresh();
    }

    /// <summary>
    /// Reads the palette again and says so when it moved. Also called when
    /// Avalonia reports a colour change, in case the notification and the
    /// platform settings arrive in either order.
    /// </summary>
    public void Refresh()
    {
        if (_stopped)
        {
            return;
        }

        HostSystemPalette next;
        try
        {
            EnsureAppKitLoaded();
            next = new HostSystemPalette(
                Resolve("NSAppearanceNameAqua"),
                Resolve("NSAppearanceNameDarkAqua"),
                AccentIsMulticolor());
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            next = HostSystemPalette.None;
        }

        if (next == Current)
        {
            return;
        }

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        DisposeNativeObserver();
    }

    private static HostSemanticColors? Resolve(string appearanceName)
    {
        var appearanceClass = objc_getClass("NSAppearance");
        var appearance = SendIdArgument(
            appearanceClass,
            Selector("appearanceNamed:"),
            NSString(appearanceName));
        if (appearance == 0)
        {
            return null;
        }

        // A dynamic NSColor resolves against the current appearance. The
        // block-based performAsCurrentDrawingAppearance: is not reachable
        // through plain objc_msgSend, so the class-level current appearance is
        // swapped for the duration of the reads and put back afterwards.
        var previous = SendId(appearanceClass, Selector("currentAppearance"));
        SendVoid(appearanceClass, Selector("setCurrentAppearance:"), appearance);
        try
        {
            var windowBackground = ReadColor("windowBackgroundColor");
            var label = ReadColor("labelColor");
            var secondaryLabel = ReadColor("secondaryLabelColor");
            var separator = ReadColor("separatorColor");
            var control = ReadColor("controlColor");
            var selected = ReadColor("selectedContentBackgroundColor");
            var unemphasized = ReadColor("unemphasizedSelectedContentBackgroundColor");
            return windowBackground is { } background
                && label is { } labelColor
                && secondaryLabel is { } secondaryLabelColor
                && separator is { } separatorColor
                && control is { } controlColor
                && selected is { } selectedColor
                && unemphasized is { } unemphasizedColor
                    ? new HostSemanticColors(
                        background,
                        labelColor,
                        secondaryLabelColor,
                        separatorColor,
                        controlColor,
                        selectedColor,
                        unemphasizedColor)
                    : null;
        }
        finally
        {
            SendVoid(appearanceClass, Selector("setCurrentAppearance:"), previous);
        }
    }

    private static Color? ReadColor(string name)
    {
        var color = SendId(objc_getClass("NSColor"), Selector(name));
        if (color == 0)
        {
            return null;
        }

        var srgb = SendIdArgument(
            color,
            Selector("colorUsingColorSpace:"),
            SendId(objc_getClass("NSColorSpace"), Selector("sRGBColorSpace")));
        if (srgb == 0)
        {
            return null;
        }

        return Color.FromArgb(
            ToByte(SendDouble(srgb, Selector("alphaComponent"))),
            ToByte(SendDouble(srgb, Selector("redComponent"))),
            ToByte(SendDouble(srgb, Selector("greenComponent"))),
            ToByte(SendDouble(srgb, Selector("blueComponent"))));
    }

    private static bool AccentIsMulticolor()
    {
        var defaults = SendId(objc_getClass("NSUserDefaults"), Selector("standardUserDefaults"));
        return defaults != 0
            && SendIdArgument(
                defaults,
                Selector("objectForKey:"),
                NSString("AppleAccentColor")) == 0;
    }

    private static byte ToByte(double component) =>
        (byte)Math.Round(Math.Clamp(component, 0, 1) * byte.MaxValue);

    private static nint NSString(string value) => SendIdString(
        objc_getClass("NSString"),
        Selector("stringWithUTF8String:"),
        value);

    private void DisposeNativeObserver()
    {
        var observer = Interlocked.Exchange(ref _observer, 0);
        var notificationCenter = Interlocked.Exchange(ref _notificationCenter, 0);
        if (observer == 0)
        {
            return;
        }

        Sources.TryRemove(observer, out _);
        if (notificationCenter != 0)
        {
            SendVoid(notificationCenter, Selector("removeObserver:"), observer);
        }

        SendVoid(observer, Selector("release"));
    }

    private static nint GetObserverClass()
    {
        lock (NativeClassGate)
        {
            if (_observerClass != 0)
            {
                return _observerClass;
            }

            _observerClass = objc_getClass(ObserverClassName);
            if (_observerClass != 0)
            {
                return _observerClass;
            }

            var observerClass = objc_allocateClassPair(
                objc_getClass("NSObject"),
                ObserverClassName,
                0);
            if (observerClass == 0)
            {
                throw new InvalidOperationException(
                    "Could not allocate the macOS system-colour observer class.");
            }

            if (!class_addMethod(
                    observerClass,
                    Selector(ObserverSelector),
                    Marshal.GetFunctionPointerForDelegate(Callback),
                    "v@:@"))
            {
                objc_disposeClassPair(observerClass);
                throw new InvalidOperationException(
                    "Could not register the macOS system-colour observer callback.");
            }

            objc_registerClassPair(observerClass);
            _observerClass = observerClass;
            return observerClass;
        }
    }

    private static nint ReadNotificationName()
    {
        lock (NativeClassGate)
        {
            EnsureAppKitLoaded();
            return Marshal.ReadIntPtr(NativeLibrary.GetExport(_appKitHandle, NotificationSymbol));
        }
    }

    private static void EnsureAppKitLoaded()
    {
        lock (NativeClassGate)
        {
            if (_appKitHandle == 0)
            {
                _appKitHandle = NativeLibrary.Load(AppKitLibrary);
            }
        }
    }

    private static void OnNativeNotification(nint observer, nint selector, nint notification)
    {
        _ = selector;
        _ = notification;
        try
        {
            if (Sources.TryGetValue(observer, out var weakSource)
                && weakSource.TryGetTarget(out var source))
            {
                // AppKit posts this on the main thread, which is Avalonia's UI
                // thread on macOS, so subscribers can restyle directly.
                source.Refresh();
            }
        }
        catch (Exception)
        {
            // Managed exceptions must never cross an Objective-C callback boundary.
        }
    }

    private static nint Selector(string name) => sel_registerName(name);

    private static bool IsUnavailable(Exception exception) => exception is
        DllNotFoundException
        or EntryPointNotFoundException
        or BadImageFormatException
        or ExternalException
        or InvalidOperationException;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotificationCallback(nint observer, nint selector, nint notification);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_allocateClassPair(
        nint superClass,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        nuint extraBytes);

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_registerClassPair(nint targetClass);

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_disposeClassPair(nint targetClass);

    [DllImport(ObjectiveCLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(
        nint targetClass,
        nint selector,
        nint implementation,
        [MarshalAs(UnmanagedType.LPStr)] string types);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendId(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdArgument(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIdString(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint argument);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(
        nint receiver,
        nint selector,
        nint argument1,
        nint argument2,
        nint argument3,
        nint argument4);
}
