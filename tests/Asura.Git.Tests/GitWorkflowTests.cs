using System.Runtime.Versioning;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Git.Tests;

[SupportedOSPlatform("macos")]
public sealed class GitWorkflowTests
{
    [Fact]
    public async Task AllRefsHistoryFindsACommitOutsideTheCurrentBranch()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("switch", "--orphan", "disconnected");
        await repo.WriteAsync("other.txt", "other\n");
        await repo.CommitAsync("Unique disconnected message");
        await repo.GitAsync("switch", "main");
        var page = Success(await repo.Client.ReadHistoryAsync(repo.Handle, new GitHistoryQuery(Search: "Unique disconnected"), 0, 10, CancellationToken.None));
        Assert.Equal("Unique disconnected message", Assert.Single(page.Commits).Subject);
    }

    [Fact]
    public async Task ConflictedRebaseCanBeResolvedAndContinuedAfterReopening()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.GitAsync("switch", "-c", "topic");
        await repo.WriteAsync("file.txt", "topic\n");
        await repo.CommitAsync("Topic");
        await repo.GitAsync("switch", "main");
        await repo.WriteAsync("file.txt", "main\n");
        await repo.CommitAsync("Main");
        var destinationRevision = (await repo.GitAsync("rev-parse", "main")).Trim();
        var incomingRevision = (await repo.GitAsync("rev-parse", "topic")).Trim();
        var destinationObject = (await repo.GitAsync("rev-parse", "main:file.txt")).Trim();
        var incomingObject = (await repo.GitAsync("rev-parse", "topic:file.txt")).Trim();
        await repo.GitAsync("switch", "topic");
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.RebaseAsync(repo.Handle, "main", CancellationToken.None));
        var reopened = Success(await repo.Client.OpenRepositoryAsync(BuiltInConnections.Local, repo.Root, CancellationToken.None));
        Assert.Equal(GitOperationKind.Rebase, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);
        var conflict = Success(await repo.Client.ReadConflictAsync(reopened, "file.txt", CancellationToken.None));
        Assert.Equal("main\n", conflict.Current);
        Assert.Equal("topic\n", conflict.Incoming);
        Assert.Equal(destinationRevision, conflict.CurrentRevision);
        Assert.Equal(incomingRevision, conflict.IncomingRevision);
        Assert.Equal(destinationObject, conflict.CurrentObjectId);
        Assert.Equal(incomingObject, conflict.IncomingObjectId);
        Assert.NotNull(conflict.BaseObjectId);
        Success(await repo.Client.ResolveConflictAsync(reopened, new("file.txt", GitConflictResolution.Edited, "resolved\n", conflict.Fingerprint), CancellationToken.None));
        Success(await repo.Client.ControlOperationAsync(reopened, GitOperationKind.Rebase, GitOperationControl.Continue, CancellationToken.None));
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(reopened, CancellationToken.None)).Kind);
        Assert.Equal("resolved\n", await repo.TextAsync("file.txt"));
    }

    [Theory]
    [InlineData(GitPatchAction.Stage)]
    [InlineData(GitPatchAction.Discard)]
    public async Task PartialSelectionChangesOnlyTheChosenEdit(GitPatchAction action)
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("file.txt", "alpha\nbeta\ngamma\n");
        await repo.CommitAsync("Base text");
        await repo.WriteAsync("file.txt", "ALPHA\nbeta\nGAMMA\n");
        var request = new GitDiffRequest(GitDiffArea.Worktree, "file.txt");
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));
        var selection = new[] { new GitPatchLineSelection(0, 0), new GitPatchLineSelection(0, 1) };
        Success(await repo.Client.ApplyPartialPatchAsync(repo.Handle, new(request, diff.RawPatch, selection, action), CancellationToken.None));
        if (action == GitPatchAction.Stage)
        {
            Assert.Equal("ALPHA\nbeta\ngamma", (await repo.GitAsync("show", ":file.txt")).TrimEnd('\n'));
            Assert.Equal("ALPHA\nbeta\nGAMMA\n", await repo.TextAsync("file.txt"));
        }
        else
        {
            Assert.Equal("alpha\nbeta\nGAMMA\n", await repo.TextAsync("file.txt"));
        }
    }

    [Fact]
    public async Task PartialUnstageKeepsTheOtherStagedEditAndWorktreeBytes()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("file.txt", "alpha\nbeta\ngamma\n");
        await repo.CommitAsync("Base text");
        await repo.WriteAsync("file.txt", "ALPHA\nbeta\nGAMMA\n");
        await repo.GitAsync("add", "file.txt");
        var request = new GitDiffRequest(GitDiffArea.Index, "file.txt");
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));
        Success(await repo.Client.ApplyPartialPatchAsync(repo.Handle, new(request, diff.RawPatch,
            [new(0, 0), new(0, 1)], GitPatchAction.Unstage), CancellationToken.None));
        Assert.Equal("alpha\nbeta\nGAMMA\n", await repo.GitAsync("show", ":file.txt"));
        Assert.Equal("ALPHA\nbeta\nGAMMA\n", await repo.TextAsync("file.txt"));
    }

    [Fact]
    public async Task StalePartialSelectionIsRejectedWithoutChangingTheIndex()
    {
        await using var repo = await Repository.CreateAsync();
        await repo.WriteAsync("file.txt", "first change\n");
        var request = new GitDiffRequest(GitDiffArea.Worktree, "file.txt");
        var diff = Success(await repo.Client.ReadDiffAsync(repo.Handle, request, CancellationToken.None));
        await repo.WriteAsync("file.txt", "external change\n");
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ApplyPartialPatchAsync(repo.Handle,
            new(request, diff.RawPatch, [new(0, 0), new(0, 1)], GitPatchAction.Stage), CancellationToken.None));
        Assert.Equal("", await repo.GitAsync("diff", "--cached", "--name-only"));
    }

    [Fact]
    public async Task InteractiveRebaseReordersFixesUpAndRewordsCommits()
    {
        await using var repo = await Repository.CreateAsync();
        var baseSha = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("one.txt", "one\n");
        await repo.CommitAsync("One");
        var one = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        await repo.WriteAsync("two.txt", "two\n");
        await repo.CommitAsync("Two");
        var two = (await repo.GitAsync("rev-parse", "HEAD")).Trim();
        Success(await repo.Client.InteractiveRebaseAsync(repo.Handle,
            new(baseSha, [new(two, "Two", GitRebaseAction.Reword, "New 'message' $()\n\nKept body"),
                new(one, "One", GitRebaseAction.Fixup)], AutoStash: false, UpdateRefs: false), CancellationToken.None));
        Assert.Equal("New 'message' $()", (await repo.GitAsync("log", "-1", "--format=%s")).Trim());
        Assert.Equal("Kept body", (await repo.GitAsync("log", "-1", "--format=%b")).Trim());
        Assert.Equal("2", (await repo.GitAsync("rev-list", "--count", "HEAD")).Trim());
        Assert.Equal("one\n", await repo.TextAsync("one.txt"));
    }

    [Fact]
    public async Task WorktreeStateUsesTheLinkedWorktreesOwnMetadata()
    {
        await using var repo = await Repository.CreateAsync();
        var path = Path.Combine(repo.Root, "linked checkout");
        Success(await repo.Client.ManageWorktreeAsync(repo.Handle, new(GitWorktreeAction.Add, path, "HEAD", "linked"), CancellationToken.None));
        var linked = Success(await repo.Client.OpenRepositoryAsync(BuiltInConnections.Local, path, CancellationToken.None));
        Assert.Equal(GitOperationKind.Normal, Success(await repo.Client.ReadOperationAsync(linked, CancellationToken.None)).Kind);
        Success(await repo.Client.ManageWorktreeAsync(repo.Handle, new(GitWorktreeAction.Lock, path), CancellationToken.None));
        Assert.IsType<GitResult<GitUnit>.Failure>(await repo.Client.ManageWorktreeAsync(repo.Handle, new(GitWorktreeAction.Remove, path), CancellationToken.None));
        Success(await repo.Client.ManageWorktreeAsync(repo.Handle, new(GitWorktreeAction.Unlock, path), CancellationToken.None));
        Success(await repo.Client.ManageWorktreeAsync(repo.Handle, new(GitWorktreeAction.Remove, path), CancellationToken.None));
    }

    private static T Success<T>(GitResult<T> result)
    {
        Assert.True(result is GitResult<T>.Success, result is GitResult<T>.Failure failure ? failure.Error.Message : "Unknown result");
        return ((GitResult<T>.Success)result).Value;
    }

    private sealed class Repository : IAsyncDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("asura-git-workflow-");
        private readonly ConnectionCommandExecutor _executor = new(new Runtime(), new PathConnectionExecutableLocator());

        private Repository()
        {
            Handle = new GitRepositoryHandle(BuiltInConnections.Local, Root);
            Client = new GitRepositoryClient(_executor, TimeProvider.System);
        }

        public string Root => _directory.FullName;
        public GitRepositoryHandle Handle { get; }
        public GitRepositoryClient Client { get; }

        public static async Task<Repository> CreateAsync()
        {
            var repo = new Repository();
            await repo.GitAsync("init", "--initial-branch=main");
            await repo.GitAsync("config", "user.name", "Workflow Test");
            await repo.GitAsync("config", "user.email", "workflow@example.invalid");
            await repo.GitAsync("config", "commit.gpgSign", "false");
            await repo.WriteAsync("file.txt", "base\n");
            await repo.CommitAsync("Base");
            return repo;
        }

        public Task WriteAsync(string path, string text) => File.WriteAllTextAsync(Path.Combine(Root, path), text);
        public Task<string> TextAsync(string path) => File.ReadAllTextAsync(Path.Combine(Root, path));
        public async Task CommitAsync(string message) { await GitAsync("add", "--all"); await GitAsync("commit", "-m", message); }

        public async Task<string> GitAsync(params string[] arguments)
        {
            var result = await _executor.ExecuteAsync(new ConnectionCommand(BuiltInConnections.Local, "git",
                ["-C", Root, .. arguments], TimeSpan.FromSeconds(15), 1024 * 1024), CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return result.StandardOutput;
        }

        public ValueTask DisposeAsync() { _directory.Delete(recursive: true); return ValueTask.CompletedTask; }
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
