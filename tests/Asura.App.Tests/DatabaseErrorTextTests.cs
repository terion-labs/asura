using System.Data.Common;
using System.Net.Sockets;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// A database panel failure reads as what didn't happen and what to do next;
/// the reason a database server gave stays, because only the server can say
/// it, and everything else the client or Asura's own layers said goes to
/// diagnostics.
/// </summary>
public sealed class DatabaseErrorTextTests
{
    [Fact]
    public void A_server_reason_follows_the_sentence_and_its_next_step()
    {
        var failure = new ServerFailure("relation \"people\" does not exist");

        Assert.Equal(
            "Asura couldn’t run the statement. The database said: relation \"people\" does not exist. "
            + "Fix the statement, then run it again.",
            DatabaseErrorText.Describe(DatabaseOperation.RunStatement, failure));
    }

    [Fact]
    public void The_worker_reports_the_server_reason_without_its_codes_or_caveats()
    {
        var failure = new IOException(
            "Database reported: duplicate key value violates unique constraint \"people_pkey\"\n"
            + "SQLSTATE 23505, code 0\n"
            + "Earlier statements or side effects may have completed. Verify the database before retrying.");

        var text = DatabaseErrorText.Describe(DatabaseOperation.SaveChanges, failure);

        Assert.Equal(
            "Asura couldn’t save the changes. The database said: duplicate key value violates unique constraint "
            + "\"people_pkey\". Your edits are kept. Correct the values, then save again.",
            text);
        Assert.DoesNotContain("SQLSTATE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_server_reason_is_bounded_to_one_line()
    {
        var failure = new ServerFailure(new string('x', 1000) + "\nsecond line");

        var text = DatabaseErrorText.Describe(DatabaseOperation.LoadRows, failure);

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain("second line", text, StringComparison.Ordinal);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.InRange(text.Length, 1, 500);
    }

    [Fact]
    public void An_unreachable_server_is_named_before_the_client_exception_around_it()
    {
        var failure = new ServerFailure(
            "Failed to connect to 10.0.0.5:5432",
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.Equal(
            "Asura couldn’t reach the database server. Check the host and port and that the server is running, then connect again.",
            DatabaseErrorText.Describe(DatabaseOperation.Connect, failure));
    }

    [Fact]
    public void A_mutation_with_an_unknown_outcome_says_to_check_before_retrying()
    {
        var text = DatabaseErrorText.Describe(
            DatabaseOperation.SaveChanges,
            new DatabaseMutationOutcomeUnknownException());

        Assert.Contains("may or may not have been made", text, StringComparison.Ordinal);
        Assert.DoesNotContain("worker", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Detached database parameters must be decoded by the owned operation worker before provider dispatch.")]
    [InlineData("Value cannot be null. (Parameter 'sql')")]
    public void Developer_text_never_reaches_the_panel(string message)
    {
        foreach (var operation in Enum.GetValues<DatabaseOperation>())
        {
            var text = DatabaseErrorText.Describe(operation, new InvalidOperationException(message));

            Assert.DoesNotContain(message, text, StringComparison.Ordinal);
            Assert.StartsWith("Asura", text.Replace("The changes were saved, but Asura", "Asura", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.EndsWith(".", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_refusal_written_for_people_is_shown_as_written()
    {
        var refusal = FailureReason.Readable(new IOException(
            "There isn't enough memory to open this complete value in the text editor."));

        Assert.Equal(refusal.Message, DatabaseErrorText.Describe(DatabaseOperation.OpenValue, refusal));
    }

    private sealed class ServerFailure : DbException
    {
        public ServerFailure()
        {
        }

        public ServerFailure(string message)
            : base(message)
        {
        }

        public ServerFailure(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
