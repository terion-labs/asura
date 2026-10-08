using System.Net.Sockets;

namespace Asura.App.ViewModels;

/// <summary>
/// A Redis failure as a person reads it: what went wrong and what to do
/// next. The client library's own text ("It was not possible to connect to
/// the redis server(s). ConnectTimeout") goes to diagnostics, not the panel;
/// a reason the server itself gave — a permission or a query syntax error —
/// is kept after the curated sentence, because only the server can say it.
/// </summary>
internal static class RedisErrorText
{
    private const string CuratedKey = "Asura.Redis.CuratedMessage";

    /// <summary>
    /// Marks an exception the panel threw itself with words meant for the
    /// person ("Enter a field name."), so it is shown as written.
    /// </summary>
    public static TException Curated<TException>(TException exception)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[CuratedKey] = true;
        return exception;
    }

    private static bool IsCurated(Exception exception) => exception.Data.Contains(CuratedKey);

    public static string ForConnection(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SecretSafeDiagnostics.WriteTrace("redis.connect.failed", exception);
        return Classify(exception) switch
        {
            Failure.Unreachable => "Asura couldn’t reach the Redis server. Check that it’s running and that the host and port are correct, then try again.",
            Failure.TimedOut => "The Redis server didn’t answer in time. Check the connection and try again.",
            Failure.Tls => "Asura couldn’t set up a secure connection to the Redis server. Check the connection’s TLS settings, then try again.",
            Failure.Permission => WithServerReason("This Redis user isn’t allowed to connect. Check the user name, or ask the server’s administrator for access.", exception),
            Failure.Server => WithServerReason("The Redis server refused the connection. Check the connection settings, then try again.", exception),
            _ => "Asura couldn’t connect to the Redis server. Check the connection settings, then try again.",
        };
    }

    public static string ForCommand(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsCurated(exception)) { return exception.Message; }
        SecretSafeDiagnostics.WriteTrace("redis.command.failed", exception);
        return Classify(exception) switch
        {
            Failure.Unreachable => "The connection to the Redis server was lost. Reconnect, then try again.",
            Failure.TimedOut => "The Redis server didn’t answer in time. Try again, or narrow the request.",
            Failure.Tls => "The secure connection to the Redis server failed. Reconnect, then try again.",
            Failure.Permission => WithServerReason("This Redis user isn’t allowed to run that command. Ask the server’s administrator for access, or connect as another user.", exception),
            Failure.WrongType => "That key now holds a different type of value. Refresh the key, then try again.",
            Failure.ReadOnly => "This Redis server is read-only. Connect to the primary to make changes.",
            Failure.OutOfMemory => "The Redis server is out of memory and refused the write. Free some memory on the server, then try again.",
            Failure.Server => WithServerReason("Redis refused the command. Check the values you entered, then try again.", exception),
            _ => "Asura couldn’t complete that Redis command. Try again; if it keeps failing, reconnect.",
        };
    }

    private enum Failure
    {
        Other,
        Unreachable,
        TimedOut,
        Tls,
        Permission,
        WrongType,
        ReadOnly,
        OutOfMemory,
        Server,
    }

    private static Failure Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.StartsWith("NOPERM", StringComparison.Ordinal)
                || message.Contains("no permissions", StringComparison.OrdinalIgnoreCase))
            {
                return Failure.Permission;
            }

            if (message.StartsWith("WRONGTYPE", StringComparison.Ordinal)) { return Failure.WrongType; }
            if (message.StartsWith("READONLY", StringComparison.Ordinal)) { return Failure.ReadOnly; }
            if (message.StartsWith("OOM", StringComparison.Ordinal)) { return Failure.OutOfMemory; }
            if (current is TimeoutException || current.GetType().Name.Contains("Timeout", StringComparison.Ordinal))
            {
                return Failure.TimedOut;
            }

            if (current is System.Security.Authentication.AuthenticationException) { return Failure.Tls; }
            if (current is SocketException or IOException
                || current.GetType().Name.Contains("RedisConnectionException", StringComparison.Ordinal))
            {
                return Failure.Unreachable;
            }

            if (current.GetType().Name.Contains("RedisServerException", StringComparison.Ordinal)) { return Failure.Server; }
        }

        return Failure.Other;
    }

    /// <summary>
    /// The server's reason after the curated sentence, on one line and
    /// bounded: the reply is the server's own words, not the client's.
    /// </summary>
    private static string WithServerReason(string curated, Exception exception)
    {
        var reason = exception.Message.Split('\n', 2)[0].Trim();
        if (reason.Length == 0) { return curated; }
        if (reason.Length > 200) { reason = string.Concat(reason.AsSpan(0, 199), "…"); }
        return $"{curated} Redis said: {reason}";
    }
}
