using System.Text.Json;
using System.Text.Unicode;
using Asura.Application;

namespace Asura.DesktopAutomation;

internal enum AgentDesktopEnvelopeKind
{
    /// <summary>Not an agent-desktop envelope Asura can act on.</summary>
    Invalid,
    Data,
    Error,
}

/// <summary>
/// The JSON envelope agent-desktop writes for a command:
/// <c>{"ok":true,"data":{…}}</c> or
/// <c>{"ok":false,"error":{"code":…,"disposition":{"delivery":…}}}</c>.
///
/// The envelope describes another application and is produced by a library
/// Asura does not control, so it is parsed defensively: anything that does not
/// have exactly this shape is <see cref="AgentDesktopEnvelopeKind.Invalid"/>,
/// which callers treat as an unknown outcome after a mutation.
/// </summary>
internal sealed class AgentDesktopEnvelope
{
    /// <summary>
    /// Largest envelope copied out of native memory. A full window tree is
    /// well under this; Asura's own 512-node projection bounds what the model
    /// sees afterwards.
    /// </summary>
    public const int MaximumBytes = 8 * 1024 * 1024;

    /// <summary>
    /// A snapshot tree nests two JSON levels per accessibility level, so the
    /// deepest tree Asura requests (32) needs about 70; the default of 64
    /// would reject it.
    /// </summary>
    private const int MaximumJsonDepth = 128;

    public static readonly AgentDesktopEnvelope Invalid = new(
        AgentDesktopEnvelopeKind.Invalid,
        default,
        errorCode: null,
        AgentDesktopDelivery.Unknown);

    private AgentDesktopEnvelope(
        AgentDesktopEnvelopeKind kind,
        JsonElement data,
        string? errorCode,
        AgentDesktopDelivery delivery)
    {
        Kind = kind;
        Data = data;
        ErrorCode = errorCode;
        Delivery = delivery;
    }

    public AgentDesktopEnvelopeKind Kind { get; }

    /// <summary>The <c>data</c> object of a successful command; undefined otherwise.</summary>
    public JsonElement Data { get; }

    /// <summary>Upstream's stable error code, such as <c>STALE_REF</c>.</summary>
    public string? ErrorCode { get; }

    /// <summary>What the library reported about input delivery for this command.</summary>
    public AgentDesktopDelivery Delivery { get; }

    public static AgentDesktopEnvelope Error(string code, AgentDesktopDelivery delivery) =>
        new(AgentDesktopEnvelopeKind.Error, default, code, delivery);

    public static AgentDesktopEnvelope Parse(ReadOnlyMemory<byte> utf8)
    {
        // The JSON reader checks structure only and decodes a string when it
        // is asked for it, so malformed text would otherwise surface as an
        // exception from whichever field is read first.
        if (!Utf8.IsValid(utf8.Span))
        {
            return Invalid;
        }

        try
        {
            using var document = JsonDocument.Parse(
                utf8,
                new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
            return FromRoot(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: an escaped lone surrogate in the
            // error code or the delivery, which no string can hold.
            return Invalid;
        }
    }

    /// <summary>
    /// Parses the envelope an entrypoint wrote next to <paramref name="status"/>.
    /// A status that contradicts its envelope (success with an error payload,
    /// or a failure with data) is treated as no answer at all.
    /// </summary>
    public static AgentDesktopEnvelope Parse(ReadOnlyMemory<byte> utf8, int status)
    {
        var envelope = Parse(utf8);
        var agrees = envelope.Kind switch
        {
            AgentDesktopEnvelopeKind.Data => status == AdResult.Ok,
            AgentDesktopEnvelopeKind.Error => status != AdResult.Ok,
            _ => true,
        };
        return agrees ? envelope : Invalid;
    }

    private static AgentDesktopEnvelope FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok))
        {
            return Invalid;
        }

        if (ok.ValueKind == JsonValueKind.True)
        {
            return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? new AgentDesktopEnvelope(
                    AgentDesktopEnvelopeKind.Data,
                    data.Clone(),
                    errorCode: null,
                    ReadDelivery(data))
                : Invalid;
        }

