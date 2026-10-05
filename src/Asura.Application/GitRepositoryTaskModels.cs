namespace Asura.Git;

public enum GitNetworkAction { Fetch, Pull, Push }
public enum GitPullStrategy { FastForward, Merge, Rebase }
public sealed record GitNetworkRequest(
    GitNetworkAction Action, string Remote, string? Source = null, string? Destination = null,
    GitPullStrategy Strategy = GitPullStrategy.FastForward, bool Prune = false,
    bool Tags = false, bool ForceWithLease = false, bool AutoStash = false, string? ExpectedRemoteSha = null);

public enum GitWorktreeAction { Add, Remove, Lock, Unlock, Prune }
public sealed record GitWorktreeRequest(GitWorktreeAction Action, string Path, string? Branch = null, string? NewBranch = null);

public enum GitSubmoduleAction { Add, Initialize, Update, Sync, Remove }
public sealed record GitSubmoduleRequest(GitSubmoduleAction Action, string Path, string? Url = null, bool Recursive = true);

public enum GitLfsLockAction { Lock, Unlock }
public enum GitLfsLockOwnership { CurrentUser, OtherUser, Unknown }
public sealed record GitLfsLock(string Id, string Path, string OwnerName, GitLfsLockOwnership Ownership, DateTimeOffset? LockedAt);
public sealed record GitLfsLockRequest(GitLfsLockAction Action, string Path, string? Remote = null, bool Force = false, string? ExpectedLockId = null);
public sealed record GitLfsStatusFile(string Path, string Status, string? OriginalPath);

public sealed record GitStashRequest(string? Message, IReadOnlyList<string> Paths, bool IncludeUntracked, bool KeepIndex);

public enum GitRepositoryTask
{
    SetUpstream, UnsetUpstream, PruneRemote, IgnorePattern, ApplyPatch, ExportPatch,
    BisectStart, LfsInstall, LfsFetch, LfsPull, LfsTrack, LfsUntrack, LfsLock, LfsUnlock, LfsStatus,
    Statistics, Blame, RangeHistory, Signature, ConflictForecast, ExternalDiff, ExternalMerge, Benchmark,
}

/// <summary>Named human workflows. Operands are argv data; this does not grant the agent a command runner.</summary>
public sealed record GitRepositoryTaskRequest(GitRepositoryTask Task, string? First = null, string? Second = null, string? Content = null);

public sealed record GitTaskOutput(string Text);

public sealed record GitIdentitySettings(string Name, string Email, bool SignCommits, string SigningKey);

public sealed record GitCustomCommand(string Executable, IReadOnlyList<string> Arguments);
