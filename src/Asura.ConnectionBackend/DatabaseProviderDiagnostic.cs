using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text;
using Asura.Application;

namespace Asura.ConnectionBackend;

internal sealed record DatabaseProviderDiagnostic(string Message, string? SqlState, int? Number, int? Line, int? Position,
    DatabaseProviderFailureKind Kind = DatabaseProviderFailureKind.Client)
{
    internal static DatabaseProviderDiagnostic Create(Exception exception, DatabaseWorkerConnection target,
        IDatabaseConnectionCatalog catalog)
    {
        var state = (exception as DbException)?.SqlState;
        if (state?.Length != 5 || !state.All(char.IsAsciiLetterOrDigit)) { state = null; }
        var number = exception switch
        {
            Microsoft.Data.SqlClient.SqlException sql => sql.Number,
            Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteExtendedErrorCode,
            MySqlConnector.MySqlException mysql => mysql.Number,
            DbException database => (int?)database.ErrorCode,
            _ => null,
        };
        var line = exception is Microsoft.Data.SqlClient.SqlException server && server.LineNumber > 0 ? server.LineNumber : (int?)null;
        var position = exception is Npgsql.PostgresException postgres && postgres.Position > 0 ? postgres.Position : (int?)null;
        var message = exception is Npgsql.PostgresException reported ? reported.MessageText : exception.Message;
        return new(Sanitize(message, target, catalog), state, number, line, position, Classify(exception));
    }

    /// <summary>
    /// Whether the text is the server's own reason, a transport failure, or
    /// the client library talking. Only a server's reason is shown to people;
    /// a client library's text ("Exception while reading from stream") is not
    /// something the database said.
    /// </summary>
    internal static DatabaseProviderFailureKind Classify(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case TimeoutException:
                    return DatabaseProviderFailureKind.TimedOut;
                case System.Security.Authentication.AuthenticationException:
                    return DatabaseProviderFailureKind.SecureConnection;
                case System.Net.Sockets.SocketException or IOException:
                    return DatabaseProviderFailureKind.Unreachable;
            }
        }

        return exception switch
        {
            Npgsql.PostgresException => DatabaseProviderFailureKind.Server,
            Npgsql.NpgsqlException => DatabaseProviderFailureKind.Client,
            // Severity 20 and above are fatal connection-level failures, not a
            // reason about the statement or the login.
            Microsoft.Data.SqlClient.SqlException sql => sql.Class is > 0 and < 20
                ? DatabaseProviderFailureKind.Server
                : DatabaseProviderFailureKind.Client,
            // MySqlConnector sets SQLSTATE only from a server error packet.
            MySqlConnector.MySqlException mysql => mysql.SqlState is null
                ? DatabaseProviderFailureKind.Client
                : DatabaseProviderFailureKind.Server,
            DbException => DatabaseProviderFailureKind.Server,
            _ => DatabaseProviderFailureKind.Client,
        };
    }

    private static string Sanitize(string message, DatabaseWorkerConnection target, IDatabaseConnectionCatalog catalog)
    {
        // A database-reported message is useful query feedback, not a closed log
        // record. Unknown connection syntax has no raw-message fallback.
        if (!TryGetSecrets(target, catalog, out var secrets))
        {
            return DatabaseProviderFailureException.ReasonOmittedWithoutSecretExtraction;
        }
        if (message.Length > 8192 || Encoding.UTF8.GetByteCount(message) > 8192)
        {
            return DatabaseProviderFailureException.ReasonOmittedForSize;
        }
        foreach (var secret in secrets.OrderByDescending(value => value.Length))
        {
            message = message.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }
        return message;
    }

    private static bool TryGetSecrets(DatabaseWorkerConnection target, IDatabaseConnectionCatalog catalog,
        out HashSet<string> secrets)
    {
        secrets = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (!catalog.IsConnectionStringValid(target.DriverId, target.ConnectionString)) { return false; }
            AddSecret(secrets, target.ConnectionString);
            AddSecret(secrets, catalog.ParseConnectionDetails(target.DriverId, target.ConnectionString).Password);
            if (target.ConnectionString.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate(target.ConnectionString, UriKind.Absolute, out var uri))
            {
                var separator = uri.UserInfo.IndexOf(':', StringComparison.Ordinal);
                if (separator >= 0) { AddSecret(secrets, Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..])); }
                foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Split('=', 2);
                    if (pair.Length != 2) { return false; }
                    if (IsSecretKey(Uri.UnescapeDataString(pair[0]))) { AddSecret(secrets, Uri.UnescapeDataString(pair[1])); }
                }
                return true;
            }
            var builder = new DbConnectionStringBuilder { ConnectionString = target.ConnectionString };
            foreach (string key in builder.Keys)
            {
                if (IsSecretKey(key)) { AddSecret(secrets, Convert.ToString(builder[key], CultureInfo.InvariantCulture)); }
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException or InvalidOperationException or DbException)
        {
            return false;
        }
    }

    private static bool IsSecretKey(string key)
    {
        var normalized = string.Concat(key.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized is "pwd" or "key"
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("passphrase", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("accesskey", StringComparison.Ordinal);
    }

    private static void AddSecret(HashSet<string> secrets, string? value)
    {
        if (string.IsNullOrEmpty(value)) { return; }
        secrets.Add(value);
        secrets.Add(Uri.EscapeDataString(value));
        secrets.Add(WebUtility.UrlEncode(value));
        secrets.Add(System.Text.Json.JsonEncodedText.Encode(value).ToString());
        secrets.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
    }

    internal string DisplayMessage()
    {
        var fields = new List<string>();
        if (SqlState is not null) { fields.Add("SQLSTATE " + SqlState); }
        if (Number is not null) { fields.Add("code " + Number.Value.ToString(CultureInfo.InvariantCulture)); }
        if (Line is not null) { fields.Add("line " + Line.Value.ToString(CultureInfo.InvariantCulture)); }
        if (Position is not null) { fields.Add("position " + Position.Value.ToString(CultureInfo.InvariantCulture)); }
        return "Database reported: " + Message + (fields.Count == 0 ? string.Empty : "\n" + string.Join(", ", fields))
            + "\nEarlier statements or side effects may have completed. Verify the database before retrying.";
    }
}

internal sealed class DatabaseProviderOperationException(DatabaseProviderDiagnostic diagnostic)
    : DatabaseProviderFailureException(diagnostic.DisplayMessage(), diagnostic.Message, diagnostic.Kind,
        diagnostic.Line, diagnostic.Position)
{
    internal DatabaseProviderDiagnostic Diagnostic { get; } = diagnostic;
}
