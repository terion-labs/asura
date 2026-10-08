using System.Text;
using Asura.App.Controls;

namespace Asura.App.Tests;

/// <summary>
/// The reason a data-panel action failed is written for the person reading
/// it: Asura's own refusals pass through, and framework failures — whose
/// messages name types and paths — become a sentence with a next step.
/// </summary>
public sealed class FailureReasonTests
{
    [Fact]
    public void Asuras_own_refusals_keep_their_message()
    {
        Assert.Equal(
            "The CSV file has no header row.",
            FailureReason.Describe(new InvalidDataException("The CSV file has no header row.")));
    }

    [Fact]
    public void Framework_failures_never_show_their_own_message()
    {
        Exception[] failures =
        [
            new UnauthorizedAccessException("Access to the path '/private/x.csv' is denied."),
            new FileNotFoundException("Could not find file '/private/x.csv'."),
            new DirectoryNotFoundException("Could not find a part of the path '/private'."),
            new PathTooLongException("The path '/private/x.csv' is too long."),
            new IOException("The process cannot access the file because it is being used."),
            new DecoderFallbackException("Unable to translate bytes [FF] at index 0."),
            new InvalidOperationException("Sequence contains no elements."),
        ];

        foreach (var failure in failures)
        {
            var reason = FailureReason.Describe(failure);
            Assert.DoesNotContain(failure.Message, reason, StringComparison.Ordinal);
            Assert.DoesNotContain("/private", reason, StringComparison.Ordinal);
            Assert.EndsWith(".", reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Permission_failures_say_what_to_do_next()
    {
        Assert.Equal(
            "Asura doesn’t have permission to use that file. Choose another location, or check the file’s permissions.",
            FailureReason.Describe(new UnauthorizedAccessException("denied")));
    }
}
