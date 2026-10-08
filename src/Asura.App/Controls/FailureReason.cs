using System.Text;

namespace Asura.App.Controls;

/// <summary>
/// Says why a file, clipboard, or diagram action failed, in words a person can
/// act on, and records the failure in diagnostics.
///
/// The data panels used to append the exception's own message to their error
/// line ("Could not import the CSV file: Access to the path … is denied."),
/// which is written for developers, names framework types, and offers no next
/// step. Only a <see cref="ReadableFailureException"/> — a refusal whose
/// message was written for people, such as an unsupported export file name —
/// keeps its message. Every other failure, including other
/// <see cref="InvalidDataException"/> messages, gets a sentence chosen here.
/// <see cref="Report"/> also writes a secret-safe trace (the stable code and
/// the failure kind, never the message or a path), so the detail the person no
/// longer sees is not lost.
/// </summary>
internal static class FailureReason
{
    /// <summary>
    /// Writes a secret-safe diagnostic trace under <paramref name="diagnosticCode"/>
    /// and returns the sentence to show.
    /// </summary>
    internal static string Report(string diagnosticCode, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        ArgumentNullException.ThrowIfNull(exception);
        SecretSafeDiagnostics.WriteTrace(diagnosticCode, exception);
        return Describe(exception);
    }

    internal static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ReadableFailureException => exception.Message,
            DecoderFallbackException => "The file isn’t UTF-8 text.",
            UnauthorizedAccessException => "Asura doesn’t have permission to use that file. Choose another location, or check the file’s permissions.",
            FileNotFoundException or DirectoryNotFoundException => "The file or folder no longer exists. Choose it again.",
            PathTooLongException => "The file’s path is too long. Choose a location with a shorter path.",
            IOException => "The file couldn’t be read or written. Check that the disk is available, then try again.",
            _ => "Something unexpected went wrong. Try again.",
        };
    }
}
