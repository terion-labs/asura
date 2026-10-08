using System.Globalization;
using System.Net;
using Asura.Application;

namespace Asura.Files;

/// <summary>
/// Sentences for failures reported by an HTTP file service (WebDAV, S3). HTTP
/// libraries and SDKs word their messages for developers and can quote URLs,
/// so the message people read comes from here, and the status or exception
/// goes to secret-safe diagnostics. The Files panel adds what to do next from
/// the error code.
/// </summary>
internal static class RemoteServiceFailureText
{
    /// <summary>
    /// Describes an HTTP status from <paramref name="service"/>, such as
    /// "the WebDAV server", and records the status in diagnostics.
    /// </summary>
    public static string ForStatus(
        string service,
        string diagnosticPrefix,
        HttpStatusCode status,
        bool targetMustNotExist = false)
    {
        var statusNumber = (int)status;
        SecretSafeDiagnosticProjection.WriteTrace(
            string.Create(CultureInfo.InvariantCulture, $"{diagnosticPrefix}.http-{statusNumber}"),
            SecretSafeDiagnosticKind.Unexpected);
        var subject = Capitalize(service);
        return status switch
        {
            HttpStatusCode.PreconditionFailed when targetMustNotExist =>
                $"An item with that name already exists on {service}.",
            HttpStatusCode.PreconditionFailed =>
                $"The item changed on {service} after Asura last read it.",
            HttpStatusCode.BadRequest => $"{subject} didn't accept this location or request.",
            HttpStatusCode.Unauthorized => $"{subject} didn't accept the sign-in details.",
            HttpStatusCode.Forbidden => $"{subject} doesn't allow this action for this account.",
            HttpStatusCode.NotFound => $"The item isn't on {service} anymore.",
            HttpStatusCode.MethodNotAllowed => $"{subject} doesn't support this action.",
            HttpStatusCode.Conflict =>
                $"{subject} couldn't make the change because something else changed at the same time, or a parent folder is missing.",
            HttpStatusCode.RequestedRangeNotSatisfiable => $"{subject} couldn't return that part of the file.",
            (HttpStatusCode)423 => $"The item is locked on {service}.",
            (HttpStatusCode)507 => $"{subject} is out of storage space.",
            (HttpStatusCode)207 => $"{subject} finished only part of the change.",
            HttpStatusCode.TooManyRequests => $"{subject} is busy and asked Asura to wait.",
            >= HttpStatusCode.InternalServerError => $"{subject} had a problem and couldn't complete the request.",
            _ => $"{subject} couldn't complete the request.",
        };
    }

    /// <summary>
    /// Describes a transport failure talking to <paramref name="service"/> and
    /// records the exception in diagnostics.
    /// </summary>
    public static string ForException(string service, string diagnosticPrefix, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SecretSafeDiagnosticProjection.WriteTrace($"{diagnosticPrefix}.request-failed", exception);
        var subject = Capitalize(service);
        return exception switch
        {
            OperationCanceledException or TimeoutException => $"{subject} didn't respond in time.",
            EndOfStreamException => $"The connection to {service} closed before the transfer finished.",
            System.Xml.XmlException => $"{subject} sent a reply Asura couldn't read.",
            HttpRequestException => $"Asura couldn't reach {service}.",
            _ => $"The connection to {service} was interrupted.",
        };
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