        if (ok.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var code)
            || code.ValueKind != JsonValueKind.String)
        {
            return Invalid;
        }

        return Error(ErrorCodeOf(code.GetString() ?? string.Empty, error), ReadDelivery(error));
    }

    /// <summary>
    /// The library answers ACTION_NOT_SUPPORTED, with the detail kind
    /// <c>window_without_accessibility_element</c>, for a window the window
    /// server lists but accessibility does not expose yet. A freshly launched
    /// application's window is in that state for a moment, so it is read as
    /// a window not found, which may be retried, rather than as an element
    /// that can never support the request.
    /// </summary>
    private static string ErrorCodeOf(string code, JsonElement error) =>
        string.Equals(code, "ACTION_NOT_SUPPORTED", StringComparison.Ordinal)
        && error.TryGetProperty("details", out var details)
        && details.ValueKind == JsonValueKind.Object
        && details.TryGetProperty("kind", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && string.Equals(kind.GetString(), "window_without_accessibility_element", StringComparison.Ordinal)
            ? "WINDOW_NOT_FOUND"
            : code;

    /// <summary>Reads <c>disposition.delivery</c>; anything unrecognized is unknown.</summary>
    private static AgentDesktopDelivery ReadDelivery(JsonElement parent)
    {
        if (!parent.TryGetProperty("disposition", out var disposition)
            || disposition.ValueKind != JsonValueKind.Object
            || !disposition.TryGetProperty("delivery", out var delivery)
            || delivery.ValueKind != JsonValueKind.String)
        {
            return AgentDesktopDelivery.Unknown;
        }

        return delivery.GetString() switch
        {
            "not_delivered" => AgentDesktopDelivery.NotDelivered,
            "delivery_uncertain" => AgentDesktopDelivery.Uncertain,
            "delivered_unverified" => AgentDesktopDelivery.DeliveredUnverified,
            "delivered_verified" => AgentDesktopDelivery.DeliveredVerified,
            _ => AgentDesktopDelivery.Unknown,
        };
    }

    /// <summary>
    /// The error code string for a bare <c>AdResult</c>, used by entrypoints
    /// that return a status without an envelope.
    /// </summary>
    public static string StatusCode(int status) => status switch
    {
        AdResult.PermissionDenied => "PERM_DENIED",
        AdResult.ElementNotFound => "ELEMENT_NOT_FOUND",
        AdResult.AppNotFound => "APP_NOT_FOUND",
        AdResult.ActionFailed => "ACTION_FAILED",
        AdResult.ActionNotSupported => "ACTION_NOT_SUPPORTED",
        AdResult.StaleRef => "STALE_REF",
        AdResult.WindowNotFound => "WINDOW_NOT_FOUND",
        AdResult.PlatformNotSupported => "PLATFORM_NOT_SUPPORTED",
        AdResult.Timeout => "TIMEOUT",
        AdResult.InvalidArguments => "INVALID_ARGS",
        AdResult.NotificationNotFound => "NOTIFICATION_NOT_FOUND",
        AdResult.Internal => InternalCode,
        AdResult.SnapshotNotFound => "SNAPSHOT_NOT_FOUND",
        AdResult.PolicyDenied => "POLICY_DENIED",
        AdResult.AmbiguousTarget => "AMBIGUOUS_TARGET",
        AdResult.AppUnresponsive => "APP_UNRESPONSIVE",
        _ => "ACTION_FAILED",
    };

    /// <summary>The code that poisons the run's adapter; it has no failure-code mapping.</summary>
    public const string InternalCode = "INTERNAL";

    public const string TimeoutCode = "TIMEOUT";

    /// <summary>
    /// Maps upstream's code onto Asura's closed set. An unrecognized code is a
    /// plain failed action: a newer library must not be able to mint a new
    /// stable code in Asura's audit.
    /// </summary>
    public static AgentDesktopFailureCode MapErrorCode(string? code) => code switch
    {
        "PERM_DENIED" => AgentDesktopFailureCode.PermissionDenied,
        "ELEMENT_NOT_FOUND" => AgentDesktopFailureCode.ElementNotFound,
        "APP_NOT_FOUND" => AgentDesktopFailureCode.AppNotFound,
        "ACTION_NOT_SUPPORTED" => AgentDesktopFailureCode.ActionNotSupported,
        "STALE_REF" => AgentDesktopFailureCode.StaleRef,
        "AMBIGUOUS_TARGET" => AgentDesktopFailureCode.AmbiguousTarget,
        "WINDOW_NOT_FOUND" => AgentDesktopFailureCode.WindowNotFound,
        "PLATFORM_NOT_SUPPORTED" => AgentDesktopFailureCode.Unavailable,
        TimeoutCode or "APP_UNRESPONSIVE" => AgentDesktopFailureCode.Timeout,
        "INVALID_ARGS" => AgentDesktopFailureCode.InvalidArguments,
        "SNAPSHOT_NOT_FOUND" => AgentDesktopFailureCode.SnapshotNotFound,
        "POLICY_DENIED" => AgentDesktopFailureCode.PolicyDenied,
        _ => AgentDesktopFailureCode.ActionFailed,
    };
}
