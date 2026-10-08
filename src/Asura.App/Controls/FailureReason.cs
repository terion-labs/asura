using System.Text;

namespace Asura.App.Controls;

/// <summary>
/// Says why a file, clipboard, or diagram action failed, in words a person can
/// act on.
///
/// The data panels used to append the exception's own message to their error
/// line ("Could not import the CSV file: Access to the path … is denied."),
/// which is written for developers, names framework types, and offers no next
/// step. Asura's own refusals — a CSV row with too many values, a file over the
/// size limit — are thrown as <see cref="InvalidDataException"/> with a message
/// written for people, so those pass through; everything else gets a sentence
/// chosen here. The exception itself still goes to diagnostics wherever the
/// caller logged it.
/// </summary>
internal static class FailureReason
{
    internal static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            InvalidDataException => exception.Message,
            DecoderFallbackException => "The file isn’t UTF-8 text.",
            UnauthorizedAccessException => "Asura doesn’t have permission to use that file. Choose another location, or check the file’s permissions.",
            FileNotFoundException or DirectoryNotFoundException => "The file or folder no longer exists. Choose it again.",
            PathTooLongException => "The file’s path is too long. Choose a location with a shorter path.",
            IOException => "The file couldn’t be read or written. Check that the disk is available, then try again.",
            _ => "Something unexpected went wrong. Try again.",
        };
    }
}
