using System.Buffers;
using System.Text;
using System.Text.Json;
using Asura.Application;

namespace Asura.Agent.Runtime;

/// <summary>
/// Projects desktop results into the bounded JSON the provider sees. Text
/// read from another application arrives here already redacted and bounded,
/// and is written only as string values of data fields, never as property
/// names or prose. Listings and snapshots are trimmed to fit the result
/// budget and say so with <c>truncated</c>.
/// </summary>
internal static class DesktopAgentToolResultJson
{
    internal const string ContentOrigin = "untrusted_desktop";
    internal const string ObservationCompletedStableCode = "desktop_observation_completed";
    internal const string ActionCompletedStableCode = "desktop_action_completed";

    /// <summary>The ceiling for one result, measured as the escaped JSON (ADR 0061).</summary>
    internal const int MaximumResultBytes = 64 * 1024;

    private const string SnapshotRequiredAction = "desktop_snapshot";
    private const string AuthorizationDeadlineStableCode = "deadline_exceeded";

    public static DesktopAgentToolJsonProjection Project(
        AgentDesktopRequest request,
        AgentDesktopResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        return (request, result) switch
        {
            (AgentDesktopRequest.ListApps, AgentDesktopResult.AppList apps) =>
                Completed(request, Apps(apps)),
            (AgentDesktopRequest.ListWindows, AgentDesktopResult.WindowList windows) =>
                Completed(request, Windows(windows)),
            (AgentDesktopRequest.Snapshot, AgentDesktopResult.Snapshot snapshot) =>
                Completed(request, Snapshot(snapshot)),
            (AgentDesktopRequest.Wait, AgentDesktopResult.WaitCompleted wait) =>
                Completed(request, Wait(wait)),

            // An element action succeeded when its input was delivered, with
            // "verified" saying whether the library observed the effect: a
            // plain button press never can be. It also succeeded when the
            // library found the element already in the requested state and
            // delivered nothing. Only an uncertain or unknown delivery leaves
            // the outcome open. Focusing a window and asking an application to
            // quit can only ever report that the request was made (ADR 0061).
            (AgentDesktopRequest.ElementAction, AgentDesktopResult.ActionDelivered delivered) =>
                IsDelivered(delivered.Delivery)
                    ? Completed(request, Delivered(delivered))
                    : delivered.Delivery == AgentDesktopDelivery.NotDelivered
                        ? Completed(request, AlreadySatisfied(delivered))
                        : Failed(AgentDesktopStableCodes.ActionOutcomeUnknown),
            (AgentDesktopRequest.FocusWindow, AgentDesktopResult.ActionDelivered delivered) =>
                IsDelivered(delivered.Delivery)
                    ? Completed(request, Delivered(delivered))
                    : Failed(AgentDesktopStableCodes.ActionOutcomeUnknown),
            (AgentDesktopRequest.LaunchApp, AgentDesktopResult.AppLaunched launched) =>
                Completed(request, Launched(launched)),
            (AgentDesktopRequest.CloseApp, AgentDesktopResult.AppCloseRequested closed) =>
                IsDelivered(closed.Delivery)
                    ? Completed(request, CloseRequested(closed))
                    : Failed(AgentDesktopStableCodes.ActionOutcomeUnknown),

            // A result of the wrong shape proves nothing about what happened.
            _ => Failed(UnexplainedFailure(request)),
        };
    }

    public static string Failure(string stableCode, bool retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableCode);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteBoolean("ok", false);
        AgentToolResultJson.WriteError(writer, "error", stableCode, retryable);
        if (RequiresFreshSnapshot(stableCode))
        {
            writer.WriteString("required_action", SnapshotRequiredAction);
        }

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// The code the provider is told for a host failure. The host reports the
    /// closed desktop codes; anything else is translated, and for a mutation
    /// an unexplained failure is an unknown outcome, never a clean one.
    /// </summary>
    public static string ProviderStableCode(HostError error, AgentDesktopRequest request)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(request);
        if (IsPassedThrough(error.StableCode))
        {
            return error.StableCode;
        }

