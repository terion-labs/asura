using System.Text.Json;
using Asura.Application;

namespace Asura.DesktopAutomation;

/// <summary>
/// Turns the <c>data</c> object of an agent-desktop snapshot into Asura's
/// bounded, flattened snapshot.
///
/// Everything in the tree was written by another application, so nothing is
/// trusted: text becomes redacted display text, references are kept only when
/// they belong to this snapshot, a secure field never carries a value, and the
/// result stops at 512 nodes and depth 32 whatever the library returned.
/// </summary>
internal static class AgentDesktopSnapshotProjection
{
    public const string SecureState = "secure";

    /// <summary>
    /// States copied per node before Asura's own cap of sixteen applies, so a
    /// node cannot make the projection copy an unbounded array. The secure
    /// marker is the one state still looked for beyond it.
    /// </summary>
    private const int MaximumStatesRead = 64;

    /// <summary>
    /// Returns null when the data is not a snapshot of <paramref name="application"/>.
    /// The caller resolved that application to a process Asura may address;
    /// a tree that claims to describe a different application is refused
    /// rather than attributed to the resolved one.
    /// </summary>
    public static AgentDesktopResult.Snapshot? Project(
        JsonElement data,
        AgentDesktopRunningApp application,
        AgentDesktopApplicationIdentity applicationIdentity,
        string? processInstance)
    {
        if (ReadString(data, "snapshot_id") is not { } snapshotId
            || !AgentDesktopElementRef.IsValidSnapshotId(snapshotId)
            || !string.Equals(ReadString(data, "app"), application.Name, StringComparison.OrdinalIgnoreCase)
            || !data.TryGetProperty("tree", out var tree)
            || tree.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryReadWindowId(data, out var windowId))
        {
            return null;
        }

        var flattened = new FlattenedTree(snapshotId);
        flattened.Add(tree);
        return new AgentDesktopResult.Snapshot(
            snapshotId,
            AgentDesktopDisplayText.FromUntrusted(application.Name, "unknown"),
            applicationIdentity,
            application.ProcessId,
            processInstance,
            windowId,
            windowId is null ? null : AgentDesktopDisplayText.FromUntrusted(ReadString(data.GetProperty("window"), "title")),
            complete: IsTrue(data, "complete") && !IsTrue(data, "truncated"),
            identityUnproven: IsTrue(data, "identity_unproven"),
            flattened.Nodes,
            flattened.Truncated,
            ReadReferenceCount(data) ?? flattened.ReferenceCount);
    }

    /// <summary>
    /// The identifier of the window a snapshot walked; null with a true
    /// answer for an application-level surface, where the envelope names no
    /// window. False when the envelope names a window whose identifier
    /// cannot be carried in a proposal or compared with a window listing:
    /// such a snapshot cannot be tied to a process and is refused whole.
    /// </summary>
    public static bool TryReadWindowId(JsonElement data, out string? windowId)
    {
        windowId = null;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("window", out var window))
        {
            return data.ValueKind == JsonValueKind.Object;
        }

        if (window.ValueKind != JsonValueKind.Object
            || ReadString(window, "id") is not { } id
            || !AgentDesktopNativeText.IsBoundedToken(id, AgentDesktopWindowTarget.MaximumWindowIdBytes))
        {
            return false;
        }

