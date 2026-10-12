using System.Text;
using Asura.App.ViewModels;

namespace Asura.App.Controls;

/// <summary>
/// Says why a file, clipboard, or diagram action failed, in words a person can
/// act on, and records the failure in diagnostics.
///
/// The data panels used to append the exception's own message to their error
/// line ("Could not import the CSV file: Access to the path … is denied."),
/// which is written for developers, names framework types, and offers no next
/// step. Only a refusal whose message was written for people keeps its
/// message: a <see cref="ReadableFailureException"/>, an exception marked with
/// <see cref="Readable{TException}"/>, or an <see cref="InvalidField"/>
/// failure. Every other failure, including other
/// <see cref="InvalidDataException"/> messages, gets a sentence chosen here.
/// <see cref="Report"/> and <see cref="Refusal"/> also write a secret-safe
/// trace (the stable code and the failure kind, never the message or a path),
/// so the detail the person no longer sees is not lost.
/// </summary>
internal static class FailureReason
{
    private const string ReadableKey = "Asura.ReadableFailure";

    /// <summary>
    /// Marks an exception Asura throws with words meant for the person
    /// ("Enter one key with at least one modifier…"), so it is shown as
    /// written. The exception keeps its type, so the code that catches it by
    /// type is unchanged.
    /// </summary>
    internal static TException Readable<TException>(TException exception)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[ReadableKey] = true;
        return exception;
    }

    /// <summary>Whether <paramref name="exception"/>'s message was written for people.</summary>
    internal static bool IsReadable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is ReadableFailureException
            || exception.Data.Contains(ReadableKey)
            || InvalidField.Of(exception) is not null;
    }

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

    /// <summary>
    /// The sentence for a request Asura refused before running it: form
    /// values a profile won't accept, a location that can't be parsed, a Git
    /// request a selection no longer fits. A message written for people is
    /// shown as written. Anything else comes from a lower layer's argument
    /// checks ("The value cannot be an empty string. (Parameter 'host')"), so
    /// it goes to diagnostics and <paramref name="fallback"/> — what happened
    /// and what to do next — is shown instead.
    /// </summary>
    internal static string Refusal(string diagnosticCode, Exception exception, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        if (IsReadable(exception))
        {
            return exception.Message;
        }

        SecretSafeDiagnostics.WriteTrace(diagnosticCode, exception);
        return fallback;
    }

    internal static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsReadable(exception))
        {
            return exception.Message;
        }

        return exception switch
        {
            DecoderFallbackException => "The file isn’t UTF-8 text.",
            UnauthorizedAccessException => "Asura doesn’t have permission to use that file. Choose another location, or check the file’s permissions.",
            FileNotFoundException or DirectoryNotFoundException => "The file or folder no longer exists. Choose it again.",
            PathTooLongException => "The file’s path is too long. Choose a location with a shorter path.",
            IOException => "The file couldn’t be read or written. Check that the disk is available, then try again.",
            _ => "Something unexpected went wrong. Try again.",
        };
    }
}
