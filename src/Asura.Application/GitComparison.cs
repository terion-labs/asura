namespace Asura.Git;

/// <summary>A null target compares the pinned base with the current working tree.</summary>
public sealed record GitComparison(string BaseRevision, string? TargetRevision, IReadOnlyList<GitFileChange> Changes);