        windowId = id;
        return true;
    }

    /// <summary>
    /// True when an element holds a secret. agent-desktop reports the
    /// <c>secure</c> state for password fields; the role texts are checked as
    /// well because a toolkit can describe a password field only there.
    /// </summary>
    public static bool IsSecure(IEnumerable<string> states, params ReadOnlySpan<string?> roleTexts)
    {
        if (states.Contains(SecureState, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (var text in roleTexts)
        {
            if (text is not null
                && (text.Contains(SecureState, StringComparison.OrdinalIgnoreCase)
                    || text.Contains("password", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static List<string> ReadStates(JsonElement element)
    {
        var states = new List<string>();
        if (!element.TryGetProperty("states", out var raw) || raw.ValueKind != JsonValueKind.Array)
        {
            return states;
        }

        foreach (var state in raw.EnumerateArray())
        {
            if (state.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (states.Count < MaximumStatesRead)
            {
                states.Add(state.GetString() ?? string.Empty);
            }
            else if (state.ValueEquals(SecureState))
            {
                // Padding the array must not be a way to show a secret.
                states.Add(SecureState);
                break;
            }
        }

        return states;
    }

    public static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadReferenceCount(JsonElement data) =>
        data.TryGetProperty("ref_count", out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var count)
        && count >= 0
            ? count
            : null;

    /// <summary>
    /// The tree in document order with each node's depth. The walk keeps its
    /// own stack: recursion depth would otherwise be chosen by the application
    /// being observed.
    /// </summary>
    private sealed class FlattenedTree(string snapshotId)
    {
        private readonly Stack<(JsonElement.ArrayEnumerator Siblings, int Depth)> _pending = new();

        public List<AgentDesktopSnapshotNode> Nodes { get; } = [];

        /// <summary>True when nodes were dropped at the node or depth ceiling.</summary>
        public bool Truncated { get; private set; }

        public int ReferenceCount { get; private set; }

        public void Add(JsonElement root)
        {
            AddNode(root, depth: 0);
            while (_pending.Count > 0)
            {
                var (siblings, depth) = _pending.Pop();
                if (!siblings.MoveNext())
                {
                    continue;
                }

                // The enumerator is a struct: push the advanced copy back so
                // the next sibling is visited after this one's subtree.
                _pending.Push((siblings, depth));
                if (siblings.Current.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (Nodes.Count == AgentDesktopResult.MaximumSnapshotNodes)
                {
                    Truncated = true;
                    return;
                }

                AddNode(siblings.Current, depth);
            }
        }

        private void AddNode(JsonElement element, int depth)
        {
            var hasChildren = element.TryGetProperty("children", out var children)
                && children.ValueKind == JsonValueKind.Array
                && children.GetArrayLength() > 0;
            var cutHere = hasChildren && depth == AgentDesktopRequest.MaximumSnapshotDepth;
            Nodes.Add(ReadNode(element, depth, cutHere));
            if (cutHere)
            {
                Truncated = true;
            }
            else if (hasChildren)
            {
                _pending.Push((children.EnumerateArray(), depth + 1));
            }
        }

        private AgentDesktopSnapshotNode ReadNode(JsonElement element, int depth, bool cutHere)
        {
            var role = ReadString(element, "role");
            var states = ReadStates(element);
            var secure = IsSecure(
                states,
                role,
                ReadString(element, "subrole"),
                ReadString(element, "role_description"));
            if (secure)
            {
                // First, so the sixteen-state cap cannot drop it.
                states.Insert(0, SecureState);
            }

            var reference = ReadReference(element);
            if (reference is not null)
            {
                ReferenceCount++;
            }

            return new AgentDesktopSnapshotNode(
                depth,
                AgentDesktopDisplayText.FromUntrusted(role, "unknown"),
                AgentDesktopDisplayText.FromUntrusted(ReadString(element, "name"))
                    ?? AgentDesktopDisplayText.FromUntrusted(ReadString(element, "description"))
                    ?? AgentDesktopDisplayText.FromUntrusted(ReadString(element, "placeholder")),
                secure ? null : AgentDesktopDisplayText.FromUntrusted(ReadString(element, "value")),
                reference,
                states,
                subtreeTruncated: cutHere || IsTrue(element, "subtree_truncated"));
        }

        /// <summary>A reference from another snapshot is dropped, not relabelled.</summary>
        private AgentDesktopElementRef? ReadReference(JsonElement element)
        {
            var text = ReadString(element, "ref_id");
            if (!AgentDesktopElementRef.IsValid(text))
            {
                return null;
            }

            var reference = new AgentDesktopElementRef(text!);
            return string.Equals(reference.SnapshotId, snapshotId, StringComparison.Ordinal) ? reference : null;
        }
    }
}
