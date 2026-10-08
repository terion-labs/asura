using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Asura.Application;
using Asura.Core;

namespace Asura.Desktop;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct WindowsHotkeyGesture(uint VirtualKey, uint Modifiers);

internal readonly record struct WindowsHotkeyNativeResult(bool Succeeded, int ErrorCode)
{
    public static WindowsHotkeyNativeResult Success { get; } = new(true, 0);

    public static WindowsHotkeyNativeResult Failure(int errorCode) => new(false, errorCode);
}

internal interface IWindowsHotkeyLoop : IDisposable
{
    event Action<int>? HotkeyPressed;

    WindowsHotkeyNativeResult Register(int id, WindowsHotkeyGesture gesture);

    void Unregister(int id);
}

internal sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int PrimaryHotkeyId = 1;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const int ErrorInvalidHotkey = 1422;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierWindows = 0x0008;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeyOem3 = 0xC0;
    private const uint VirtualKeyF1 = 0x70;
    private const uint VirtualKeyF24 = 0x87;

    private readonly object _lifecycleGate = new();
    private readonly IWindowsHotkeyLoop _loop;
    private KeyStroke? _registeredGesture;
    private int _primaryRegistered;
    private long _primaryGeneration;
    private bool _disposed;

    public WindowsGlobalHotkeyService()
        : this(CreateNativeLoop())
    {
    }

    internal WindowsGlobalHotkeyService(IWindowsHotkeyLoop loop)
    {
        _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        _loop.HotkeyPressed += OnHotkeyPressed;
    }

    public event EventHandler? Pressed;

    public KeyStroke? RegisteredGesture
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _registeredGesture;
            }
        }
    }

    public GlobalHotkeyRegistrationResult Register(KeyStroke gesture)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            UnregisterPrimary();
            if (!TryMapGesture(gesture, out var nativeGesture))
            {
                return Failure(
                    GlobalHotkeyRegistrationErrorCode.InvalidGesture,
                    "global_hotkey_invalid_gesture",
                    "Windows can't use this shortcut as a global shortcut. Choose another one.");
            }

            WindowsHotkeyNativeResult nativeResult;
            try
            {
                nativeResult = _loop.Register(PrimaryHotkeyId, nativeGesture);
            }
            catch (Win32Exception)
            {
                return NativeFailure();
            }

            if (!nativeResult.Succeeded)
            {
                return MapRegistrationFailure(nativeResult.ErrorCode);
            }

            _registeredGesture = gesture;
            _ = Interlocked.Increment(ref _primaryGeneration);
            Volatile.Write(ref _primaryRegistered, 1);
            return new GlobalHotkeyRegistrationResult.Success(gesture);
        }
    }

    public void Unregister()
    {
        lock (_lifecycleGate)
        {
            UnregisterPrimary();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UnregisterPrimary();
            _loop.HotkeyPressed -= OnHotkeyPressed;
            _loop.Dispose();
        }
    }

    internal static bool TryMapGesture(
        KeyStroke gesture,
        out WindowsHotkeyGesture nativeGesture)
    {
        nativeGesture = default;
        if (gesture.Modifiers == KeyModifiers.None
            || !TryMapVirtualKey(gesture.Key, out var virtualKey))
        {
            return false;
        }

        var modifiers = ModifierNoRepeat;
        if ((gesture.Modifiers & KeyModifiers.Alt) != KeyModifiers.None)
        {
            modifiers |= ModifierAlt;
        }

        if ((gesture.Modifiers & KeyModifiers.Control) != KeyModifiers.None)
        {
            modifiers |= ModifierControl;
        }

        if ((gesture.Modifiers & KeyModifiers.Shift) != KeyModifiers.None)
        {
            modifiers |= ModifierShift;
        }

        if ((gesture.Modifiers & KeyModifiers.Meta) != KeyModifiers.None)
        {
            modifiers |= ModifierWindows;
        }

        nativeGesture = new WindowsHotkeyGesture(virtualKey, modifiers);
        return true;
    }

    private static IWindowsHotkeyLoop CreateNativeLoop()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The RegisterHotKey adapter requires Windows.");
        }

        return new WindowsHotkeyMessageLoop();
    }

    private static bool TryMapVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = key switch
        {
            "`" or "GRAVE" or "OEMTILDE" => VirtualKeyOem3,
            "SPACE" => 0x20,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "END" => 0x23,
            "HOME" => 0x24,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            _ => 0,
        };
        if (virtualKey != 0)
        {
            return true;
        }

        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            virtualKey = key[0];
            return true;
        }

        if (key.Length >= 2
            && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out var functionNumber) && functionNumber is >= 1 and <= 24)
        {
            virtualKey = VirtualKeyF1 + (uint)(functionNumber - 1);
            return virtualKey <= VirtualKeyF24;
        }

        return false;
    }

    private void UnregisterPrimary()
    {
        if (Interlocked.Exchange(ref _primaryRegistered, 0) != 0)
        {
            _ = Interlocked.Increment(ref _primaryGeneration);
            _loop.Unregister(PrimaryHotkeyId);
        }

        _registeredGesture = null;
    }

    private void OnHotkeyPressed(int id)
    {
        if (id != PrimaryHotkeyId || Volatile.Read(ref _primaryRegistered) == 0)
        {
            return;
        }

        var generation = Volatile.Read(ref _primaryGeneration);

        // The native message loop must never be held by presentation callbacks: registration and
        // disposal synchronously marshal work back to that same loop.
        ThreadPool.QueueUserWorkItem(
            static state => state.Service.DispatchHotkey(state.Generation),
            (Service: this, Generation: generation),
            preferLocal: false);
    }

    private void DispatchHotkey(long generation)
    {
        try
        {
            if (Volatile.Read(ref _primaryRegistered) != 0
                && Volatile.Read(ref _primaryGeneration) == generation)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            Asura.Application.SecretSafeDiagnosticProjection.WriteTrace(
                "desktop.hotkey.windows-service-callback.failed",
                exception);
        }
    }

    private static GlobalHotkeyRegistrationResult MapRegistrationFailure(int errorCode) =>
        errorCode switch
        {
            ErrorHotkeyAlreadyRegistered => Failure(
                GlobalHotkeyRegistrationErrorCode.Conflict,
                "global_hotkey_conflict",
                "Another app already uses this shortcut."),
            ErrorInvalidHotkey => Failure(
                GlobalHotkeyRegistrationErrorCode.InvalidGesture,
                "global_hotkey_invalid_gesture",
                "Windows didn't accept this shortcut. Choose another one."),
            _ => NativeFailure(),
        };

    // The Win32 error code stays out of the message people read.
    private static GlobalHotkeyRegistrationResult NativeFailure()
    {
        SecretSafeDiagnosticProjection.WriteTrace(
            "desktop.hotkey.windows-native-failure",
            SecretSafeDiagnosticKind.Unexpected);
        return Failure(
            GlobalHotkeyRegistrationErrorCode.NativeFailure,
            "global_hotkey_native_failure",
            "Windows didn't accept this shortcut. Try again, or choose another one.");
    }

    private static GlobalHotkeyRegistrationResult Failure(
        GlobalHotkeyRegistrationErrorCode code,
        string stableCode,
        string message) =>
        new GlobalHotkeyRegistrationResult.Failure(new(code, stableCode, message));
}
