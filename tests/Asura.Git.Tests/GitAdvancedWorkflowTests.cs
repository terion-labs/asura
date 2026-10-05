using System.Runtime.Versioning;
using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Git.Tests;

/// <summary>Exercises mutation contracts against Git's actual index, operation metadata, and branch graph.</summary>
[SupportedOSPlatform("macos")]
public sealed class GitAdvancedWorkflowTests
{
    [Theory]
    [InlineData(GitPatchAction.Stage, "\n", true)]
    [InlineData(GitPatchAction.Stage, "\n", false)]
    [InlineData(GitPatchAction.Stage, "\r\n", true)]
    [InlineData(GitPatchAction.Stage, "\r\n", false)]
    [InlineData(GitPatchAction.Unstage, "\n", true)]
    [InlineData(GitPatchAction.Unstage, "\n", false)]
    [InlineData(GitPatchAction.Unstage, "\r\n", true)]
    [InlineData(GitPatchAction.Unstage, "\r\n", false)]
    [InlineData(GitPatchAction.Discard, "\n", true)]
    [InlineData(GitPatchAction.Discard, "\n", false)]
    [InlineData(GitPatchAction.Discard, "\r\n", true)]
    [InlineData(GitPatchAction.Discard, "\r\n", false)]
    public async Task PartialLastLineEditPreservesUnicodeAndLineEndings(
        GitPatchAction action, string newline, bool finalNewline)
    {
        await using var repo = await Repository.CreateAsync();
        const string path = "дані λ.txt";
        var suffix = finalNewline ? newline : "";
        var original = $"α{newline}middle{newline}ω{suffix}";
        var modified = $"Α{newline}middle{newline}Ω{suffix}";
        await repo.WriteAsync(path, original);
        await repo.CommitAsync("Unicode base");
        await repo.WriteAsync(path, modified);
        if (action == GitPatchAction.Unstage)
        {
            await repo.GitAsync("add", "--", path);
        }

        var request = new GitDiffRequest(action == GitPatchAction.Unstage ? GitDiffArea.Index : GitDiffArea.Worktree, path);
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));
        var selected = ChangedLines(diff, "ω", "Ω");
        Assert.Equal(2, selected.Count);
        Success(await repo.Client.ApplyPartialPatchAsync(repo.Handle,
            new(request, diff.RawPatch, selected, action), CancellationToken.None));

        var expectedIndex = action switch
        {
            GitPatchAction.Stage => $"α{newline}middle{newline}Ω{suffix}",
            GitPatchAction.Unstage => $"Α{newline}middle{newline}ω{suffix}",
            _ => original,
        };
        var expectedWorktree = action == GitPatchAction.Discard ? $"Α{newline}middle{newline}ω{suffix}" : modified;
        Assert.Equal(expectedIndex, await repo.GitAsync("show", ":" + path));
        Assert.Equal(expectedWorktree, await repo.TextAsync(path));
    }

    [Theory]
    [InlineData(GitPatchAction.Stage, true)]
    [InlineData(GitPatchAction.Stage, false)]
    [InlineData(GitPatchAction.Unstage, false)]
    [InlineData(GitPatchAction.Discard, true)]
    public async Task PartialNewFileActionKeepsItsUnselectedLines(GitPatchAction action, bool untracked)
    {
        await using var repo = await Repository.CreateAsync();
        const string path = "new λ.txt";
        await repo.WriteAsync(path, "alpha\nbeta\ngamma\n");
        if (action == GitPatchAction.Unstage)
        {
            await repo.GitAsync("add", "--", path);
        }
        else if (!untracked)
        {
            await repo.GitAsync("add", "--intent-to-add", "--", path);
        }

        var request = new GitDiffRequest(action == GitPatchAction.Unstage ? GitDiffArea.Index : GitDiffArea.Worktree,
            path, IsUntracked: untracked);
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));
        Success(await repo.Client.ApplyPartialPatchAsync(repo.Handle,
            new(request, diff.RawPatch, ChangedLines(diff, "beta"), action), CancellationToken.None));

        if (action == GitPatchAction.Discard)
        {
            Assert.Equal("", await repo.GitAsync("ls-files", "--", path));
            Assert.Equal("alpha\ngamma\n", await repo.TextAsync(path));
        }
        else
        {
            Assert.Equal(action == GitPatchAction.Stage ? "beta\n" : "alpha\ngamma\n", await repo.GitAsync("show", ":" + path));
            Assert.Equal("alpha\nbeta\ngamma\n", await repo.TextAsync(path));
        }
    }

    [Theory]
    [InlineData(GitPatchAction.Stage)]
    [InlineData(GitPatchAction.Unstage)]
    [InlineData(GitPatchAction.Discard)]
    public async Task PartialDeletedFileActionChangesOnlyTheSelectedDeletion(GitPatchAction action)
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("file.txt", "alpha\nbeta\ngamma\n");
        await repo.CommitAsync("Three lines");
        if (action == GitPatchAction.Unstage)
        {
            await repo.GitAsync("rm", "--", "file.txt");
        }
        else
        {
            File.Delete(Path.Combine(repo.Root, "file.txt"));
        }
        var request = new GitDiffRequest(action == GitPatchAction.Unstage ? GitDiffArea.Index : GitDiffArea.Worktree, "file.txt");
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));

        Success(await repo.Client.ApplyPartialPatchAsync(repo.Handle,
            new(request, diff.RawPatch, ChangedLines(diff, "beta"), action), CancellationToken.None));

        Assert.Equal(action switch
        {
            GitPatchAction.Stage => "alpha\ngamma\n",
            GitPatchAction.Unstage => "beta\n",
            _ => "alpha\nbeta\ngamma\n",
        }, await repo.GitAsync("show", ":file.txt"));
        if (action == GitPatchAction.Discard)
        {
            Assert.Equal("beta\n", await repo.TextAsync("file.txt"));
        }
        else
        {
            Assert.False(File.Exists(Path.Combine(repo.Root, "file.txt")));
        }
    }

    [Fact]
    public async Task PartialUnstageOfARenameRestoresTheIndexPathAndKeepsTheOtherEdit()
    {
        await using var repo = await Repository.CreateAsync();
        const string original = "alpha\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nomega\n";
        const string modified = "ALPHA\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nOMEGA\n";
        const string renamed = "renamed λ.txt";
        await repo.WriteAsync("file.txt", original);
        await repo.CommitAsync("Rename base");
        await repo.GitAsync("mv", "file.txt", renamed);
        await repo.WriteAsync(renamed, modified);
        await repo.GitAsync("add", "--", renamed);
        var request = new GitDiffRequest(GitDiffArea.Index, renamed, "file.txt");
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));
        Assert.Contains("rename from ", diff.RawPatch, StringComparison.Ordinal);

        Success(await repo.Client.ApplyPartialPatchAsync(repo.Handle,
            new(request, diff.RawPatch, ChangedLines(diff, "alpha", "ALPHA"), GitPatchAction.Unstage), CancellationToken.None));

        Assert.Equal(original.Replace("omega", "OMEGA", StringComparison.Ordinal), await repo.GitAsync("show", ":file.txt"));
        Assert.Equal("", await repo.GitAsync("ls-files", "--", renamed));
        Assert.False(File.Exists(Path.Combine(repo.Root, "file.txt")));
        Assert.Equal(modified, await repo.TextAsync(renamed));
    }

    [Fact]
    public async Task MergeRemainsRecoverableUntilResolvedAndContinued()
    {
        await using var repo = await Repository.CreateAsync();
        await CreateDivergentEditsAsync(repo);
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.MergeBranchAsync(repo.Handle, "topic", CancellationToken.None));
        var reopened = Success(await repo.Client.OpenRepositoryAsync(BuiltInConnections.Local, repo.Root, CancellationToken.None));
        Assert.Equal(GitOperationKind.Merge, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ControlOperationAsync(
            reopened, GitOperationKind.Merge, GitOperationControl.Continue, CancellationToken.None));
        Assert.Equal(GitOperationKind.Merge, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);
        var conflict = Success(await repo.Client.ReadConflictAsync(reopened, "file.txt", CancellationToken.None));
        Assert.Equal("base\n", conflict.Base);
        Assert.Equal("main\n", conflict.Current);
        Assert.Equal("topic\n", conflict.Incoming);

        Success(await repo.Client.ResolveConflictAsync(reopened,
            new("file.txt", GitConflictResolution.Edited, "resolved λ\n", conflict.Fingerprint), CancellationToken.None));
        Success(await repo.Client.ControlOperationAsync(reopened, GitOperationKind.Merge,
            GitOperationControl.Continue, CancellationToken.None));
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);
        Assert.Equal(2, (await repo.GitAsync("show", "-s", "--format=%P", "HEAD")).Trim().Split(' ').Length);
        Assert.Equal("resolved λ\n", await repo.TextAsync("file.txt"));
    }

    [Fact]
    public async Task WrongOperationControlDoesNotAbortTheActualMerge()
    {
        await using var repo = await Repository.CreateAsync();
        await CreateDivergentEditsAsync(repo);
        var originalHead = await repo.GitAsync("rev-parse", "HEAD");
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.MergeBranchAsync(repo.Handle, "topic", CancellationToken.None));
        var conflictBytes = await repo.TextAsync("file.txt");

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ControlOperationAsync(
            repo.Handle, GitOperationKind.Rebase, GitOperationControl.Abort, CancellationToken.None));
        Assert.Equal(originalHead, await repo.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(conflictBytes, await repo.TextAsync("file.txt"));
        Assert.Equal(GitOperationKind.Merge, Success(await repo.Client.ReadOperationAsync(repo.Handle, CancellationToken.None)).Kind);

        await repo.WriteAsync("unrelated.txt", "keep me\n");
        Success(await repo.Client.ControlOperationAsync(repo.Handle, GitOperationKind.Merge,
            GitOperationControl.Abort, CancellationToken.None));
        Assert.Equal(originalHead, await repo.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("main\n", await repo.TextAsync("file.txt"));
        Assert.Equal("keep me\n", await repo.TextAsync("unrelated.txt"));
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(repo.Handle, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task BisectSurvivesReopeningFindsFirstBadCommitAndResetsTheCheckout()
    {
        await using var repo = await Repository.CreateAsync();
        var commits = new List<string>();
        for (var version = 1; version <= 7; version++)
        {
            await repo.WriteAsync("version.txt", version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
            await repo.CommitAsync("Version " + version.ToString(System.Globalization.CultureInfo.InvariantCulture));
            commits.Add((await repo.GitAsync("rev-parse", "HEAD")).Trim());
        }

        Success(await repo.Client.RunRepositoryTaskAsync(repo.Handle,
            new(GitRepositoryTask.BisectStart, commits[^1], commits[0]), CancellationToken.None));
        var reopened = Success(await repo.Client.OpenRepositoryAsync(BuiltInConnections.Local, repo.Root, CancellationToken.None));
        var initialState = Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None));
        Assert.Equal(GitOperationKind.Bisect, initialState.Kind);
        Assert.Equal((await repo.GitAsync("rev-parse", "HEAD")).Trim(), initialState.CurrentRevision);
        Assert.Null(initialState.FirstBadRevision);
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ControlOperationAsync(
            reopened, GitOperationKind.Merge, GitOperationControl.Abort, CancellationToken.None));

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var version = int.Parse((await repo.TextAsync("version.txt")).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Success(await repo.Client.ControlOperationAsync(reopened, GitOperationKind.Bisect,
                version >= 4 ? GitOperationControl.Bad : GitOperationControl.Good, CancellationToken.None));
            if (Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).FirstBadRevision is not null)
            {
                break;
            }
        }

        Assert.Equal(commits[3], Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).FirstBadRevision);
        Success(await repo.Client.ControlOperationAsync(reopened, GitOperationKind.Bisect,
            GitOperationControl.BisectReset, CancellationToken.None));
        Assert.Equal("main", (await repo.GitAsync("branch", "--show-current")).Trim());
        Assert.Equal(commits[^1], (await repo.GitAsync("rev-parse", "HEAD")).Trim());
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);
    }

    [Theory]
    [InlineData("# first bad commit: [", true)]
    [InlineData("# first 'bad' commit: [", true)]
    [InlineData("# possible first bad commit: [", false)]
    [InlineData("# possible first 'bad' commit: [", false)]
    public async Task BisectCompletionRecognizesOldAndNewGitLogs(string prefix, bool completed)
    {
        await using var repo = await Repository.CreateAsync();
        var revision = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("bisect", "start");
        await repo.WriteAsync(".git/BISECT_LOG", prefix + revision + "] Base\n");

        var state = Success(await repo.Client.ReadOperationAsync(repo.Handle, CancellationToken.None));

        Assert.Equal(GitOperationKind.Bisect, state.Kind);
        Assert.Equal(completed ? revision : null, state.FirstBadRevision);
    }

    [Fact]
    public async Task BisectSkipLeavesTheSessionActiveUntilReset()
    {
        await using var repo = await Repository.CreateAsync();
        var good = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        for (var version = 0; version < 4; version++)
        {
            await repo.WriteAsync("file.txt", version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
            await repo.CommitAsync("Candidate " + version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var bad = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        Success(await repo.Client.RunRepositoryTaskAsync(repo.Handle,
            new(GitRepositoryTask.BisectStart, bad, good), CancellationToken.None));
        var skipped = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        Success(await repo.Client.ControlOperationAsync(repo.Handle, GitOperationKind.Bisect,
            GitOperationControl.BisectSkip, CancellationToken.None));
        Assert.Contains("git bisect skip " + skipped, await repo.GitAsync("bisect", "log"), StringComparison.Ordinal);
        Assert.Equal(GitOperationKind.Bisect, Success(await repo.Client.ReadOperationAsync(repo.Handle, CancellationToken.None)).Kind);
        Success(await repo.Client.ControlOperationAsync(repo.Handle, GitOperationKind.Bisect,
            GitOperationControl.BisectReset, CancellationToken.None));
        Assert.Equal(bad, (await repo.GitAsync("rev-parse", "HEAD")).Trim());
    }

    [Fact]
    public async Task WorkingComparisonIncludesCommittedStagedAndUnstagedChangesAgainstTheChosenBase()
    {
        await using var repo = await Repository.CreateAsync();
        var original = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("committed.txt", "committed after base\n");
        await repo.CommitAsync("Later commit");
        await repo.WriteAsync("staged.txt", "staged after base\n");
        await repo.GitAsync("add", "staged.txt");
        await repo.WriteAsync("file.txt", "unstaged after base\n");

        var comparison = Success(await repo.Client.ReadComparisonAsync(repo.Handle, original, null, CancellationToken.None));

        Assert.Equal(original, comparison.BaseRevision);
        Assert.Null(comparison.TargetRevision);
        Assert.Equal(["committed.txt", "file.txt", "staged.txt"], comparison.Changes.Select(change => change.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle,
            new(GitDiffArea.Worktree, "file.txt", BaseRevision: comparison.BaseRevision), CancellationToken.None));
        Assert.Contains(diff.Hunks.SelectMany(hunk => hunk.Lines), line => line.Kind == GitDiffLineKind.Removed && line.Text == "base");
        Assert.Contains(diff.Hunks.SelectMany(hunk => hunk.Lines), line => line.Kind == GitDiffLineKind.Added && line.Text == "unstaged after base");
    }

    [Fact]
    public async Task WholeCommitPatchRestoresEveryFileIncludingBinaryContent()
    {
        await using var repo = await Repository.CreateAsync();
        var before = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("first.txt", "first content\n");
        await repo.WriteAsync("second.txt", "second content\n");
        byte[] binary = [0, 255, 10, 128, 0, 42];
        await File.WriteAllBytesAsync(Path.Combine(repo.Root, "data.bin"), binary);
        await repo.CommitAsync("Several files");
        var commit = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        var patch = Success(await repo.Client.ReadDiffAsync(repo.Handle,
            new(GitDiffArea.Commit, ".", CommitSha: commit, BaseRevision: before, IncludeBinary: true), CancellationToken.None));
        Assert.False(patch.IsTruncated);
        await repo.GitAsync("reset", "--hard", before);
        await repo.GitWithInputAsync(patch.RawPatch, "apply", "--index", "-");
        Assert.Equal("first content\n", await repo.TextAsync("first.txt"));
        Assert.Equal("second content\n", await repo.TextAsync("second.txt"));
        Assert.Equal(binary, await File.ReadAllBytesAsync(Path.Combine(repo.Root, "data.bin")));
        Assert.Equal("", await repo.GitAsync("diff", commit, "--"));
    }

    [Fact]
    public async Task ComparisonPinsBranchNamesToReviewedCommitsBeforeAHeadMovement()
    {
        await using var repo = await Repository.CreateAsync();
        var original = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("branch", "comparison-base");
        await repo.WriteAsync("file.txt", "reviewed change\n");
        await repo.CommitAsync("Reviewed target");
        var reviewed = (await repo.GitAsync("rev-parse", "HEAD")).Trim();

        var comparison = Success(await repo.Client.ReadComparisonAsync(repo.Handle, "comparison-base", "main", CancellationToken.None));

        Assert.Equal(original, comparison.BaseRevision);
        Assert.Equal(reviewed, comparison.TargetRevision);
        Assert.Equal("file.txt", Assert.Single(comparison.Changes).Path);
        await repo.WriteAsync("file.txt", "later unreviewed change\n");
        await repo.CommitAsync("Move target branch");
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle,
            new(GitDiffArea.Commit, "file.txt", CommitSha: comparison.TargetRevision, BaseRevision: comparison.BaseRevision), CancellationToken.None));
        Assert.Contains(diff.Hunks.SelectMany(hunk => hunk.Lines), line => line.Text == "reviewed change" && line.Kind == GitDiffLineKind.Added);
        Assert.DoesNotContain(diff.Hunks.SelectMany(hunk => hunk.Lines), line => line.Text == "later unreviewed change");
    }

    [Fact]
    public async Task DirectoryHistoryIncludesChangesToEachChildWhenFileRenameFollowingIsRequested()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("folder/a.txt", "first\n");
        await repo.WriteAsync("folder/b.txt", "second\n");
        await repo.CommitAsync("Add directory");
        await repo.WriteAsync("folder/a.txt", "edited first\n");
        await repo.CommitAsync("Edit first child");
        await repo.WriteAsync("folder/b.txt", "edited second\n");
        await repo.CommitAsync("Edit second child");
        await repo.WriteAsync("file.txt", "outside folder\n");
        await repo.CommitAsync("Outside directory");

        var page = Success(await repo.Client.ReadHistoryAsync(repo.Handle,
            new(AllRefs: false, Revision: "HEAD", Path: "folder", FollowRenames: true, PathIsDirectory: true), 0, 20, CancellationToken.None));

        Assert.Equal(["Edit second child", "Edit first child", "Add directory"], page.Commits.Select(commit => commit.Subject), StringComparer.Ordinal);
    }

    [Fact]
    public async Task ParentPushRejectsAnUnpublishedSubmoduleCommitWithoutCreatingTheRemoteBranch()
    {
        await using var repo = await Repository.CreateAsync();
        await using var source = await Repository.CreateAsync();
        const string path = "nested";
        await repo.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", source.Root, path);
        await repo.GitAsync("-C", path, "config", "--local", "user.name", "Nested fixture");
        await repo.GitAsync("-C", path, "config", "--local", "user.email", "nested@example.test");
        await repo.GitAsync("-C", path, "config", "--local", "commit.gpgSign", "false");
        await repo.GitAsync("-C", path, "config", "--local", "core.hooksPath", "/dev/null");
        await repo.WriteAsync(path + "/file.txt", "unpublished nested commit\n");
        await repo.GitAsync("-C", path, "add", "file.txt");
        await repo.GitAsync("-C", path, "commit", "-m", "Nested commit absent from its remote");
        await repo.CommitAsync("Parent references unpublished nested commit");
        await repo.GitAsync("init", "--bare", repo.RemoteRoot);
        await repo.GitAsync("remote", "add", "parent-origin", repo.RemoteRoot);

        var result = await repo.Client.NetworkAsync(repo.Handle,
            new(GitNetworkAction.Push, "parent-origin", Destination: "refs/heads/main"), CancellationToken.None);

        Assert.IsType<GitResult<GitUnit>.Failure>(result);
        Assert.Equal("", await repo.GitAsync("ls-remote", "parent-origin", "refs/heads/main"));
    }

    [Fact]
    public async Task SubmoduleShowsExpectedAndCheckedOutRevisionsAndRejectsReplacedDirectories()
    {
        await using var repo = await Repository.CreateAsync();
        await using var source = await Repository.CreateAsync();
        var expected = (await source.GitAsync("rev-parse", "HEAD")).Trim();
        const string path = "modules/nested λ";
        await repo.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", source.Root, path);
        await repo.CommitAsync("Record nested repository");
        var initial = Assert.Single(Success(await repo.Client.ReadSubmodulesAsync(repo.Handle, CancellationToken.None)));
        Assert.Equal(expected, initial.ExpectedRevision);
        Assert.Equal(expected, initial.CheckedOutRevision);
        Assert.True(initial.IsInitialized);
        Assert.False(initial.IsDirty);
        Assert.Equal("clean", initial.State);
        await source.WriteAsync("file.txt", "new nested revision\n");
        await source.CommitAsync("Advance nested repository");
        var observed = (await source.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("-C", path, "-c", "protocol.file.allow=always", "fetch", "origin");
        await repo.GitAsync("-C", path, "checkout", observed);

        var modified = Assert.Single(Success(await repo.Client.ReadSubmodulesAsync(repo.Handle, CancellationToken.None)));
        Assert.Equal(expected, modified.ExpectedRevision);
        Assert.Equal(observed, modified.CheckedOutRevision);
        Assert.Equal("modified", modified.State);
        Assert.False(modified.IsDirty);
        var nested = Success(await repo.Client.OpenSubmoduleAsync(repo.Handle, path, CancellationToken.None));
        Assert.Equal((await repo.GitAsync("-C", path, "rev-parse", "--show-toplevel")).Trim(), nested.WorkingTreeRoot);
        var comparison = Success(await repo.Client.ReadSubmoduleComparisonAsync(repo.Handle, path, expected, observed, CancellationToken.None));
        Assert.Equal("file.txt", Assert.Single(comparison.Changes).Path);
        await repo.WriteAsync(path + "/file.txt", "uncommitted nested edit\n");
        var dirty = Assert.Single(Success(await repo.Client.ReadSubmodulesAsync(repo.Handle, CancellationToken.None)));
        Assert.Equal("dirty", dirty.State);
        Assert.True(dirty.IsDirty);
        await repo.GitAsync("submodule", "deinit", "--force", "--", path);
        var uninitialized = Assert.Single(Success(await repo.Client.ReadSubmodulesAsync(repo.Handle, CancellationToken.None)));
        Assert.False(uninitialized.IsInitialized);
        Assert.Null(uninitialized.CheckedOutRevision);
        Assert.Equal(expected, uninitialized.ExpectedRevision);
        Assert.IsType<GitResult<GitRepositoryHandle>.Failure>(await repo.Client.OpenSubmoduleAsync(repo.Handle, path, CancellationToken.None));

        Directory.Delete(Path.Combine(repo.Root, path));
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, path), source.Root);

        Assert.IsType<GitResult<GitRepositoryHandle>.Failure>(await repo.Client.OpenSubmoduleAsync(repo.Handle, path, CancellationToken.None));
        Assert.IsType<GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure>(await repo.Client.ReadSubmodulesAsync(repo.Handle, CancellationToken.None));
        Assert.Equal("new nested revision\n", await source.TextAsync("file.txt"));
    }

    [Fact]
    public async Task UnsignedCommitReturnsTypedSignatureWithoutInventingASigner()
    {
        await using var repo = await Repository.CreateAsync();

        var signature = Success(await repo.Client.ReadSignatureAsync(repo.Handle, "HEAD", CancellationToken.None));

        Assert.Equal(new GitSignature("Not signed", "", "", ""), signature);
        Assert.Equal("Not signed", signature.Summary);
    }

    [GpgAvailableFact]
    public async Task DisposableGpgKeySignsACommitAndReturnsItsVerifiedIdentity()
    {
        await using var repo = await Repository.CreateAsync();
        var gpg = AvailableGpgPath!;
        // macOS's normal temporary root exceeds the Unix socket path limit
        // once GPG appends its agent socket name. Keep this isolated home short.
        var directory = Path.Combine("/tmp", "asura-gpg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            Success(await repo.Client.RunCustomCommandAsync(repo.Handle,
                new(gpg, ["--homedir", directory, "--batch", "--pinentry-mode", "loopback", "--passphrase", "",
                    "--quick-generate-key", "Asura Fixture <fixture@example.test>", "ed25519", "sign", "0"]), CancellationToken.None));
            var keys = Success(await repo.Client.RunCustomCommandAsync(repo.Handle,
                new(gpg, ["--homedir", directory, "--batch", "--with-colons", "--list-secret-keys"]), CancellationToken.None)).Text;
            var fingerprint = keys.Split('\n').First(line => line.StartsWith("fpr:", StringComparison.Ordinal)).Split(':')[9];
            var wrapper = Path.Combine(repo.Root, ".git", "signing program with spaces");
            await File.WriteAllTextAsync(wrapper,
                "#!/bin/sh\nexec '" + gpg.Replace("'", "'\\''", StringComparison.Ordinal) + "' --homedir '"
                + directory.Replace("'", "'\\''", StringComparison.Ordinal) + "' \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await repo.GitAsync("config", "--local", "gpg.format", "openpgp");
            await repo.GitAsync("config", "--local", "gpg.program", wrapper);
            await repo.GitAsync("config", "--local", "user.signingkey", fingerprint);
            await repo.GitAsync("config", "--local", "commit.gpgSign", "true");
            await repo.WriteAsync("signed.txt", "signed fixture\n");
            Success(await repo.Client.StageAsync(repo.Handle, ["signed.txt"], CancellationToken.None));

            Success(await repo.Client.CommitAsync(repo.Handle, new("Signed fixture", "", false), CancellationToken.None));
            var signature = Success(await repo.Client.ReadSignatureAsync(repo.Handle, "HEAD", CancellationToken.None));

            Assert.Equal("Signature valid", signature.Status);
            Assert.Equal("Asura Fixture <fixture@example.test>", signature.Signer);
            Assert.Equal(fingerprint, signature.Fingerprint);
            Assert.NotEmpty(signature.Key);
        }
        finally
        {
            var gpgconf = Path.Combine(Path.GetDirectoryName(gpg)!, "gpgconf");
            if (File.Exists(gpgconf))
            {
                await repo.Client.RunCustomCommandAsync(repo.Handle,
                    new(gpgconf, ["--homedir", directory, "--kill", "gpg-agent"]), CancellationToken.None);
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HistoricalFilePathResolvesBothSidesOfSuccessiveUnicodeRenames()
    {
        await using var repo = await Repository.CreateAsync();
        var original = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("mv", "file.txt", "intermediate λ.txt");
        await repo.CommitAsync("First rename");
        var firstRename = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("mv", "intermediate λ.txt", "資料 final.txt");
        await repo.CommitAsync("Second rename");
        var secondRename = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("資料 final.txt", "base\nlatest\n");
        await repo.CommitAsync("Edit renamed file");
        var current = (await repo.GitAsync("rev-parse", "HEAD")).Trim();

        Assert.Equal(new GitHistoricalFilePath("file.txt", null),
            Success(await repo.Client.ReadHistoricalFilePathAsync(repo.Handle, current, "資料 final.txt", original, CancellationToken.None)));
        Assert.Equal(new GitHistoricalFilePath("intermediate λ.txt", "file.txt"),
            Success(await repo.Client.ReadHistoricalFilePathAsync(repo.Handle, current, "資料 final.txt", firstRename, CancellationToken.None)));
        Assert.Equal(new GitHistoricalFilePath("資料 final.txt", "intermediate λ.txt"),
            Success(await repo.Client.ReadHistoricalFilePathAsync(repo.Handle, current, "資料 final.txt", secondRename, CancellationToken.None)));
        Assert.Equal(new GitHistoricalFilePath("資料 final.txt", null),
            Success(await repo.Client.ReadHistoricalFilePathAsync(repo.Handle, current, "資料 final.txt", current, CancellationToken.None)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImagePreviewResolvesCachedLfsBytesAndIdentifiesMissingObjects(bool cached)
    {
        await using var repo = await Repository.CreateAsync();
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jwWQAAAAASUVORK5CYII=");
        var oid = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)).ToLowerInvariant();
        var pointer = $"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize {image.Length}\n";
        await repo.WriteAsync("image λ.png", pointer);
        await repo.CommitAsync("LFS pointer without an installed filter");
        if (cached)
        {
            var location = (await repo.GitAsync("rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
            var cachePath = Path.Combine(location, "lfs", "objects", oid[..2], oid[2..4], oid);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllBytesAsync(cachePath, image);
        }

        var pair = Success(await repo.Client.ReadImagesAsync(repo.Handle,
            new(GitDiffArea.Worktree, "image λ.png"), CancellationToken.None));

        foreach (var version in new[] { pair.Before, pair.After })
        {
            Assert.False(version.IsMissing);
            Assert.Equal(!cached, version.IsLfsPointer);
            Assert.Equal(oid, version.LfsObjectId);
            Assert.Equal(cached ? image : Encoding.UTF8.GetBytes(pointer), version.Bytes.ToArray());
        }
    }

    [Fact]
    public async Task SelectedPathStashTreatsGlobCharactersLiterallyAndKeepsOtherChanges()
    {
        await using var repo = await Repository.CreateAsync();
        const string selected = "notes[λ]*.txt";
        await repo.WriteAsync(selected, "selected untracked note\n");
        await repo.WriteAsync("notesλ-unselected.txt", "other untracked note\n");
        await repo.WriteAsync("file.txt", "unstashed tracked edit\n");

        Success(await repo.Client.SaveStashAsync(repo.Handle,
            new("Selected literal path", [selected], IncludeUntracked: true, KeepIndex: false), CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(repo.Root, selected)));
        Assert.Equal("other untracked note\n", await repo.TextAsync("notesλ-unselected.txt"));
        Assert.Equal("unstashed tracked edit\n", await repo.TextAsync("file.txt"));
        var patch = Success(await repo.Client.ReadStashPatchAsync(repo.Handle, "stash@{0}", CancellationToken.None)).Text;
        Assert.Contains("+selected untracked note", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("+other untracked note", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("+unstashed tracked edit", patch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StashPatchIncludesTrackedAndUntrackedFilesAndCanRestoreTheirBytes()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("file.txt", "changed tracked content\n");
        await repo.WriteAsync("notes λ.txt", "untracked note\n");
        Success(await repo.Client.SaveStashAsync(repo.Handle,
            new("Keep both kinds", [], IncludeUntracked: true, KeepIndex: false), CancellationToken.None));
        Assert.Equal("", await repo.GitAsync("status", "--porcelain"));

        var patch = Success(await repo.Client.ReadStashPatchAsync(repo.Handle, "stash@{0}", CancellationToken.None)).Text;

        Assert.Contains("+changed tracked content", patch, StringComparison.Ordinal);
        Assert.Contains("+untracked note", patch, StringComparison.Ordinal);
        Success(await repo.Client.StashApplyAsync(repo.Handle, "stash@{0}", CancellationToken.None));
        Assert.Equal("changed tracked content\n", await repo.TextAsync("file.txt"));
        Assert.Equal("untracked note\n", await repo.TextAsync("notes λ.txt"));
        Assert.NotEqual("", await repo.GitAsync("stash", "list"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task HiddenReferenceRemovesItsDecorationWithoutHidingSharedHistory()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("branch", "hidden-topic");
        var head = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        var before = Success(await repo.Client.ReadHistoryAsync(repo.Handle, new(), 0, 10, CancellationToken.None));
        Assert.Contains(Assert.Single(before.Commits).RefNames, name => name.Contains("hidden-topic", StringComparison.Ordinal));

        var after = Success(await repo.Client.ReadHistoryAsync(repo.Handle,
            new(HiddenRefs: ["refs/heads/hidden-topic"]), 0, 10, CancellationToken.None));

        var commit = Assert.Single(after.Commits);
        Assert.Equal(head, commit.Sha);
        Assert.DoesNotContain(commit.RefNames, name => name.Contains("hidden-topic", StringComparison.Ordinal));
        Assert.Contains(commit.RefNames, name => name.Contains("main", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(GitFlowAction.Start, "main")]
    [InlineData(GitFlowAction.Start, "integration")]
    [InlineData(GitFlowAction.Finish, "main")]
    [InlineData(GitFlowAction.Finish, "integration")]
    public async Task GitFlowEmptyTopicPrefixCannotMutateAPrincipalBranch(GitFlowAction action, string name)
    {
        await using var repo = await Repository.CreateAsync();
        var configuration = new GitFlowRequest(GitFlowAction.Initialize, GitFlowBranchKind.Feature, "",
            DevelopBranch: "integration", FeaturePrefix: "");
        Success(await repo.Client.GitFlowAsync(repo.Handle, configuration, CancellationToken.None));
        var references = await repo.GitAsync("show-ref");
        var head = await repo.GitAsync("rev-parse", "HEAD");

        var result = await repo.Client.GitFlowAsync(repo.Handle, configuration with { Action = action, Name = name }, CancellationToken.None);

        Assert.IsType<GitResult<GitUnit>.Failure>(result);
        Assert.Equal(references, await repo.GitAsync("show-ref"));
        Assert.Equal(head, await repo.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("main", (await repo.GitAsync("branch", "--show-current")).Trim());
        Assert.Equal("", await repo.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task GitFlowFeatureAndReleaseUseConfiguredBranchesAndCreateAnAnnotatedTag()
    {
        await using var repo = await Repository.CreateAsync();
        var configuration = new GitFlowRequest(GitFlowAction.Initialize, GitFlowBranchKind.Feature, "",
            DevelopBranch: "integration", FeaturePrefix: "work/", ReleasePrefix: "ship/", HotfixPrefix: "fix/");
        Success(await repo.Client.GitFlowAsync(repo.Handle, configuration, CancellationToken.None));
        Assert.Equal("integration", (await repo.GitAsync("config", "--get", "gitflow.branch.develop")).Trim());
        Assert.Equal("work/", (await repo.GitAsync("config", "--get", "gitflow.prefix.feature")).Trim());
        Assert.Equal(new GitFlowSettings("main", "integration", "work/", "ship/", "fix/"),
            Success(await repo.NewClient().ReadGitFlowSettingsAsync(repo.Handle, CancellationToken.None)));

        var feature = configuration with { Action = GitFlowAction.Start, Name = "new-ui" };
        Success(await repo.Client.GitFlowAsync(repo.Handle, feature, CancellationToken.None));
        Assert.Equal("work/new-ui", (await repo.GitAsync("branch", "--show-current")).Trim());
        await repo.WriteAsync("feature.txt", "feature\n");
        await repo.CommitAsync("New UI");
        Success(await repo.Client.GitFlowAsync(repo.Handle, feature with { Action = GitFlowAction.Finish }, CancellationToken.None));
        Assert.Equal("integration", (await repo.GitAsync("branch", "--show-current")).Trim());
        Assert.Equal("feature\n", await repo.GitAsync("show", "integration:feature.txt"));
        Assert.Equal("", await repo.GitAsync("branch", "--list", "work/new-ui"));
        Assert.Equal(2, (await repo.GitAsync("show", "-s", "--format=%P", "integration")).Trim().Split(' ').Length);

        var release = configuration with { Action = GitFlowAction.Start, Kind = GitFlowBranchKind.Release, Name = "1.0" };
        Success(await repo.Client.GitFlowAsync(repo.Handle, release, CancellationToken.None));
        Assert.Equal("ship/1.0", (await repo.GitAsync("branch", "--show-current")).Trim());
        await repo.WriteAsync("version.txt", "1.0\n");
        await repo.CommitAsync("Prepare release");
        Success(await repo.Client.GitFlowAsync(repo.Handle, release with { Action = GitFlowAction.Finish }, CancellationToken.None));
        Assert.Equal("integration", (await repo.GitAsync("branch", "--show-current")).Trim());
        Assert.Equal("1.0\n", await repo.GitAsync("show", "main:version.txt"));
        Assert.Equal("1.0\n", await repo.GitAsync("show", "integration:version.txt"));
        Assert.Equal("", await repo.GitAsync("branch", "--list", "ship/1.0"));
        Assert.Equal("tag", (await repo.GitAsync("cat-file", "-t", "1.0")).Trim());
        Assert.Equal(await repo.GitAsync("rev-parse", "main"), await repo.GitAsync("rev-parse", "1.0^{}"));
    }

    [Fact]
    public async Task GitFlowHotfixStartsFromMainAndFinishesIntoBothBranches()
    {
        await using var repo = await Repository.CreateAsync();
        var configuration = new GitFlowRequest(GitFlowAction.Initialize, GitFlowBranchKind.Hotfix, "",
            DevelopBranch: "integration", HotfixPrefix: "fix/");
        Success(await repo.Client.GitFlowAsync(repo.Handle, configuration, CancellationToken.None));
        await repo.GitAsync("switch", "integration");
        await repo.WriteAsync("development.txt", "unreleased\n");
        await repo.CommitAsync("Unreleased work");
        var main = await repo.GitAsync("rev-parse", "main");
        var hotfix = configuration with { Action = GitFlowAction.Start, Name = "1.0.1" };
        Success(await repo.Client.GitFlowAsync(repo.Handle, hotfix, CancellationToken.None));
        Assert.Equal(main, await repo.GitAsync("rev-parse", "HEAD"));
        Assert.False(File.Exists(Path.Combine(repo.Root, "development.txt")));
        await repo.WriteAsync("fix.txt", "fixed\n");
        await repo.CommitAsync("Fix released bug");
        Success(await repo.Client.GitFlowAsync(repo.Handle, hotfix with { Action = GitFlowAction.Finish }, CancellationToken.None));
        Assert.Equal("fixed\n", await repo.GitAsync("show", "main:fix.txt"));
        Assert.Equal("fixed\n", await repo.GitAsync("show", "integration:fix.txt"));
        Assert.Equal("unreleased\n", await repo.GitAsync("show", "integration:development.txt"));
        Assert.Equal("", await repo.GitAsync("branch", "--list", "fix/1.0.1"));
        Assert.Equal("tag", (await repo.GitAsync("cat-file", "-t", "1.0.1")).Trim());
    }

    [Fact]
    public async Task GitFlowPublishSetsTheExactFeatureUpstream()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("init", "--bare", repo.RemoteRoot);
        await repo.GitAsync("remote", "add", "test-origin", repo.RemoteRoot);
        var request = new GitFlowRequest(GitFlowAction.Initialize, GitFlowBranchKind.Feature, "", Remote: "test-origin");
        Success(await repo.Client.GitFlowAsync(repo.Handle, request, CancellationToken.None));
        request = request with { Action = GitFlowAction.Start, Name = "published" };
        Success(await repo.Client.GitFlowAsync(repo.Handle, request, CancellationToken.None));
        await repo.WriteAsync("published.txt", "published\n");
        await repo.CommitAsync("Publish feature");
        Success(await repo.Client.GitFlowAsync(repo.Handle, request with { Action = GitFlowAction.Publish }, CancellationToken.None));
        Assert.Equal("test-origin/feature/published", (await repo.GitAsync("rev-parse", "--abbrev-ref", "@{upstream}")).Trim());
        var remote = await repo.GitAsync("ls-remote", "test-origin", "refs/heads/feature/published");
        Assert.StartsWith((await repo.GitAsync("rev-parse", "HEAD")).Trim() + "\t", remote, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InteractiveRebaseRejectsCommitsAddedAfterThePlanWasReviewed(bool includeExpectedHead)
    {
        await using var repo = await Repository.CreateAsync();
        var basis = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("one.txt", "one\n");
        await repo.CommitAsync("One");
        var reviewed = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("two.txt", "external work\n");
        await repo.CommitAsync("Added while plan was open");
        var current = await repo.GitAsync("rev-parse", "HEAD");

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.InteractiveRebaseAsync(repo.Handle,
            new(basis, [new(reviewed, "One", GitRebaseAction.Drop)], false, false,
                includeExpectedHead ? reviewed : null), CancellationToken.None));

        Assert.Equal(current, await repo.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("3", (await repo.GitAsync("rev-list", "--count", "HEAD")).Trim());
        Assert.Equal("external work\n", await repo.TextAsync("two.txt"));
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(repo.Handle, CancellationToken.None)).Kind);
        Assert.Equal("", await repo.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task AddAddConflictDistinguishesAnAbsentBaseFromAnEmptyBlob()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("switch", "-c", "topic");
        await repo.WriteAsync("added.txt", "topic addition\n");
        await repo.CommitAsync("Topic addition");
        await repo.GitAsync("switch", "main");
        await repo.WriteAsync("added.txt", "main addition\n");
        await repo.CommitAsync("Main addition");
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.MergeBranchAsync(repo.Handle, "topic", CancellationToken.None));

        var conflict = Success(await repo.Client.ReadConflictAsync(repo.Handle, "added.txt", CancellationToken.None));

        Assert.False(conflict.BaseExists);
        Assert.True(conflict.CurrentExists);
        Assert.True(conflict.IncomingExists);
        Assert.Equal("", conflict.Base);
        Assert.Equal("main addition\n", conflict.Current);
        Assert.Equal("topic addition\n", conflict.Incoming);
        Success(await repo.Client.ResolveConflictAsync(repo.Handle,
            new("added.txt", GitConflictResolution.Current, ExpectedFingerprint: conflict.Fingerprint), CancellationToken.None));
        Success(await repo.Client.ControlOperationAsync(repo.Handle, GitOperationKind.Merge, GitOperationControl.Continue, CancellationToken.None));
        Assert.Equal("main addition\n", await repo.TextAsync("added.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModifyDeleteConflictCanAcceptTheAbsentCurrentOrIncomingSide(bool currentDeleted)
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("switch", "-c", "topic");
        if (currentDeleted)
        {
            await repo.WriteAsync("file.txt", "topic edit\n");
        }
        else
        {
            await repo.GitAsync("rm", "--", "file.txt");
        }
        await repo.CommitAsync("Topic change");
        await repo.GitAsync("switch", "main");
        if (currentDeleted)
        {
            await repo.GitAsync("rm", "--", "file.txt");
        }
        else
        {
            await repo.WriteAsync("file.txt", "main edit\n");
        }
        await repo.CommitAsync("Main change");
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.MergeBranchAsync(repo.Handle, "topic", CancellationToken.None));
        var conflict = Success(await repo.Client.ReadConflictAsync(repo.Handle, "file.txt", CancellationToken.None));
        Assert.True(conflict.BaseExists);
        Assert.Equal(!currentDeleted, conflict.CurrentExists);
        Assert.Equal(currentDeleted, conflict.IncomingExists);
        var resolution = currentDeleted ? GitConflictResolution.Current : GitConflictResolution.Incoming;

        Success(await repo.Client.ResolveConflictAsync(repo.Handle,
            new("file.txt", resolution, ExpectedFingerprint: conflict.Fingerprint), CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(repo.Root, "file.txt")));
        Assert.Equal("", await repo.GitAsync("ls-files", "--unmerged"));
        Success(await repo.Client.ControlOperationAsync(repo.Handle, GitOperationKind.Merge, GitOperationControl.Continue, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(repo.Root, "file.txt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EditedConflictResolutionRejectsChangedIndexOrWorktree(bool changeIndex)
    {
        await using var repo = await Repository.CreateAsync();
        await CreateDivergentEditsAsync(repo);
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.MergeBranchAsync(repo.Handle, "topic", CancellationToken.None));
        var conflict = Success(await repo.Client.ReadConflictAsync(repo.Handle, "file.txt", CancellationToken.None));
        var originalWorktree = await repo.TextAsync("file.txt");
        await repo.WriteAsync("file.txt", "external resolution\n");
        if (changeIndex)
        {
            var replacement = (await repo.GitAsync("hash-object", "-w", "--", "file.txt")).Trim();
            await repo.GitWithInputAsync("100644 " + replacement + " 2\tfile.txt\n", "update-index", "--index-info");
            await repo.WriteAsync("file.txt", originalWorktree);
        }
        var expectedWorktree = await repo.TextAsync("file.txt");
        var expectedIndex = await repo.GitAsync("ls-files", "--unmerged");

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ResolveConflictAsync(repo.Handle,
            new("file.txt", GitConflictResolution.Edited, "stale editor text\n", conflict.Fingerprint), CancellationToken.None));

        Assert.Equal(expectedWorktree, await repo.TextAsync("file.txt"));
        Assert.Equal(expectedIndex, await repo.GitAsync("ls-files", "--unmerged"));
        Assert.Equal(GitOperationKind.Merge, Success(await repo.Client.ReadOperationAsync(repo.Handle, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task EditedConflictResolutionRejectsASymlinkInAnAncestorDirectory()
    {
        await using var repo = await Repository.CreateAsync();
        const string path = "directory/file.txt";
        await repo.WriteAsync(path, "base\n");
        await repo.CommitAsync("Nested base");
        await CreateDivergentEditsAsync(repo, path);
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.MergeBranchAsync(repo.Handle, "topic", CancellationToken.None));
        var conflict = Success(await repo.Client.ReadConflictAsync(repo.Handle, path, CancellationToken.None));
        var expectedIndex = await repo.GitAsync("ls-files", "--unmerged");
        var outside = Directory.CreateDirectory(Path.Combine(repo.RemoteRoot, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "file.txt"), "outside bytes\n");
        var ancestor = Path.Combine(repo.Root, "directory");
        Directory.Delete(ancestor, recursive: true);
        Directory.CreateSymbolicLink(ancestor, outside);

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ResolveConflictAsync(repo.Handle,
            new(path, GitConflictResolution.Edited, "must not escape\n", conflict.Fingerprint), CancellationToken.None));

        Assert.Equal("outside bytes\n", await File.ReadAllTextAsync(Path.Combine(outside, "file.txt")));
        Assert.Equal(expectedIndex, await repo.GitAsync("ls-files", "--unmerged"));
    }

    [Fact]
    public async Task CustomCommandRunsInsideTheRepositoryWithLiteralArguments()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("relative file.txt", "relative content\n");

        var result = Success(await repo.Client.RunCustomCommandAsync(repo.Handle,
            new("/bin/cat", ["relative file.txt"]), CancellationToken.None));

        Assert.Equal("relative content\n", result.Text);
        Assert.Equal("", await repo.GitAsync("diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task HumanExecutablePreferenceAcceptsSpacesWithoutChangingGovernedGitExecution()
    {
        await using var repo = await Repository.CreateAsync();
        var wrapper = Path.Combine(repo.Root, ".git", "custom tools", "git wrapper");
        var trace = Path.Combine(repo.Root, ".git", "wrapper-trace");
        Directory.CreateDirectory(Path.GetDirectoryName(wrapper)!);
        var traceOperand = "'" + trace.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nprintf 'invoked\\n' >> " + traceOperand + "\nexec git \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var human = repo.Handle with { Executable = wrapper };

        Assert.NotEmpty(Success(await repo.Client.ReadHistoryAsync(human, new(), 0, 10, CancellationToken.None)).Commits);
        Assert.True(File.Exists(trace));
        File.Delete(trace);

        Success(await repo.Client.ReadGovernedStateAsync(human, 0, CancellationToken.None));

        Assert.False(File.Exists(trace));
    }

    [Fact]
    public async Task StructuredLineHistoryFollowsRenameAndPaginatesOnlyTheSelectedRange()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("file.txt", "alpha\nbeta\ngamma\ndelta\n");
        await repo.CommitAsync("Range base");
        await repo.WriteAsync("file.txt", "ALPHA\nbeta\ngamma\ndelta\n");
        await repo.CommitAsync("Outside range first");
        await repo.WriteAsync("file.txt", "ALPHA\nBETA\ngamma\ndelta\n");
        await repo.CommitAsync("Selected line first");
        await repo.GitAsync("mv", "file.txt", "renamed.txt");
        await repo.CommitAsync("Rename selected file");
        await repo.WriteAsync("renamed.txt", "ALPHA\nBETA\ngamma\nDELTA\n");
        await repo.CommitAsync("Outside range latest");
        await repo.WriteAsync("renamed.txt", "ALPHA\nBeta latest\ngamma\nDELTA\n");
        await repo.CommitAsync("Selected line latest");
        var query = new GitHistoryQuery(AllRefs: false, Path: "renamed.txt", StartLine: 2, EndLine: 2);

        var all = Success(await repo.Client.ReadHistoryAsync(repo.Handle, query, 0, 20, CancellationToken.None));

        Assert.Contains(all.Commits, commit => commit.Subject == "Selected line latest");
        Assert.Contains(all.Commits, commit => commit.Subject == "Selected line first");
        Assert.Contains(all.Commits, commit => commit.Subject == "Range base");
        Assert.DoesNotContain(all.Commits, commit => commit.Subject.StartsWith("Outside range", StringComparison.Ordinal));
        var paginated = new List<string>();
        for (var offset = 0; offset < all.Commits.Count; offset++)
        {
            var page = Success(await repo.Client.ReadHistoryAsync(repo.Handle, query, offset, 1, CancellationToken.None));
            paginated.Add(Assert.Single(page.Commits).Sha);
            Assert.Equal(offset < all.Commits.Count - 1, page.HasMore);
        }
        Assert.Equal(all.Commits.Select(commit => commit.Sha), paginated, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    public async Task StructuredLineHistoryRejectsInvalidBounds(int start, int end)
    {
        await using var repo = await Repository.CreateAsync();
        var head = await repo.GitAsync("rev-parse", "HEAD");

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await repo.Client.ReadHistoryAsync(repo.Handle,
            new GitHistoryQuery(AllRefs: false, Path: "file.txt", StartLine: start, EndLine: end), 0, 10, CancellationToken.None));

        Assert.Equal(head, await repo.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("", await repo.GitAsync("status", "--porcelain"));
    }

    [Fact]
    public async Task DestinationOnlyPushPublishesHeadToTheRequestedRemoteBranch()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("init", "--bare", repo.RemoteRoot);
        await repo.GitAsync("remote", "add", "test-origin", repo.RemoteRoot);

        Success(await repo.Client.NetworkAsync(repo.Handle,
            new(GitNetworkAction.Push, "test-origin", Destination: "refs/heads/review"), CancellationToken.None));

        var remote = await repo.GitAsync("ls-remote", "test-origin", "refs/heads/review");
        Assert.StartsWith((await repo.GitAsync("rev-parse", "HEAD")).Trim() + "\t", remote, StringComparison.Ordinal);
        Assert.Equal("", await repo.GitAsync("ls-remote", "test-origin", "refs/heads/main"));
    }

    [Fact]
    public async Task ForcePushUsesTheReviewedRemoteCommitEvenAfterAnInterveningFetch()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("init", "--bare", repo.RemoteRoot);
        await repo.GitAsync("remote", "add", "test-origin", repo.RemoteRoot);
        await repo.GitAsync("push", "test-origin", "main");
        var reviewed = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("switch", "-c", "other-writer");
        await repo.WriteAsync("remote.txt", "other writer\n");
        await repo.CommitAsync("Remote advance");
        var advanced = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("push", "test-origin", "HEAD:refs/heads/main");
        await repo.GitAsync("switch", "main");
        await repo.WriteAsync("local.txt", "local divergence\n");
        await repo.CommitAsync("Local divergence");
        var local = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("fetch", "test-origin");
        Assert.Equal(advanced, (await repo.GitAsync("rev-parse", "refs/remotes/test-origin/main")).Trim());
        var request = new GitNetworkRequest(GitNetworkAction.Push, "test-origin", Destination: "main",
            ForceWithLease: true, ExpectedRemoteSha: reviewed);

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.NetworkAsync(repo.Handle, request, CancellationToken.None));

        Assert.StartsWith(advanced + "\t", await repo.GitAsync("ls-remote", "test-origin", "refs/heads/main"), StringComparison.Ordinal);
        Assert.Equal(local, (await repo.GitAsync("rev-parse", "HEAD")).Trim());
        Success(await repo.Client.NetworkAsync(repo.Handle, request with { ExpectedRemoteSha = advanced }, CancellationToken.None));
        Assert.StartsWith(local + "\t", await repo.GitAsync("ls-remote", "test-origin", "refs/heads/main"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GitFlowBranchKind.Release)]
    [InlineData(GitFlowBranchKind.Hotfix)]
    public async Task GitFlowFinishResumesAfterAConflictWithoutRepeatingItsCompletedTag(GitFlowBranchKind kind)
    {
        await using var repo = await Repository.CreateAsync();
        var request = new GitFlowRequest(GitFlowAction.Initialize, kind, "", DevelopBranch: "integration");
        Success(await repo.Client.GitFlowAsync(repo.Handle, request, CancellationToken.None));
        request = request with { Action = GitFlowAction.Start, Name = "1.0.1" };
        Success(await repo.Client.GitFlowAsync(repo.Handle, request, CancellationToken.None));
        await repo.WriteAsync("file.txt", "hotfix edit\n");
        await repo.CommitAsync("Hotfix edit");
        var sourceBranch = (await repo.GitAsync("branch", "--show-current")).Trim();
        await repo.GitAsync("switch", "integration");
        await repo.WriteAsync("file.txt", "development edit\n");
        await repo.CommitAsync("Development edit");
        await repo.GitAsync("switch", sourceBranch);

        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.GitFlowAsync(repo.Handle,
            request with { Action = GitFlowAction.Finish }, CancellationToken.None));
        var tagged = await repo.GitAsync("rev-parse", "1.0.1^{}");
        Assert.Equal(await repo.GitAsync("rev-parse", "main"), tagged);
        var reopened = Success(await repo.Client.OpenRepositoryAsync(BuiltInConnections.Local, repo.Root, CancellationToken.None));
        var conflict = Success(await repo.Client.ReadConflictAsync(reopened, "file.txt", CancellationToken.None));
        Success(await repo.Client.ResolveConflictAsync(reopened,
            new("file.txt", GitConflictResolution.Edited, "resolved hotfix\n", conflict.Fingerprint), CancellationToken.None));
        Success(await repo.Client.ControlOperationAsync(reopened, GitOperationKind.Merge, GitOperationControl.Continue, CancellationToken.None));

        Success(await repo.NewClient().GitFlowAsync(reopened, request with { Action = GitFlowAction.Finish }, CancellationToken.None));

        Assert.Equal(tagged, await repo.GitAsync("rev-parse", "1.0.1^{}"));
        Assert.Equal("integration", (await repo.GitAsync("branch", "--show-current")).Trim());
        Assert.Equal("resolved hotfix\n", await repo.TextAsync("file.txt"));
        Assert.Equal("", await repo.GitAsync("branch", "--list", sourceBranch));
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task GitFlowFinishResumesAfterAnExistingTagIsCorrected()
    {
        await using var repo = await Repository.CreateAsync();
        var baseSha = await repo.GitAsync("rev-parse", "HEAD");
        await repo.GitAsync("tag", "1.0.1");
        var request = new GitFlowRequest(GitFlowAction.Initialize, GitFlowBranchKind.Hotfix, "");
        Success(await repo.Client.GitFlowAsync(repo.Handle, request, CancellationToken.None));
        request = request with { Action = GitFlowAction.Start, Name = "1.0.1" };
        Success(await repo.Client.GitFlowAsync(repo.Handle, request, CancellationToken.None));
        await repo.WriteAsync("fix.txt", "fixed\n");
        await repo.CommitAsync("Hotfix");
        var sourceRevision = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.GitFlowAsync(repo.Handle,
            request with { Action = GitFlowAction.Finish }, CancellationToken.None));
        var pending = Success(await repo.NewClient().ReadGitFlowPendingFinishAsync(repo.Handle, CancellationToken.None));
        Assert.NotNull(pending);
        Assert.Equal(request with { Action = GitFlowAction.Finish }, pending.Request);
        Assert.Equal(sourceRevision, pending.SourceRevision);
        Assert.True(pending.CompletedSteps > 0);
        Assert.True(pending.CompletedSteps < pending.TotalSteps);
        Assert.Equal(baseSha, await repo.GitAsync("rev-parse", "1.0.1"));
        var mergedMain = await repo.GitAsync("rev-parse", "main");
        await repo.GitAsync("tag", "-d", "1.0.1");
        await repo.GitAsync("switch", "-c", "unrelated", baseSha.Trim());
        await repo.WriteAsync("unrelated.txt", "unrelated branch work\n");
        await repo.CommitAsync("Unrelated branch work");
        var unrelated = await repo.GitAsync("rev-parse", "HEAD");

        Success(await repo.NewClient().GitFlowAsync(repo.Handle, request with { Action = GitFlowAction.Finish }, CancellationToken.None));

        Assert.Equal(mergedMain, await repo.GitAsync("rev-parse", "main"));
        Assert.Equal(mergedMain, await repo.GitAsync("rev-parse", "1.0.1^{}"));
        Assert.Equal("tag", (await repo.GitAsync("cat-file", "-t", "1.0.1")).Trim());
        Assert.Equal("fixed\n", await repo.GitAsync("show", "develop:fix.txt"));
        Assert.Equal("", await repo.GitAsync("branch", "--list", "hotfix/1.0.1"));
        Assert.Equal(unrelated, await repo.GitAsync("rev-parse", "unrelated"));
        Assert.Null(Success(await repo.NewClient().ReadGitFlowPendingFinishAsync(repo.Handle, CancellationToken.None)));
    }

    private static IReadOnlyList<GitPatchLineSelection> ChangedLines(GitDiffDocument diff, params string[] text)
    {
        var selection = new List<GitPatchLineSelection>();
        for (var hunk = 0; hunk < diff.Hunks.Count; hunk++)
        {
            for (var line = 0; line < diff.Hunks[hunk].Lines.Count; line++)
            {
                var row = diff.Hunks[hunk].Lines[line];
                if (row.Kind is GitDiffLineKind.Added or GitDiffLineKind.Removed && text.Contains(row.Text, StringComparer.Ordinal))
                {
                    selection.Add(new(hunk, line));
                }
            }
        }

        return selection;
    }

    private static async Task CreateDivergentEditsAsync(Repository repo, string path = "file.txt")
    {
        await repo.GitAsync("switch", "-c", "topic");
        await repo.WriteAsync(path, "topic\n");
        await repo.CommitAsync("Topic edit");
        await repo.GitAsync("switch", "main");
        await repo.WriteAsync(path, "main\n");
        await repo.CommitAsync("Main edit");
    }

    private static T Success<T>(GitResult<T> result)
    {
        Assert.True(result is GitResult<T>.Success, result is GitResult<T>.Failure failure ? failure.Error.Message : "Unknown result");
        return ((GitResult<T>.Success)result).Value;
    }

    private static string? AvailableGpgPath { get; } = new[] { "/opt/homebrew/bin/gpg", "/usr/local/bin/gpg", "/usr/bin/gpg" }.FirstOrDefault(File.Exists);

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class GpgAvailableFactAttribute : FactAttribute
    {
        public GpgAvailableFactAttribute()
        {
            if (AvailableGpgPath is null)
            {
                Skip = "GPG is unavailable; the signing fixture requires a disposable key generator.";
            }
        }
    }

    private sealed class Repository : IAsyncDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("asura-git-advanced-");
        private readonly ConnectionCommandExecutor _executor = new(new Runtime(), new PathConnectionExecutableLocator());

        private Repository()
        {
            Root = Directory.CreateDirectory(Path.Combine(_directory.FullName, "repository")).FullName;
            Handle = new GitRepositoryHandle(BuiltInConnections.Local, Root);
            Client = new GitRepositoryClient(_executor, TimeProvider.System);
        }

        public string Root { get; }
        public string RemoteRoot => Path.Combine(_directory.FullName, "remote.git");
        public GitRepositoryHandle Handle { get; }
        public GitRepositoryClient Client { get; }

        public static async Task<Repository> CreateAsync()
        {
            var repo = new Repository();
            await repo.GitAsync("init", "--initial-branch=main");
            await repo.GitAsync("config", "user.name", "Advanced Workflow Test");
            await repo.GitAsync("config", "user.email", "workflow@example.invalid");
            await repo.GitAsync("config", "commit.gpgSign", "false");
            await repo.GitAsync("config", "tag.gpgSign", "false");
            await repo.GitAsync("config", "core.autocrlf", "false");
            await repo.GitAsync("config", "core.hooksPath", "/dev/null");
            await repo.WriteAsync("file.txt", "base\n");
            await repo.CommitAsync("Base");
            return repo;
        }

        public Task WriteAsync(string path, string text)
        {
            var fullPath = Path.Combine(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return File.WriteAllTextAsync(fullPath, text);
        }

        public Task<string> TextAsync(string path) => File.ReadAllTextAsync(Path.Combine(Root, path));

        public GitRepositoryClient NewClient() => new(_executor, TimeProvider.System);

        public async Task CommitAsync(string message)
        {
            await GitAsync("add", "--all");
            await GitAsync("commit", "-m", message);
        }

        public async Task<string> GitAsync(params string[] arguments)
        {
            var result = await _executor.ExecuteAsync(new ConnectionCommand(BuiltInConnections.Local, "git",
                ["-C", Root, .. arguments], TimeSpan.FromSeconds(15), 1024 * 1024), CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return result.StandardOutput;
        }

        public async Task<string> GitWithInputAsync(string input, params string[] arguments)
        {
            using var material = SecretMaterial.TakeOwnership(System.Text.Encoding.UTF8.GetBytes(input));
            var result = await _executor.ExecuteAsync(new ConnectionCommand(BuiltInConnections.Local, "git",
                ["-C", Root, .. arguments], TimeSpan.FromSeconds(15), 1024 * 1024)
            { StandardInput = material }, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return result.StandardOutput;
        }

        public ValueTask DisposeAsync()
        {
            _directory.Delete(recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Runtime : IConnectionRuntime
    {
        public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => ValueTask.FromResult(
                ConnectionRuntimeResult<ConnectionOpenPlan>.Succeed(new(profile.Id, ConnectionKind.Local,
                    new TerminalLaunchRequest(null), ConnectionAuthenticationMode.None, SshHostKeyPolicy.NotApplicable, ConnectionReconnectMode.NotApplicable)));

        public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
