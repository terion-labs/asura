using Asura.Core;

namespace Asura.Application;

public enum GlobalHotkeyRegistrationErrorCode
{
    Unsupported,
    InvalidGesture,
    Conflict,
    NativeFailure,
}

public sealed record GlobalHotkeyRegistrationError(
    GlobalHotkeyRegistrationErrorCode Code,
    string StableCode,
    string Message);

public abstract record GlobalHotkeyRegistrationResult
{
    private GlobalHotkeyRegistrationResult()
    {
    }

    public sealed record Success(KeyStroke Gesture) : GlobalHotkeyRegistrationResult;

    public sealed record Failure(GlobalHotkeyRegistrationError Error) : GlobalHotkeyRegistrationResult;
}

/// <summary>
/// Owns the process-wide Quick Terminal shortcut. Implementations must report conflicts instead of
/// silently sharing a system gesture with another registration. Escape is never captured globally:
/// while Quick Terminal is open it belongs to the program running in the terminal.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? Pressed;

    KeyStroke? RegisteredGesture { get; }

    GlobalHotkeyRegistrationResult Register(KeyStroke gesture);

    void Unregister();
}
