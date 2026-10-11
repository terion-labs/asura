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
        var failure = Reported("duplicate key value violates unique constraint \"people_pkey\"");

        var text = DatabaseErrorText.Describe(DatabaseOperation.SaveChanges, failure);

        Assert.Equal(
            "Asura couldn’t save the changes. The database said: duplicate key value violates unique constraint "
            + "\"people_pkey\". Your edits are kept. Correct the values, then save again.",
            text);
        Assert.DoesNotContain("SQLSTATE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Database reported", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_statement_warns_that_earlier_statements_may_have_run()
    {
        var failure = Reported("relation \"people\" does not exist");

        Assert.Equal(
            "Asura couldn’t run the statement. The database said: relation \"people\" does not exist. "
            + "Statements before it may already have run. Check the database before you run it again.",
            DatabaseErrorText.Describe(DatabaseOperation.RunStatement, failure));
    }

    [Fact]
    public void A_failed_statement_keeps_the_caveat_when_the_server_gave_no_reason()
    {
        var failure = Reported(DatabaseProviderFailureException.ReasonOmittedForSize);

        Assert.Equal(
            "Asura couldn’t run the statement. Statements before it may already have run. "
            + "Check the database before you run it again.",
            DatabaseErrorText.Describe(DatabaseOperation.RunStatement, failure));
    }

    [Fact]
    public void The_line_and_position_the_server_named_are_kept()
    {
        Assert.Equal(
            "Asura couldn’t run the statement. The database said: syntax error at or near \"FORM\" "
            + "(line 3, position 14). Statements before it may already have run. "
            + "Check the database before you run it again.",
            DatabaseErrorText.Describe(
                DatabaseOperation.RunStatement,
                Reported("syntax error at or near \"FORM\".", line: 3, position: 14)));
        Assert.Contains(
            "Incorrect syntax near 'x' (line 2).",
            DatabaseErrorText.Describe(DatabaseOperation.LoadRows, Reported("Incorrect syntax near 'x'.", line: 2)),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DatabaseProviderFailureException.ReasonOmittedWithoutSecretExtraction)]
    [InlineData(DatabaseProviderFailureException.ReasonOmittedForSize)]
    public void A_withheld_reason_is_not_presented_as_the_server_s(string placeholder)
    {
        var text = DatabaseErrorText.Describe(DatabaseOperation.LoadRows, Reported(placeholder));

        Assert.Equal("Asura couldn’t load the rows. Try again; if it keeps failing, reconnect.", text);
    }

    [Fact]
    public void A_client_library_s_text_is_not_presented_as_the_server_s()
    {
        var failure = Reported("Exception while reading from stream", kind: DatabaseProviderFailureKind.Client);

        var text = DatabaseErrorText.Describe(DatabaseOperation.LoadRows, failure);

        Assert.DoesNotContain("The database said", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stream", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_the_worker_couldn_t_reach_is_named_without_the_client_s_text()
    {
        var failure = Reported("Failed to connect to 10.0.0.5:5432", kind: DatabaseProviderFailureKind.Unreachable);

        Assert.Equal(
            "Asura couldn’t reach the database server. Check the host and port and that the server is running, then connect again.",
            DatabaseErrorText.Describe(DatabaseOperation.Connect, failure));
        Assert.Equal(
            "Asura couldn’t run the statement. The connection to the database was lost. "
            + "Statements before it may already have run. Check the database before you run it again.",
            DatabaseErrorText.Describe(DatabaseOperation.RunStatement, failure));
    }

    [Fact]
    public void The_workspace_kill_switch_is_named_instead_of_the_connection_settings()
    {
        var text = DatabaseErrorText.Describe(
            DatabaseOperation.Connect,
            new InvalidOperationException("outer", new WorkspaceNetworkBlockedException()));

        Assert.StartsWith("Asura couldn’t connect to the database. The workspace’s network connection is down", text, StringComparison.Ordinal);
        Assert.Contains("turn off its kill switch in Edit Workspace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("connection’s settings", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_URL_parameter_the_driver_can_t_use_is_named()
    {
        var text = DatabaseErrorText.Describe(
            DatabaseOperation.Connect,
            new UnsupportedConnectionParameterException("PostgreSQL", ["gssencmode"]));

        Assert.Equal(
            "Asura couldn’t connect to the database. PostgreSQL can’t use the connection URL’s gssencmode parameter. "
            + "Remove it from the URL, or write the connection as keyword=value pairs instead.",
            text);
        Assert.Contains(
            "can’t use the connection URL’s a, b parameters. Remove them",
            DatabaseErrorText.DescribeUnsupportedParameters(new UnsupportedConnectionParameterException("MySQL", ["a", "b"])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_server_reason_is_bounded_to_one_line()
    {
        var failure = Reported(new string('x', 1000) + "\nsecond line");

        var text = DatabaseErrorText.Describe(DatabaseOperation.LoadRows, failure);

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain("second line", text, StringComparison.Ordinal);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.InRange(text.Length, 1, 500);
    }

    [Fact]
    public void A_database_exception_outside_the_worker_is_not_read_as_the_server_s_reason()
    {
        var text = DatabaseErrorText.Describe(
            DatabaseOperation.LoadRows,
            new ServerFailure("Exception while reading from stream"));

        Assert.Equal("Asura couldn’t load the rows. Try again; if it keeps failing, reconnect.", text);
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

    private static DatabaseProviderFailureException Reported(
        string reason,
        DatabaseProviderFailureKind kind = DatabaseProviderFailureKind.Server,
        int? line = null,
        int? position = null) =>
        new(
            "Database reported: " + reason + "\nSQLSTATE 23505, code 0\n"
            + "Earlier statements or side effects may have completed. Verify the database before retrying.",
            reason,
            kind,
            line,
            position);

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
