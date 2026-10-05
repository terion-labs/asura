using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.Git.Tests;

public sealed class GitLfsWorkflowTests
{
    private static readonly GitRepositoryHandle Repository = new(BuiltInConnections.Local, "/repo");
    private const string Locks = """
        {"ours":[{"id":"ours-id","path":"z.png","owner":{"name":"Current user"},"locked_at":"2026-10-04T10:00:00Z"}],
         "theirs":[{"id":"theirs-id","path":"資料.png","owner":{"name":"Other user"}}]}
        """;

    [Fact]
    public async Task LocksAreServerVerifiedBeforeReadingCachedOwnership()
    {
        var executor = new Executor();
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var locks = Assert.IsType<GitResult<IReadOnlyList<GitLfsLock>>.Success>(await client.ReadLfsLocksAsync(
            Repository, "team", CancellationToken.None)).Value;

        Assert.Equal(2, locks.Count);
        Assert.Equal(new GitLfsLock("ours-id", "z.png", "Current user", GitLfsLockOwnership.CurrentUser,
            new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero)), locks[0]);
        Assert.Equal(new GitLfsLock("theirs-id", "資料.png", "Other user", GitLfsLockOwnership.OtherUser, null), locks[1]);
        var reads = executor.Commands.Where(command => command.Arguments.Contains("locks", StringComparer.Ordinal)).ToArray();
        Assert.Equal(2, reads.Length);
        Assert.DoesNotContain("--json", reads[0].Arguments, StringComparer.Ordinal);
        Assert.DoesNotContain("--cached", reads[0].Arguments, StringComparer.Ordinal);
        Assert.Contains("--verify", reads[0].Arguments, StringComparer.Ordinal);
        Assert.Contains("--cached", reads[1].Arguments, StringComparer.Ordinal);
        Assert.Contains("--json", reads[1].Arguments, StringComparer.Ordinal);
    }

    [Fact]
    public async Task StatusPreservesRenamedPathsAndSortsFilesForPresentation()
    {
        var executor = new Executor
        {
            Status = """
                {"files":{"資料.png":{"status":"M"},"a.png":{"status":"R","from":"old a.png"}}}
                """,
        };
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var files = Assert.IsType<GitResult<IReadOnlyList<GitLfsStatusFile>>.Success>(await client.ReadLfsStatusAsync(
            Repository, CancellationToken.None)).Value;

        Assert.Equal([new("a.png", "R", "old a.png"), new GitLfsStatusFile("資料.png", "M", null)], files);
        Assert.Contains("--json", Assert.Single(executor.Commands).Arguments, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PullOverridesLiteralModeForItsInternalAttributePathspecs()
    {
        var executor = new Executor();
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        Assert.IsType<GitResult<GitTaskOutput>.Success>(await client.RunRepositoryTaskAsync(
            Repository, new(GitRepositoryTask.LfsPull), CancellationToken.None));

        var arguments = Assert.Single(executor.Commands).Arguments;
        Assert.Equal(["--literal-pathspecs", "--no-literal-pathspecs"],
            arguments.Where(argument => argument is "--literal-pathspecs" or "--no-literal-pathspecs"), StringComparer.Ordinal);
        Assert.Equal(["lfs", "pull"], arguments.TakeLast(2), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("\"images/photo \\316\\273.png\"", "images/photo λ.png")]
    [InlineData("\"images/tab\\tname\\nrow.png\"", "images/tab\tname\nrow.png")]
    [InlineData("\"images/quote\\\"and\\\\slash.png\"", "images/quote\"and\\slash.png")]
    [InlineData("\"images/control\\a\\b\\f\\r\\v.png\"", "images/control\a\b\f\r\v.png")]
    public async Task StatusDecodesGitQuotedJsonPathsAndRenameSources(string encodedPath, string expectedPath)
    {
        var jsonPath = JsonEncodedText.Encode(encodedPath).ToString();
        var executor = new Executor { Status = "{\"files\":{\"" + jsonPath + "\":{\"status\":\"R\",\"from\":\"" + jsonPath + "\"}}}" };
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var files = Assert.IsType<GitResult<IReadOnlyList<GitLfsStatusFile>>.Success>(await client.ReadLfsStatusAsync(
            Repository, CancellationToken.None)).Value;

        Assert.Equal(new(expectedPath, "R", expectedPath), Assert.Single(files));
    }

    [Theory]
    [InlineData("\"images/invalid\\q.png\"")]
    [InlineData("\"images/invalid\\777.png\"")]
    [InlineData("\"images/incomplete\\\"")]
    public async Task InvalidGitQuotedStatusPathReturnsTypedFailure(string encodedPath)
    {
        var jsonPath = JsonEncodedText.Encode(encodedPath).ToString();
        var executor = new Executor { Status = "{\"files\":{\"" + jsonPath + "\":{\"status\":\"A\"}}}" };
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var failure = Assert.IsType<GitResult<IReadOnlyList<GitLfsStatusFile>>.Failure>(await client.ReadLfsStatusAsync(
            Repository, CancellationToken.None));

        Assert.Equal(GitErrorCode.InvalidResponse, failure.Error.Code);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("{\"files\":[]}")]
    [InlineData("{\"files\":{\"a.png\":{\"status\":true}}}")]
    public async Task InvalidStatusIsAReadableTypedFailureInsteadOfAnEmptyList(string status)
    {
        var executor = new Executor { Status = status };
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var failure = Assert.IsType<GitResult<IReadOnlyList<GitLfsStatusFile>>.Failure>(await client.ReadLfsStatusAsync(
            Repository, CancellationToken.None));

        Assert.Equal(GitErrorCode.InvalidResponse, failure.Error.Code);
        Assert.Single(executor.Commands);
    }

    [Fact]
    public async Task FailedLockVerificationCannotFallBackToAStaleCachedList()
    {
        var executor = new Executor { VerificationFails = true };
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        Assert.IsType<GitResult<IReadOnlyList<GitLfsLock>>.Failure>(await client.ReadLfsLocksAsync(
            Repository, "team", CancellationToken.None));

        Assert.DoesNotContain(executor.Commands, command => command.Arguments.Contains("--cached", StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnlockRejectsAReplacementLockAtTheSamePath()
    {
        var executor = new Executor();
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        Assert.IsType<GitResult<GitUnit>.Failure>(await client.ManageLfsLockAsync(Repository,
            new(GitLfsLockAction.Unlock, "z.png", "team", ExpectedLockId: "old-id"), CancellationToken.None));

        Assert.DoesNotContain(executor.Commands, command => command.Arguments.Contains("unlock", StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnlockingAnotherUsersReviewedLockRequiresForceAndItsExactId(bool force)
    {
        var executor = new Executor();
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var result = await client.ManageLfsLockAsync(Repository,
            new(GitLfsLockAction.Unlock, "資料.png", "team", Force: force, ExpectedLockId: "theirs-id"), CancellationToken.None);

        if (force)
        {
            Assert.IsType<GitResult<GitUnit>.Success>(result);
            var unlock = Assert.Single(executor.Commands, command => command.Arguments.Contains("unlock", StringComparer.Ordinal));
            Assert.Contains("--force", unlock.Arguments, StringComparer.Ordinal);
            Assert.Contains("--id=theirs-id", unlock.Arguments, StringComparer.Ordinal);
            Assert.Contains("--remote=team", unlock.Arguments, StringComparer.Ordinal);
        }
        else
        {
            Assert.IsType<GitResult<GitUnit>.Failure>(result);
            Assert.DoesNotContain(executor.Commands, command => command.Arguments.Contains("unlock", StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("team\n", "HEAD", "team")]
    [InlineData("origin\nteam\n", "team/main", "team")]
    [InlineData("origin\nteam\n", "HEAD", null)]
    public async Task DownloadUsesTheRevisionOrOnlyRemoteAndRejectsAmbiguity(string remotes, string revision, string? expectedRemote)
    {
        var executor = new Executor { Remotes = remotes };
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var result = await client.DownloadLfsObjectsAsync(Repository, revision, CancellationToken.None);

        if (expectedRemote is null)
        {
            Assert.IsType<GitResult<GitUnit>.Failure>(result);
            Assert.DoesNotContain(executor.Commands, command => command.Arguments.Contains("fetch", StringComparer.Ordinal));
        }
        else
        {
            Assert.IsType<GitResult<GitUnit>.Success>(result);
            var fetch = Assert.Single(executor.Commands, command => command.Arguments.Contains("fetch", StringComparer.Ordinal));
            Assert.Equal(["lfs", "fetch", expectedRemote, revision], fetch.Arguments.TakeLast(4), StringComparer.Ordinal);
        }
    }

    private sealed class Executor : IConnectionCommandExecutor
    {
        public List<ConnectionCommand> Commands { get; } = [];
        public string Remotes { get; init; } = "team\n";
        public string Status { get; init; } = "{\"files\":{}}";
        public bool VerificationFails { get; init; }

        public ValueTask<ConnectionCommandResult> ExecuteAsync(ConnectionCommand request, CancellationToken cancellationToken)
        {
            Commands.Add(request);
            var output = request.Arguments.Contains("remote", StringComparer.Ordinal) ? Remotes
                : request.Arguments.Contains("status", StringComparer.Ordinal) ? Status
                : request.Arguments.Contains("symbolic-ref", StringComparer.Ordinal) ? "main\n"
                : request.Arguments.Contains("--cached", StringComparer.Ordinal) ? Locks
                : request.Arguments.Contains("locks", StringComparer.Ordinal) ? "Verified locks successfully, with plain output.\n" : "";
            var fails = VerificationFails && request.Arguments.Contains("locks", StringComparer.Ordinal)
                && !request.Arguments.Contains("--cached", StringComparer.Ordinal);
            return ValueTask.FromResult(new ConnectionCommandResult(ConnectionCommandOutcome.Exited, fails ? 128 : 0, output,
                fails ? "LFS verification failed" : ""));
        }

        public ValueTask<ConnectionBinaryCommandResult> ExecuteBinaryAsync(ConnectionBinaryCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionStreamingCommandResult<T>> ExecuteStreamingAsync<T>(ConnectionBinaryCommand request,
            Func<Stream, CancellationToken, ValueTask<T>> consumeOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
