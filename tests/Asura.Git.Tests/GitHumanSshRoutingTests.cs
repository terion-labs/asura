using System.Text;
using Asura.Application;
using Asura.Core;

namespace Asura.Git.Tests;

public sealed class GitHumanSshRoutingTests
{
    [Fact]
    public async Task HistoryConflictResolutionAndCustomTasksRunOnTheSelectedSshTargetAsItsRepositoryOwner()
    {
        var local = BuiltInConnections.Local;
        var connection = new ConnectionProfile(new("ssh.human-workflow"), local.SchemaVersion, "SSH fixture",
            new ConnectionEndpoint.Ssh("git.example.test", username: "operator"), new ConnectionAuthentication.SshAgent(),
            local.Startup, local.KeepAlive, SshHostKeyPolicy.Strict);
        const string root = "/srv/repo λ";
        const string executable = "/custom tools/git";
        var repository = new GitRepositoryHandle(connection, root, RunAsUser: "repository-owner") { Executable = executable };
        var executor = new Executor();
        var client = new GitRepositoryClient(executor, TimeProvider.System);
        Assert.IsType<GitResult<GitCommitPage>.Success>(await client.ReadHistoryAsync(repository,
            new(AllRefs: false, Revision: "HEAD"), 0, 10, CancellationToken.None));
        var conflict = Assert.IsType<GitResult<GitConflictContent>.Success>(await client.ReadConflictAsync(
            repository, "conflict λ.txt", CancellationToken.None)).Value;
        Assert.Equal("current\n", conflict.Current);

        Assert.IsType<GitResult<GitUnit>.Success>(await client.ResolveConflictAsync(repository,
            new("conflict λ.txt", GitConflictResolution.Edited, "resolved λ\n", conflict.Fingerprint), CancellationToken.None));
        Assert.IsType<GitResult<GitTaskOutput>.Success>(await client.RunCustomCommandAsync(repository,
            new("/bin/cat", ["relative file"]), CancellationToken.None));
        Assert.IsType<GitResult<GitTaskOutput>.Success>(await client.RunRepositoryTaskAsync(repository,
            new(GitRepositoryTask.IgnorePattern, "generated/"), CancellationToken.None));

        Assert.All(executor.Commands, command =>
        {
            Assert.Same(connection, command.Connection);
            Assert.Equal("sudo", command.Executable);
            Assert.Equal(["-n", "-u", "repository-owner", "-H", "--"], command.Arguments.Take(5), StringComparer.Ordinal);
            Assert.Contains(command.Arguments, argument => argument == root || argument == root + "/.git");
            if (command.Arguments[5] == "/bin/sh")
            {
                Assert.DoesNotContain(root, command.Arguments[7], StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(executable, command.Arguments[5]);
            }
        });
        Assert.Contains("resolved λ\n", executor.Inputs, StringComparer.Ordinal);
        Assert.Contains("\ngenerated/\n", executor.Inputs, StringComparer.Ordinal);
        Assert.Contains(executor.Commands, command => command.Arguments.Contains("relative file", StringComparer.Ordinal));
    }

    private sealed class Executor : IConnectionCommandExecutor
    {
        public List<ConnectionCommand> Commands { get; } = [];
        public List<string> Inputs { get; } = [];

        public ValueTask<ConnectionCommandResult> ExecuteAsync(ConnectionCommand request, CancellationToken cancellationToken)
        {
            Commands.Add(request);
            if (request.StandardInput is { } material)
            {
                var bytes = new byte[material.Length];
                material.CopyTo(bytes);
                Inputs.Add(Encoding.UTF8.GetString(bytes));
            }
            var output = request.Arguments.Contains("--absolute-git-dir", StringComparer.Ordinal) ? "/srv/repo λ/.git\n"
                : request.Arguments.Any(argument => argument.Contains("$directory/rebase-merge", StringComparison.Ordinal)) ? "Normal"
                : request.Arguments.Contains("HEAD^{commit}", StringComparer.Ordinal) ? new string('c', 40) + "\n"
                : request.Arguments.Contains("ls-files", StringComparer.Ordinal)
                ? "100644 " + new string('a', 40) + " 2\tconflict λ.txt\0"
                : request.Arguments.Contains("cat-file", StringComparer.Ordinal) ? "current\n"
                : request.Arguments.Any(argument => argument.Contains("hash-object", StringComparison.Ordinal)) ? new string('b', 40) + "\n" : "";
            return ValueTask.FromResult(new ConnectionCommandResult(ConnectionCommandOutcome.Exited, 0, output));
        }

        public ValueTask<ConnectionBinaryCommandResult> ExecuteBinaryAsync(ConnectionBinaryCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionStreamingCommandResult<T>> ExecuteStreamingAsync<T>(ConnectionBinaryCommand request,
            Func<Stream, CancellationToken, ValueTask<T>> consumeOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
