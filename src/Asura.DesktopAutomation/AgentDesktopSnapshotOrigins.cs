using Asura.Application;

namespace Asura.DesktopAutomation;

/// <summary>
/// The process one snapshot was taken of, as Asura established it: the exact
/// name the library was handed, the process that name resolved to, that
/// process's generation token and the window the library walked.
/// </summary>
internal sealed record AgentDesktopSnapshotOrigin(
    string ApplicationName,
    uint ProcessId,
    string? ProcessInstance,
    string? WindowId)
{
    /// <summary>
    /// True when <paramref name="target"/> names the process, generation and
    /// window this snapshot showed. A target that names anything else carries
    /// a reference that was not minted for it.
    /// </summary>
    public bool Describes(AgentDesktopElementTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ProcessId == target.ProcessId
            && string.Equals(ProcessInstance, target.ProcessInstance, StringComparison.Ordinal)
            && string.Equals(WindowId, target.WindowId, StringComparison.Ordinal);
    }
}

/// <summary>
/// The snapshots one run's adapter took, by snapshot identifier.
///
/// agent-desktop resolves an element reference from a file it keeps on disk
/// and never tells the caller which process it acted on. Asura therefore
/// keeps its own record of what every snapshot was resolved to, in memory
/// where no other process can rewrite it, and dispatches an element action
/// only for a reference whose snapshot is in this record. The self-target
/// guard and the approval then judge the same process the reference was
/// minted for (ADR 0061, "Target binding and the self-operation guard").
/// </summary>
internal sealed class AgentDesktopSnapshotOrigins
{
    /// <summary>
    /// More snapshots than stay usable at once: a reference expires two
    /// minutes after its snapshot. The oldest is forgotten beyond it, and
    /// its references are then refused as stale.
    /// </summary>
    public const int Capacity = 64;

    private readonly Dictionary<string, AgentDesktopSnapshotOrigin> _origins = new(StringComparer.Ordinal);
    private readonly Queue<string> _oldestFirst = new();

    public void Remember(string snapshotId, AgentDesktopSnapshotOrigin origin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        ArgumentNullException.ThrowIfNull(origin);
        if (!_origins.ContainsKey(snapshotId))
        {
            if (_oldestFirst.Count == Capacity)
            {
                _ = _origins.Remove(_oldestFirst.Dequeue());
            }

            _oldestFirst.Enqueue(snapshotId);
        }

        // The library overwrites its own record when it reuses an identifier,
        // so the newest origin is the one its references belong to.
        _origins[snapshotId] = origin;
    }

    public AgentDesktopSnapshotOrigin? Find(string snapshotId) =>
        _origins.GetValueOrDefault(snapshotId);
}
