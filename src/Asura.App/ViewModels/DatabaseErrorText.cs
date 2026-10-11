using System.Data.Common;
using System.Net.Sockets;
using System.Security.Authentication;
using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>What the database panel was doing when an operation failed.</summary>
internal enum DatabaseOperation
{
    Connect,
    RunStatement,
    LoadRows,
    SaveChanges,
    ReloadAfterSave,
    OpenDiagram,
    OpenValue,
}

/// <summary>
/// A database panel failure as a person reads it: what didn't happen, why
/// when the database said, and what to do next.
///
/// The panel used to show the exception's own message, which for anything but
/// a server reply is written for developers ("Detached database parameters
/// must be decoded…", "Value cannot be null. (Parameter 'sql')") and offers no
/// next step. The reason a database server gives — a syntax error, a missing
/// table, a constraint — is kept after the curated sentence, because only the
/// server can say it and the person needs it to fix the statement; its codes
/// and the client's own text go to diagnostics.
/// </summary>
internal static class DatabaseErrorText
{
    private const int MaximumReasonCharacters = 300;

    // The operation worker reports a provider failure as an IOException whose
    // message starts with this, followed by the server's own text.
    private const string WorkerReasonPrefix = "Database reported: ";

    public static string Describe(DatabaseOperation operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SecretSafeDiagnostics.WriteTrace(DiagnosticCode(operation), exception);
        if (FailureReason.IsReadable(exception))
        {
            return exception.Message;
        }

        if (Find<DatabaseMutationOutcomeUnknownException>(exception) is not null)
        {
            return "Asura lost contact with the database before it confirmed the result, so the change may or may not "
                + "have been made. Reload the data and check it before you try again.";
        }

        // A client library reports an unreachable server as its own database
        // exception around the socket or timeout failure; that isn't the
        // server's reason, so the transport failures are named first.
        if (Find<TimeoutException>(exception) is not null)
        {
            return $"{WhatHappened(operation)} The database didn’t answer in time. Check the connection, then try again.";
        }

        if (Find<AuthenticationException>(exception) is not null)
        {
            return $"{WhatHappened(operation)} Asura couldn’t set up a secure connection. Check the connection’s TLS settings, then try again.";
        }

        if (Find<SocketException>(exception) is not null)
        {
            return operation == DatabaseOperation.Connect
                ? "Asura couldn’t reach the database server. Check the host and port and that the server is running, then connect again."
                : $"{WhatHappened(operation)} The connection to the database was lost. Reconnect, then try again.";
        }

        if (ServerReason(exception) is { } reason)
        {
            return $"{WhatHappened(operation)} The database said: {reason} {NextStepForServerReason(operation)}";
        }

        return $"{WhatHappened(operation)} {NextStep(operation)}";
    }

    private static string WhatHappened(DatabaseOperation operation) => operation switch
    {
        DatabaseOperation.Connect => "Asura couldn’t connect to the database.",
        DatabaseOperation.RunStatement => "Asura couldn’t run the statement.",
        DatabaseOperation.LoadRows => "Asura couldn’t load the rows.",
        DatabaseOperation.SaveChanges => "Asura couldn’t save the changes.",
        DatabaseOperation.ReloadAfterSave => "The changes were saved, but Asura couldn’t reload the rows.",
        DatabaseOperation.OpenDiagram => "Asura couldn’t open the diagram.",
        DatabaseOperation.OpenValue => "Asura couldn’t open the full value.",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static string NextStepForServerReason(DatabaseOperation operation) => operation switch
    {
        DatabaseOperation.Connect => "Check the connection’s settings, then connect again.",
        DatabaseOperation.RunStatement => "Fix the statement, then run it again.",
        DatabaseOperation.LoadRows => "Check the filters and sorting, then try again.",
        DatabaseOperation.SaveChanges => "Your edits are kept. Correct the values, then save again.",
        _ => NextStep(operation),
    };

    private static string NextStep(DatabaseOperation operation) => operation switch
    {
        DatabaseOperation.Connect => "Check the connection’s settings, then connect again.",
        DatabaseOperation.SaveChanges => "Your edits are kept. Save again; if it keeps failing, reconnect.",
        DatabaseOperation.ReloadAfterSave => "Reload the table to see them.",
        _ => "Try again; if it keeps failing, reconnect.",
    };

    private static string DiagnosticCode(DatabaseOperation operation) => operation switch
    {
        DatabaseOperation.Connect => "database.connect.failed",
        DatabaseOperation.RunStatement => "database.statement.failed",
        DatabaseOperation.LoadRows => "database.rows.load.failed",
        DatabaseOperation.SaveChanges => "database.changes.save.failed",
        DatabaseOperation.ReloadAfterSave => "database.changes.reload.failed",
        DatabaseOperation.OpenDiagram => "database.diagram.open.failed",
        DatabaseOperation.OpenValue => "database.value.open.failed",
        _ => "database.operation.failed",
    };

    /// <summary>
    /// The server's own reason, on one line and bounded, or null when the
    /// failure didn't come from the database server.
    /// </summary>
    private static string? ServerReason(Exception exception)
    {
        string? text = null;
        for (var current = exception; current is not null && text is null; current = current.InnerException)
        {
            if (current is DbException)
            {
                text = current.Message;
            }
            else if (current is IOException
                && current.Message.StartsWith(WorkerReasonPrefix, StringComparison.Ordinal))
            {
                text = current.Message[WorkerReasonPrefix.Length..];
            }
        }

        var reason = text?.Split('\n', 2)[0].Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return null;
        }

        if (reason.Length > MaximumReasonCharacters)
        {
            reason = string.Concat(reason.AsSpan(0, MaximumReasonCharacters - 1), "…");
        }

        return reason[^1] is '.' or '!' or '?' or '…' ? reason : reason + ".";
    }

    private static T? Find<T>(Exception exception)
        where T : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
