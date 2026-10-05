namespace Asura.Git;

public enum GitRebaseAction { Pick, Reword, Edit, Squash, Fixup, Drop }

public sealed record GitRebaseEntry(string Sha, string Subject, GitRebaseAction Action = GitRebaseAction.Pick, string? Message = null);

public sealed record GitInteractiveRebaseRequest(string BaseRevision, IReadOnlyList<GitRebaseEntry> Entries, bool AutoStash, bool UpdateRefs,
    string? ExpectedHead = null);

public enum GitFlowBranchKind { Feature, Release, Hotfix }
public enum GitFlowAction { Initialize, Start, Finish, Publish, CancelFinish }
public sealed record GitFlowRequest(GitFlowAction Action, GitFlowBranchKind Kind, string Name, string MainBranch = "main",
    string DevelopBranch = "develop", string FeaturePrefix = "feature/", string ReleasePrefix = "release/", string HotfixPrefix = "hotfix/", string Remote = "origin");

public sealed record GitFlowSettings(string MainBranch, string DevelopBranch, string FeaturePrefix, string ReleasePrefix, string HotfixPrefix);

public sealed record GitFlowPendingFinish(GitFlowRequest Request, int CompletedSteps, int TotalSteps, string SourceRevision);