        return error.Code switch
        {
            // Refused while binding the authorization, before any dispatch.
            HostErrorCode.InvalidRequest or HostErrorCode.RevisionConflict => "target_changed",
            HostErrorCode.Cancelled when !request.IsMutation => AgentDesktopStableCodes.Cancelled,

            // How every family's host reports a one-action authorization that
            // expired before it was consumed: nothing was dispatched. A
            // timeout of the action itself always carries a desktop code.
            HostErrorCode.DeadlineExceeded
                when string.Equals(error.StableCode, AuthorizationDeadlineStableCode, StringComparison.Ordinal) =>
                AuthorizationDeadlineStableCode,
            _ => UnexplainedFailure(request),
        };
    }

    /// <summary>
    /// The code for a failure the host did not explain: an observation simply
    /// failed, while a mutation may have reached the application.
    /// </summary>
    public static string UnexplainedFailure(AgentDesktopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.IsMutation
            ? AgentDesktopStableCodes.ActionOutcomeUnknown
            : AgentDesktopStableCodes.ActionFailed;
    }

    private static bool IsPassedThrough(string stableCode) => stableCode is
        AgentDesktopStableCodes.Unavailable
        or AgentDesktopStableCodes.PermissionDenied
        or AgentDesktopStableCodes.SelfTarget
        or AgentDesktopStableCodes.SelfTargetLaunched
        or AgentDesktopStableCodes.AppNotFound
        or AgentDesktopStableCodes.WindowNotFound
        or AgentDesktopStableCodes.StaleRef
        or AgentDesktopStableCodes.AmbiguousTarget
        or AgentDesktopStableCodes.TimeoutBeforeDelivery
        or AgentDesktopStableCodes.PolicyDenied
        or AgentDesktopStableCodes.ActionFailed
        or AgentDesktopStableCodes.NotSupported
        or AgentDesktopStableCodes.InvalidArguments
        or AgentDesktopStableCodes.SecureField
        or AgentDesktopStableCodes.ActionOutcomeUnknown
        or AgentDesktopStableCodes.AdapterFaulted
        or AgentDesktopStableCodes.Cancelled
        or AgentActionFailureCodes.CompletionAuditUnavailable

        // The runtime renews an expired one-action approval from the original
        // proposal, re-resolving the target and asking again; that only works
        // when the expiry keeps the code the broker's denial has.
        or "approval_expired"
        or "authorization_expired";

    private static bool RequiresFreshSnapshot(string stableCode) =>
        stableCode is AgentDesktopStableCodes.StaleRef
        || AgentDesktopStableCodes.RequiresReconciliation(stableCode);

    private static bool IsDelivered(AgentDesktopDelivery delivery) =>
        delivery is AgentDesktopDelivery.DeliveredVerified or AgentDesktopDelivery.DeliveredUnverified;

    private static BoundedListJson Apps(AgentDesktopResult.AppList apps) =>
        BoundedList(
            "apps",
            apps.Apps.Count,
            apps.Truncated,
            static _ => { },
            (writer, index) =>
            {
                var app = apps.Apps[index];
                writer.WriteStartObject();
                writer.WriteString("name", app.Name.Text);
                writer.WriteNumber("pid", app.ProcessId);
                WriteOptional(writer, "bundle_id", app.BundleId);
                writer.WriteEndObject();
            });

    private static BoundedListJson Windows(AgentDesktopResult.WindowList windows) =>
        BoundedList(
            "windows",
            windows.Windows.Count,
            windows.Truncated,
            static _ => { },
            (writer, index) =>
            {
                var window = windows.Windows[index];
                writer.WriteStartObject();

                // Only a window the ledger can bind gets an identifier; the
                // rest are shown so the listing stays truthful. The time is
                // not part of that answer.
                if (DesktopAgentObservationLedger.WindowTargetFor(window, observedAt: default) is { } target)
                {
                    writer.WriteString("window_id", target.WindowId);
                }

                writer.WriteString("title", window.Title.Text);
                writer.WriteString("app", window.Application.Text);
                writer.WriteNumber("pid", window.ProcessId);
                writer.WriteBoolean("focused", window.IsFocused);
                writer.WriteEndObject();
            });

    private static BoundedListJson Snapshot(AgentDesktopResult.Snapshot snapshot)
    {
        var nodeCount = Math.Min(snapshot.Nodes.Count, AgentDesktopResult.MaximumSnapshotNodes);
        return BoundedList(
            "nodes",
            nodeCount,
            snapshot.NodesTruncated || nodeCount < snapshot.Nodes.Count,
            writer =>
            {
                writer.WriteString("snapshot_id", snapshot.SnapshotId);
                writer.WriteString("app", snapshot.Application.Text);
                writer.WriteNumber("pid", snapshot.ProcessId);
                WriteOptional(writer, "window_title", snapshot.WindowTitle);
                writer.WriteBoolean("complete", snapshot.Complete);
                writer.WriteBoolean("identity_unproven", snapshot.IdentityUnproven);
                writer.WriteNumber("ref_count", snapshot.RefCount);
                writer.WriteNumber(
                    "refs_expire_after_seconds",
                    (int)DesktopAgentObservationLedger.ObservationLifetime.TotalSeconds);
            },
            (writer, index) => WriteNode(writer, snapshot.Nodes[index]));
    }

    private static void WriteNode(Utf8JsonWriter writer, AgentDesktopSnapshotNode node)
    {
        writer.WriteStartObject();
        writer.WriteNumber("depth", node.Depth);
        writer.WriteString("role", node.Role.Text);
        WriteOptional(writer, "name", node.Name);
        WriteOptional(writer, "value", node.Value);
        if (node.Reference is { } reference)
        {
            writer.WriteString("ref", reference.Value);
        }

        WriteStates(writer, node.States);
        if (node.IsSecure)
        {
            writer.WriteBoolean("secure", true);
        }

        if (node.SubtreeTruncated)
        {
            writer.WriteBoolean("subtree_truncated", true);
        }

        writer.WriteEndObject();
    }

    private static string Wait(AgentDesktopResult.WaitCompleted wait) =>
        Single(writer =>
        {
            writer.WriteBoolean("satisfied", wait.Satisfied);
            writer.WriteNumber("elapsed_ms", wait.ElapsedMilliseconds);
        });

    private static string Delivered(AgentDesktopResult.ActionDelivered delivered) =>
        Single(writer =>
        {
            WriteDelivery(writer, delivered.Delivery);
            WritePostState(writer, delivered);

            // The ledger has already expired this window's refs.
            writer.WriteString("required_action", SnapshotRequiredAction);
        });

    /// <summary>
    /// An element action that needed nothing done. No input reached the
    /// application, so the window's refs stay valid and no snapshot is asked for.
    /// </summary>
    private static string AlreadySatisfied(AgentDesktopResult.ActionDelivered delivered) =>
        Single(writer =>
        {
            WriteDelivery(writer, delivered.Delivery);
            writer.WriteBoolean("already_in_state", true);
            WritePostState(writer, delivered);
        });

    private static void WritePostState(Utf8JsonWriter writer, AgentDesktopResult.ActionDelivered delivered)
    {
        if (delivered.PostRole is null && delivered.PostValue is null && delivered.PostStates.Count == 0)
        {
            return;
        }

        writer.WriteString("content_origin", ContentOrigin);
        writer.WriteStartObject("post_state");
        WriteOptional(writer, "role", delivered.PostRole);
        WriteOptional(writer, "value", delivered.PostValue);
        WriteStates(writer, delivered.PostStates);
        writer.WriteEndObject();
    }

    private static string Launched(AgentDesktopResult.AppLaunched launched) =>
        Single(writer =>
        {
            writer.WriteNumber("pid", launched.ProcessId);
            if (launched.WindowTitle is { } title)
            {
                writer.WriteString("content_origin", ContentOrigin);
                writer.WriteString("window_title", title.Text);
            }
        });

    private static string CloseRequested(AgentDesktopResult.AppCloseRequested closed) =>
        Single(writer =>
        {
            WriteDelivery(writer, closed.Delivery);
            writer.WriteBoolean("close_requested", true);
        });

    private static void WriteDelivery(Utf8JsonWriter writer, AgentDesktopDelivery delivery)
    {
        writer.WriteString(
            "delivery",
            delivery switch
            {
                AgentDesktopDelivery.DeliveredVerified => "delivered_verified",
                AgentDesktopDelivery.NotDelivered => "not_delivered",
                _ => "delivered_unverified",
            });
        writer.WriteBoolean("verified", delivery == AgentDesktopDelivery.DeliveredVerified);
    }

    private static void WriteStates(Utf8JsonWriter writer, IReadOnlyList<string> states)
    {
        if (states.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("states");
        foreach (var state in states)
        {
            writer.WriteStringValue(state);
        }

        writer.WriteEndArray();
    }

    private static void WriteOptional(
        Utf8JsonWriter writer,
        string name,
        AgentDesktopDisplayText? text)
    {
        if (text is not null)
        {
            writer.WriteString(name, text.Text);
        }
    }

    private static string Single(Action<Utf8JsonWriter> writeBody)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteBoolean("ok", true);
        writeBody(writer);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenCount <= MaximumResultBytes
            ? Encoding.UTF8.GetString(buffer.WrittenSpan)
            : throw new InvalidOperationException("The desktop result exceeds its byte limit.");
    }

    /// <summary>
    /// Writes a result whose only unbounded part is one list, keeping the
    /// longest prefix of that list that fits the byte ceiling, and reports
    /// how long that prefix is.
    /// </summary>
    private static BoundedListJson BoundedList(
        string listName,
        int count,
        bool hostTruncated,
        Action<Utf8JsonWriter> writeHeader,
        Action<Utf8JsonWriter, int> writeItem)
    {
        var all = Serialize(count, hostTruncated);
        if (all.ByteCount <= MaximumResultBytes)
        {
            return new BoundedListJson(all.Json, count);
        }

        var best = Serialize(0, truncated: true);
        var bestCount = 0;
        var low = 1;
        var high = count - 1;
        while (low <= high)
        {
            var candidateCount = low + ((high - low) / 2);
            var candidate = Serialize(candidateCount, truncated: true);
            if (candidate.ByteCount <= MaximumResultBytes)
            {
                best = candidate;
                bestCount = candidateCount;
                low = candidateCount + 1;
            }
            else
            {
                high = candidateCount - 1;
            }
        }

        return best.ByteCount <= MaximumResultBytes
            ? new BoundedListJson(best.Json, bestCount)
            : throw new InvalidOperationException("The desktop result exceeds its byte limit.");

        (string Json, int ByteCount) Serialize(int itemCount, bool truncated)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteString("content_origin", ContentOrigin);
            writeHeader(writer);
            writer.WriteStartArray(listName);
            for (var index = 0; index < itemCount; index++)
            {
                writeItem(writer, index);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("truncated", truncated);
            writer.WriteEndObject();
            writer.Flush();
            return (Encoding.UTF8.GetString(buffer.WrittenSpan), buffer.WrittenCount);
        }
    }

    private static DesktopAgentToolJsonProjection Completed(AgentDesktopRequest request, BoundedListJson list) =>
        Completed(request, list.Json, list.ItemCount);

    private static DesktopAgentToolJsonProjection Completed(AgentDesktopRequest request, string json) =>
        Completed(request, json, listedItems: 0);

    private static DesktopAgentToolJsonProjection Completed(
        AgentDesktopRequest request,
        string json,
        int listedItems) =>
        new(
            IsSuccess: true,
            request.IsMutation ? ActionCompletedStableCode : ObservationCompletedStableCode,
            json,
            listedItems);

    private static DesktopAgentToolJsonProjection Failed(string stableCode) =>
        new(IsSuccess: false, stableCode, Failure(stableCode, retryable: false), ListedItems: 0);

    private readonly record struct BoundedListJson(string Json, int ItemCount);
}

/// <param name="ListedItems">
/// How many leading entries of the result's list the JSON carries: the
/// snapshot nodes or listed windows the model was actually shown. Zero for a
/// result without a list.
/// </param>
internal sealed record DesktopAgentToolJsonProjection(
    bool IsSuccess,
    string StableCode,
    string Json,
    int ListedItems);
