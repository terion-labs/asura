using System.Collections.Immutable;

namespace Asura.Core;

public enum WorkspaceMemoryKind { Fact, Decision, Tip, Handoff }
public enum WorkspaceMemoryStatus { Active, Archived, Superseded }

/// <summary>A dated assertion. Source attribution establishes origin, never truth or action authority.</summary>
public sealed record WorkspaceMemory(
    string Id, WorkspaceMemoryKind Kind, string Title, string Body,
    ImmutableArray<string> Tags, string Applicability, string Source, string Author,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt,
    WorkspaceMemoryStatus Status, bool Pinned, long Revision, WorkspaceMemoryOrigin? Origin = null, string? SupersededNoteId = null);

public sealed record WorkspaceMemoryOrigin(string Kind, string? RunId, bool Available);

public sealed record WorkspaceMemoryState(bool Enabled, bool AllowAgentWrites, long Generation, long Revision, long ContentBytes, bool Retired = false);

public sealed record WorkspaceMemoryPage(WorkspaceMemoryState State, ImmutableArray<WorkspaceMemory> Notes, bool HasMore);

public sealed record WorkspaceMemoryBrief(WorkspaceMemoryState State, string Text);

public sealed record WorkspaceMemoryWrite(
    string RequestId, long Generation, string? Id, long? ExpectedRevision,
    WorkspaceMemoryKind Kind, string Title, string Body, ImmutableArray<string> Tags,
    string Applicability, string Source, DateTimeOffset? ExpiresAt = null,
    string? SupersedesId = null, long? SupersedesRevision = null);

public sealed record WorkspaceMemoryReceipt(string Code, WorkspaceMemoryState State, WorkspaceMemory? Note = null)
{
    public bool Succeeded => string.Equals(Code, "memory_saved", StringComparison.Ordinal);
}

public enum WorkspaceMemoryChange { Archive, Restore, Pin, Unpin, Forget, ForgetAll, Enable, Disable, AllowWrites, DenyWrites, Retire, ForgetSource }
