namespace Asura.Git;

public enum GitOperationKind
{
    Normal,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Bisect,
}

public sealed record GitOperationState(GitOperationKind Kind)
{
    public static GitOperationState Normal { get; } = new(GitOperationKind.Normal);

    public string? CurrentRevision { get; init; }

    public string? FirstBadRevision { get; init; }

    public bool CanContinue => Kind is GitOperationKind.Merge or GitOperationKind.Rebase
        or GitOperationKind.CherryPick or GitOperationKind.Revert;

    public bool CanSkip => Kind is GitOperationKind.Rebase or GitOperationKind.CherryPick or GitOperationKind.Revert;
}

public sealed record GitHistoryQuery(
    bool AllRefs = true,
    string? Revision = null,
    string? Search = null,
    string? Author = null,
    string? Path = null,
    bool FirstParent = false,
    bool FollowRenames = false,
    bool IncludeReflog = false,
    IReadOnlyList<string>? HiddenRefs = null,
    int? StartLine = null,
    int? EndLine = null,
    bool PathIsDirectory = false);

public enum GitResetMode { Soft, Mixed, Hard }

public enum GitHistoryAction { Checkout, CherryPick, Revert, Reset, CreateBranch, RestoreFile }

public sealed record GitHistoryRequest(
    GitHistoryAction Action,
    IReadOnlyList<string> Revisions,
    GitResetMode ResetMode = GitResetMode.Mixed,
    string? Name = null,
    string? Path = null,
    bool SwitchBranch = false,
    int? Mainline = null);

public enum GitOperationControl { Continue, Skip, Abort, Good, Bad, BisectSkip, BisectReset }

public sealed record GitReflogEntry(string Sha, string Selector, string Subject);

public sealed record GitConflictContent(string Path, string Base, string Current, string Incoming, bool IsBinary)
{
    public string Fingerprint { get; init; } = "";
    public bool BaseExists { get; init; } = true;
    public bool CurrentExists { get; init; } = true;
    public bool IncomingExists { get; init; } = true;
    public string? CurrentRevision { get; init; }
    public string? IncomingRevision { get; init; }
    public string? BaseObjectId { get; init; }
    public string? CurrentObjectId { get; init; }
    public string? IncomingObjectId { get; init; }
}

public enum GitConflictResolution { Edited, Current, Incoming, Delete }

public sealed record GitConflictRequest(string Path, GitConflictResolution Resolution, string? Text = null, string? ExpectedFingerprint = null);
