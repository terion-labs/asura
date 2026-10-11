using System.Diagnostics;
using System.Text;
using Asura.App.Controls;
using Asura.App.ViewModels;

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

    [Fact]
    public void A_marked_refusal_keeps_its_type_and_its_message()
    {
        var refusal = FailureReason.Readable(new FormatException("Use at least one modifier with the key."));

        Assert.IsType<FormatException>(refusal);
        Assert.True(FailureReason.IsReadable(refusal));
        Assert.Equal("Use at least one modifier with the key.", FailureReason.Describe(refusal));
    }

    [Fact]
    public void A_field_message_is_written_for_people()
    {
        var refusal = InvalidField.Exception("Port", "The port must be a number from 1 to 65535.");

        Assert.True(FailureReason.IsReadable(refusal));
        Assert.Equal(refusal.Message, FailureReason.Refusal("test.refused", refusal, "Check each value."));
    }

    [Fact]
    public void Argument_checks_from_lower_layers_become_the_fallback_and_a_trace()
    {
        using var traceOutput = new StringWriter();
        using var listener = new TextWriterTraceListener(traceOutput);
        Exception[] refusals =
        [
            new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", "host"),
            new ArgumentOutOfRangeException("port", 0, "An SSH port must be between 1 and 65535."),
            new UriFormatException("Invalid URI: The hostname could not be parsed."),
            new InvalidOperationException("A governed branch target is invalid."),
        ];
        var reasons = new List<string>();
        Trace.Listeners.Add(listener);
        try
        {
            foreach (var refusal in refusals)
            {
                reasons.Add(FailureReason.Refusal("connection.editor.refused", refusal, "Check each value, then test again."));
            }

            Trace.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.All(reasons, reason => Assert.Equal("Check each value, then test again.", reason));
        Assert.Contains("code=connection.editor.refused", traceOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", traceOutput.ToString(), StringComparison.Ordinal);
    }
}
