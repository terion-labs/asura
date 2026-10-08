using System.Diagnostics;
using System.Text;
using Asura.App.Controls;

namespace Asura.App.Tests;

/// <summary>
/// The reason a data-panel action failed is written for the person reading
/// it: refusals written for people pass through, and every other failure —
/// framework messages that name types and paths, and Asura's internal data
/// errors — becomes a sentence with a next step.
/// </summary>
public sealed class FailureReasonTests
{
    [Fact]
    public void Readable_refusals_keep_their_message()
    {
        Assert.Equal(
            "The selected file is larger than 16 MiB.",
            FailureReason.Describe(new ReadableFailureException("The selected file is larger than 16 MiB.")));
    }

    [Fact]
    public void Internal_data_errors_never_show_their_own_message()
    {
        var failure = new InvalidDataException("The detached database array format is invalid.");

        Assert.Equal("Something unexpected went wrong. Try again.", FailureReason.Describe(failure));
    }

    [Fact]
    public void Report_writes_a_secret_safe_trace_and_returns_the_reason()
    {
        using var traceOutput = new StringWriter();
        using var listener = new TextWriterTraceListener(traceOutput);
        var failure = new UnauthorizedAccessException("Access to the path '/private/x.csv' is denied.");
        string reason;
        Trace.Listeners.Add(listener);
        try
        {
            reason = FailureReason.Report("database.page.export.failed", failure);
            Trace.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Equal(FailureReason.Describe(failure), reason);
        Assert.Contains("code=database.page.export.failed", traceOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/private", traceOutput.ToString(), StringComparison.Ordinal);
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
