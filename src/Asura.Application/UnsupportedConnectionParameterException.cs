namespace Asura.Application;

/// <summary>
/// A connection URL names parameters this build's driver for
/// <see cref="Engine"/> can't apply. The message is written for people and
/// names the parameters, so the person knows what to remove.
/// </summary>
public sealed class UnsupportedConnectionParameterException : ArgumentException
{
    public UnsupportedConnectionParameterException()
        : this("database", [])
    {
    }

    public UnsupportedConnectionParameterException(string message)
        : base(message)
    {
        Engine = "database";
        Parameters = [];
    }

    public UnsupportedConnectionParameterException(string message, Exception innerException)
        : base(message, innerException)
    {
        Engine = "database";
        Parameters = [];
    }

    public UnsupportedConnectionParameterException(string engine, IReadOnlyList<string> parameters)
        : base(Describe(engine, parameters))
    {
        Engine = engine;
        Parameters = parameters;
    }

    /// <summary>The engine's display name, such as "PostgreSQL".</summary>
    public string Engine { get; }

    /// <summary>The URL parameter names the driver didn't accept.</summary>
    public IReadOnlyList<string> Parameters { get; }

    private static string Describe(string engine, IReadOnlyList<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.Count == 0
            ? $"This build's {engine} driver can't use a parameter in the URL. Write the connection out as keyword=value pairs instead."
            : $"This build's {engine} driver doesn't understand {string.Join(", ", parameters)}. Remove "
                + (parameters.Count == 1 ? "it" : "them")
                + " from the URL, or write the connection out as keyword=value pairs instead.";
    }
}
