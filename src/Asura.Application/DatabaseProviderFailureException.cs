namespace Asura.Application;

/// <summary>Where a provider failure inside the database worker came from.</summary>
public enum DatabaseProviderFailureKind
{
    /// <summary>The client library or Asura refused; its text is diagnostic.</summary>
    Client,

    /// <summary>The database server or embedded engine refused with its own reason.</summary>
    Server,

    /// <summary>The server could not be reached, or the connection to it broke.</summary>
    Unreachable,

    /// <summary>The server did not answer in time.</summary>
    TimedOut,

    /// <summary>The secure (TLS) connection could not be set up.</summary>
    SecureConnection,
}

/// <summary>
/// A provider failure after the worker dispatched the operation. Statements
/// before the failing one may have completed. <see cref="Exception.Message"/>
/// is the diagnostic text; the fields carry what a person can be shown.
/// </summary>
public class DatabaseProviderFailureException : IOException
{
    /// <summary>The reason text when the worker can't tell which values are connection secrets.</summary>
    public const string ReasonOmittedWithoutSecretExtraction =
        "Diagnostic text omitted because connection-secret extraction is unavailable for this target.";

    /// <summary>The reason text when the provider's message is too large to keep.</summary>
    public const string ReasonOmittedForSize =
        "Diagnostic text omitted because the database message exceeds 8 KiB.";

    public DatabaseProviderFailureException()
        : this("The database provider refused the operation.")
    {
    }

    public DatabaseProviderFailureException(string message)
        : this(message, string.Empty, DatabaseProviderFailureKind.Client, line: null, position: null)
    {
    }

    public DatabaseProviderFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = string.Empty;
    }

    public DatabaseProviderFailureException(
        string message,
        string reason,
        DatabaseProviderFailureKind kind,
        int? line,
        int? position)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(reason);
        Reason = reason;
        Kind = kind;
        Line = line;
        Position = position;
    }

    /// <summary>The provider's message with connection secrets removed, or an omission placeholder.</summary>
    public string Reason { get; }

    public DatabaseProviderFailureKind Kind { get; }

    /// <summary>The statement line the server named, when it named one.</summary>
    public int? Line { get; }

    /// <summary>The character position in the statement the server named, when it named one.</summary>
    public int? Position { get; }

    /// <summary>
    /// The server's own reason, or null when the failure didn't come from the
    /// server or its text was withheld.
    /// </summary>
    public string? ServerReason => Kind == DatabaseProviderFailureKind.Server
        && !string.IsNullOrWhiteSpace(Reason)
        && Reason is not (ReasonOmittedWithoutSecretExtraction or ReasonOmittedForSize)
            ? Reason
            : null;
}
