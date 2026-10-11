using System.Globalization;
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
/// server can say it and the person needs it to fix the statement, with the
/// line and position the server named; its codes and the client's own text go
/// to diagnostics. A cause Asura can name — the workspace kill switch, a URL
/// parameter the driver can't use — is named before any generic sentence.
/// </summary>
internal static class DatabaseErrorText
{
    private const int MaximumReasonCharacters = 300;

    // A script can fail part way through: the statements before the failing
    // one may already have committed, so "run it again" could repeat them.
    private const string StatementsMayHaveRun =
        "Statements before it may already have run. Check the database before you run it again.";

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

        if (Find<WorkspaceNetworkBlockedException>(exception) is not null)
        {
            return $"{WhatHappened(operation)} {KillSwitchBlocking}";
        }

        if (Find<UnsupportedConnectionParameterException>(exception) is { } unsupported)
        {
            return $"{WhatHappened(operation)} {DescribeUnsupportedParameters(unsupported)}";
        }

        // The provider failed after the worker dispatched the operation, so a
        // script may have run part way.
        var provider = Find<DatabaseProviderFailureException>(exception);
        var partlyRun = provider is not null && operation == DatabaseOperation.RunStatement;

        // A client library reports an unreachable server as its own database
        // exception around the socket or timeout failure; that isn't the
        // server's reason, so the transport failures are named first.
        if (Find<TimeoutException>(exception) is not null || provider?.Kind == DatabaseProviderFailureKind.TimedOut)
        {
            return $"{WhatHappened(operation)} The database didn’t answer in time. "
                + (partlyRun ? StatementsMayHaveRun : "Check the connection, then try again.");
        }

        if (Find<AuthenticationException>(exception) is not null
            || provider?.Kind == DatabaseProviderFailureKind.SecureConnection)
        {
            return $"{WhatHappened(operation)} Asura couldn’t set up a secure connection. Check the connection’s TLS settings, then try again.";
        }

        if (Find<SocketException>(exception) is not null || provider?.Kind == DatabaseProviderFailureKind.Unreachable)
        {
            return operation == DatabaseOperation.Connect
                ? "Asura couldn’t reach the database server. Check the host and port and that the server is running, then connect again."
                : $"{WhatHappened(operation)} The connection to the database was lost. "
                    + (partlyRun ? StatementsMayHaveRun : "Reconnect, then try again.");
        }

        if (provider is not null && ServerReason(provider) is { } reason)
        {
            return $"{WhatHappened(operation)} The database said: {reason} "
                + (partlyRun ? StatementsMayHaveRun : NextStepForServerReason(operation));
        }

        return $"{WhatHappened(operation)} {(partlyRun ? StatementsMayHaveRun : NextStep(operation))}";
    }

    /// <summary>Why the workspace's network refused the connection, and how to lift it.</summary>
    internal const string KillSwitchBlocking =
        "The workspace’s network connection is down, and its kill switch is blocking traffic until it’s back. "
        + "Reconnect the workspace’s network, or turn off its kill switch in Edit Workspace, then try again.";

    /// <summary>Names the URL parameters the driver can't use, and what to do instead.</summary>
    internal static string DescribeUnsupportedParameters(UnsupportedConnectionParameterException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var names = refusal.Parameters
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Length > 40 ? string.Concat(name.AsSpan(0, 39), "…") : name)
            .Take(5)
            .ToList();
        if (names.Count == 0)
        {
            return $"{refusal.Engine} can’t use one of the connection URL’s parameters. "
                + "Write the connection as keyword=value pairs instead.";
        }

        var list = names.Count == refusal.Parameters.Count
            ? string.Join(", ", names)
            : string.Join(", ", names) + ", and others";
        return names.Count == 1 && refusal.Parameters.Count == 1
            ? $"{refusal.Engine} can’t use the connection URL’s {list} parameter. Remove it from the URL, "
                + "or write the connection as keyword=value pairs instead."
            : $"{refusal.Engine} can’t use the connection URL’s {list} parameters. Remove them from the URL, "
                + "or write the connection as keyword=value pairs instead.";
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
    /// The server's own reason, on one line and bounded, with the line and
    /// position the server named; null when the failure didn't come from the
    /// database server or the worker withheld its text.
    /// </summary>
    private static string? ServerReason(DatabaseProviderFailureException failure)
    {
        var reason = failure.ServerReason?.Split('\n', 2)[0].Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return null;
        }

        if (reason.Length > MaximumReasonCharacters)
        {
            reason = string.Concat(reason.AsSpan(0, MaximumReasonCharacters - 1), "…");
        }

        var location = new List<string>(2);
        if (failure.Line is { } line && line > 0)
        {
            location.Add("line " + line.ToString(CultureInfo.InvariantCulture));
        }

        if (failure.Position is { } position && position > 0)
        {
            location.Add("position " + position.ToString(CultureInfo.InvariantCulture));
        }

        if (location.Count > 0)
        {
            return $"{reason.TrimEnd('.')} ({string.Join(", ", location)}).";
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
