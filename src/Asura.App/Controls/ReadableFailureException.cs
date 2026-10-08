namespace Asura.App.Controls;

/// <summary>
/// A refusal whose message was written for the person using Asura: it says
/// what went wrong in plain words and, where there is one, what to do next.
/// <see cref="FailureReason"/> shows this message as is; every other exception
/// message is treated as developer text and replaced.
/// </summary>
internal sealed class ReadableFailureException : Exception
{
    public ReadableFailureException()
    {
    }

    public ReadableFailureException(string message)
        : base(message)
    {
    }

    public ReadableFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
