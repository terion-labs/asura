namespace Asura.Git;

public sealed record GitComparison(string BaseRevision, string TargetRevision, IReadOnlyList<GitFileChange> Changes);
