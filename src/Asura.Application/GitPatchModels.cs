namespace Asura.Git;

public enum GitPatchAction { Stage, Unstage, Discard }

public sealed record GitPatchLineSelection(int Hunk, int Line);

public sealed record GitPatchRequest(GitDiffRequest Diff, string ExpectedPatch, IReadOnlyList<GitPatchLineSelection> Lines, GitPatchAction Action);
