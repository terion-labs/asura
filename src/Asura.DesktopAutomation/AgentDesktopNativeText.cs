using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Asura.DesktopAutomation;

/// <summary>
/// Conversions between managed strings and the NUL-terminated UTF-8 the C
/// library speaks. The library hands out Rust <c>CString</c>s, so a non-null
/// pointer is always terminated; what is not trusted is the size, because the
/// text describes another application. Every read is therefore bounded.
/// </summary>
internal static unsafe class AgentDesktopNativeText
{
    /// <summary>Longest application, window, or process token read from a list entry.</summary>
    public const int MaximumFieldBytes = 1_024;

    /// <summary>
    /// Encodes one argument. A value with an embedded NUL would be silently
    /// shortened by the C side, so it is refused instead.
    /// </summary>
    public static byte[] ToUtf8Z(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A native argument cannot contain NUL.", nameof(value));
        }

        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }

    /// <summary>
    /// Copies the bytes before the terminator, or returns null when the
    /// pointer is null or the text is longer than the bound. The scan stops at
    /// the terminator, so it never reads past the allocation.
    /// </summary>
    public static byte[]? ReadBytes(byte* text, int maximumBytes)
    {
        if (text is null)
        {
            return null;
        }

        var bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text);
        return bytes.Length > maximumBytes ? null : bytes.ToArray();
    }

    /// <summary>
    /// Reads one bounded field. Malformed UTF-8 decodes to replacement
    /// characters, which the display projection then treats as ordinary text.
    /// </summary>
    public static string? Read(byte* text, int maximumBytes = MaximumFieldBytes)
    {
        var bytes = ReadBytes(text, maximumBytes);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// True for the identifier-like tokens Asura passes back to the library
    /// (a window id, a process-generation token): non-empty, single-line,
    /// printable, and short.
    /// </summary>
    public static bool IsBoundedToken(string? value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            return false;
        }

        // The same single-line rule the Application contracts enforce, so a
        // token accepted here can never make a result constructor throw.
        foreach (var character in value)
        {
            if (char.IsSurrogate(character)
                || char.GetUnicodeCategory(character) is
                    UnicodeCategory.Control
                    or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }
}
