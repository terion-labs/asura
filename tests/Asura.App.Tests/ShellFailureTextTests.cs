using System.Diagnostics;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// The window's own refusals — opening an editor, switching a connection,
/// queuing a transfer, running a command, closing sessions — say what didn't
/// happen and what to do next. A refusal Asura wrote for people keeps its
/// words; the lower layers' text goes to diagnostics.
/// </summary>
public sealed class ShellFailureTextTests
{
    private static readonly string[] SiteNames =
    [
        nameof(ShellFailureText.ConnectionEditor),
        nameof(ShellFailureText.PanelConnection),
        nameof(ShellFailureText.BrowserNavigation),
        nameof(ShellFailureText.FileTransfer),
        nameof(ShellFailureText.AiProviderEditor),
        nameof(ShellFailureText.McpServerEditor),
        nameof(ShellFailureText.SavedScreenEditor),
        nameof(ShellFailureText.WorkspaceSave),
        nameof(ShellFailureText.Command),
    ];

    public static TheoryData<string> Sites()
    {
        var data = new TheoryData<string>();
        foreach (var site in SiteNames)
        {
            data.Add(site);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sites))]
    public void Developer_text_is_replaced_by_a_sentence_with_a_next_step(string site)
    {
        var failure = new InvalidOperationException("Sequence contains no matching element");

        var text = Describe(site, failure);

        Assert.DoesNotContain("Sequence", text, StringComparison.Ordinal);
        Assert.EndsWith(".", text, StringComparison.Ordinal);
        Assert.Matches("(Try|Check|Choose|Wait)", text);
    }

    [Theory]
    [MemberData(nameof(Sites))]
    public void A_refusal_written_for_people_keeps_its_words(string site)
    {
        var failure = FailureReason.Readable(
            new InvalidOperationException("That connection no longer exists."));

        Assert.Equal("That connection no longer exists.", Describe(site, failure));
    }

    [Fact]
    public void The_replaced_text_goes_to_diagnostics_without_its_message()
    {
        using var traceOutput = new StringWriter();
        using var listener = new TextWriterTraceListener(traceOutput);
        Trace.Listeners.Add(listener);
        try
        {
            _ = ShellFailureText.FileTransfer(
                new ArgumentException("The path '/Users/someone/secret.txt' is invalid."));
            Trace.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Contains("code=shell.file-transfer.refused", traceOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/someone", traceOutput.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HostErrorCode.DeadlineExceeded, "didn’t close in time")]
    [InlineData(HostErrorCode.ResynchronizationRequired, "couldn’t confirm")]
    [InlineData(HostErrorCode.EngineFailed, "couldn’t close the sessions")]
    [InlineData(HostErrorCode.InvalidRequest, "couldn’t close the sessions")]
    public void A_host_close_failure_is_described_by_its_code(HostErrorCode code, string expected)
    {
        var text = ShellFailureText.CloseFailed(
            HostError.Create(code, "The operation deadline has elapsed."));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("deadline", text, StringComparison.Ordinal);
        Assert.Contains("Try closing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_that_still_has_work_asks_to_be_closed_again()
    {
        Assert.Equal(
            "A session still has work running, so it wasn’t closed. Close it again to confirm.",
            ShellFailureText.SessionCloseFailed(SessionCloseOutcome.ConfirmationRequired));
        Assert.Equal(
            "A session couldn’t be closed, so it’s still open. Try closing it again.",
            ShellFailureText.SessionCloseFailed(SessionCloseOutcome.EngineFailed));
    }

    private static string Describe(string site, Exception exception) => site switch
    {
        nameof(ShellFailureText.ConnectionEditor) => ShellFailureText.ConnectionEditor(exception),
        nameof(ShellFailureText.PanelConnection) => ShellFailureText.PanelConnection(exception),
        nameof(ShellFailureText.BrowserNavigation) => ShellFailureText.BrowserNavigation(exception),
        nameof(ShellFailureText.FileTransfer) => ShellFailureText.FileTransfer(exception),
        nameof(ShellFailureText.AiProviderEditor) => ShellFailureText.AiProviderEditor(exception),
        nameof(ShellFailureText.McpServerEditor) => ShellFailureText.McpServerEditor(exception),
        nameof(ShellFailureText.SavedScreenEditor) => ShellFailureText.SavedScreenEditor(exception),
        nameof(ShellFailureText.WorkspaceSave) => ShellFailureText.WorkspaceSave(exception),
        nameof(ShellFailureText.Command) => ShellFailureText.Command(exception),
        _ => throw new ArgumentOutOfRangeException(nameof(site), site, null),
    };
}
