using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Git.Tests;

public sealed class GitCredentialTests
{
    private static readonly GitRepositoryHandle Repository = new(BuiltInConnections.Local, "/repo");
    private static readonly ConnectionCommandResult Success = new(ConnectionCommandOutcome.Exited, 0, "");
    private static readonly ConnectionCommandResult Missing = new(ConnectionCommandOutcome.Exited, 128, "",
        "fatal: could not read Username for 'https://git.example': No such device or address");

    [Theory]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("push")]
    public async Task MissingCredentialsPromptAndRetryTheSameConnectionWithoutSecretsInArguments(string operation)
    {
        var executor = new Executor(Missing, Success);
        var prompt = new Prompt(false);
        var client = new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt);
        var result = operation switch
        {
            "fetch" => await client.FetchRemoteAsync(Repository, "origin", CancellationToken.None),
            "pull" => await client.PullAsync(Repository, CancellationToken.None),
            _ => await client.PushAsync(Repository, CancellationToken.None),
        };
        Assert.IsType<GitResult<GitUnit>.Success>(result);
        Assert.Single(prompt.Requests);
        Assert.Equal("https://git.example/", prompt.Requests[0].Remote.AbsoluteUri);
        var retry = executor.Commands[1];
        Assert.Same(Repository.Connection, retry.Connection);
        Assert.Contains(operation, retry.Arguments, StringComparer.Ordinal);
        Assert.DoesNotContain("test-user", string.Join(' ', retry.Arguments), StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", retry.ToString(), StringComparison.Ordinal);
        Assert.Equal("test-user\ntest-token\n", executor.Inputs[1]);
        Assert.True(retry.StandardInput!.IsDisposed);
    }

    [Theory]
    [InlineData("fatal: could not read Password for 'https://alice@git.example': terminal prompts disabled")]
    [InlineData("fatal: Authentication failed for 'https://alice:old-token@git.example/repo'")]
    public async Task PasswordChallengesKeepTheUsernameButNeverPassTheOldPasswordToThePrompt(string error)
    {
        var executor = new Executor(new ConnectionCommandResult(ConnectionCommandOutcome.Exited, 128, "", error), Success);
        var prompt = new Prompt(false);
        var result = await new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt)
            .PushAsync(Repository, CancellationToken.None);
        Assert.IsType<GitResult<GitUnit>.Success>(result);
        Assert.Equal("alice", Assert.Single(prompt.Requests).Remote.UserInfo);
        Assert.DoesNotContain("old-token", executor.Commands[1].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelDoesNotRetryOrWriteCredentials()
    {
        var executor = new Executor(Missing);
        var prompt = new Prompt(false) { Cancel = true };
        var client = new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt);
        var result = await client.PushAsync(Repository, CancellationToken.None);
        Assert.Equal(GitErrorCode.Cancelled, Assert.IsType<GitResult<GitUnit>.Failure>(result).Error.Code);
        Assert.Single(executor.Commands);
    }

    [Theory]
    [InlineData("fatal: unable to access 'https://git.example': Could not resolve host")]
    [InlineData("fatal: Permission denied (publickey).")]
    [InlineData("fatal: could not read Username for 'file:///tmp/repo'")]
    public async Task OtherFailuresDoNotPrompt(string error)
    {
        var executor = new Executor(new ConnectionCommandResult(ConnectionCommandOutcome.Exited, 128, "", error));
        var prompt = new Prompt(false);
        var result = await new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt)
            .PushAsync(Repository, CancellationToken.None);
        Assert.IsType<GitResult<GitUnit>.Failure>(result);
        Assert.Empty(prompt.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveIsOptInAndReusesTheVaultAfterSuccess(bool save)
    {
        using var vault = new PersistentTestVault();
        var executor = new Executor(Missing, Success, Missing, Success);
        var prompt = new Prompt(save);
        var client = new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt, secretVault: vault);
        Assert.IsType<GitResult<GitUnit>.Success>(await client.PushAsync(Repository, CancellationToken.None));
        Assert.IsType<GitResult<GitUnit>.Success>(await client.PushAsync(Repository, CancellationToken.None));
        Assert.Equal(save ? 1 : 2, prompt.Requests.Count);
        Assert.Equal(save ? 1 : 0, vault.Writes);
        Assert.All(prompt.Requests, request => Assert.True(request.CanSave));
    }

    [Fact]
    public async Task SavedCredentialsBelongToTheIsolatedWorkspaceEvenWhenRepositoryPathsMatch()
    {
        using var vault = new PersistentTestVault();
        var executor = new Executor(Missing, Success, Missing, Success, Missing, Success);
        var prompt = new Prompt(true);
        var firstId = WorkspaceId.New();
        var first = new GitRepositoryClient(executor, TimeProvider.System,
            credentialPrompt: prompt, secretVault: vault, credentialWorkspaceId: firstId);
        var second = new GitRepositoryClient(executor, TimeProvider.System,
            credentialPrompt: prompt, secretVault: vault, credentialWorkspaceId: WorkspaceId.New());
        Assert.IsType<GitResult<GitUnit>.Success>(await first.PushAsync(Repository, CancellationToken.None));
        Assert.IsType<GitResult<GitUnit>.Success>(await second.PushAsync(Repository, CancellationToken.None));
        var reopened = new GitRepositoryClient(executor, TimeProvider.System,
            credentialPrompt: prompt, secretVault: vault, credentialWorkspaceId: firstId);
        Assert.IsType<GitResult<GitUnit>.Success>(await reopened.PushAsync(Repository, CancellationToken.None));
        Assert.Equal(2, prompt.Requests.Count);
        Assert.Equal(2, vault.Writes);
    }

    [Fact]
    public async Task FailedAuthenticationIsNotStoredAndDoesNotLoopOrExposeOutput()
    {
        using var vault = new PersistentTestVault();
        var executor = new Executor(Missing, new ConnectionCommandResult(ConnectionCommandOutcome.Exited, 128, "test-token",
            "fatal: Authentication failed for 'https://test-user:test-token@git.example/repo'"));
        var prompt = new Prompt(true);
        var result = await new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt, secretVault: vault)
            .PushAsync(Repository, CancellationToken.None);
        var error = Assert.IsType<GitResult<GitUnit>.Failure>(result).Error;
        Assert.Equal(GitErrorCode.AuthenticationRequired, error.Code);
        Assert.DoesNotContain("test-token", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, vault.Writes);
        Assert.Single(prompt.Requests);
        Assert.Equal(2, executor.Commands.Count);
    }

    [Fact]
    public async Task RejectedSavedCredentialsCanBeReplacedAfterSuccessfulRetry()
    {
        using var vault = new PersistentTestVault();
        var executor = new Executor(Missing, Success, Missing, Missing, Success);
        var prompt = new Prompt(true);
        var client = new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt, secretVault: vault);
        Assert.IsType<GitResult<GitUnit>.Success>(await client.PushAsync(Repository, CancellationToken.None));
        Assert.IsType<GitResult<GitUnit>.Success>(await client.PushAsync(Repository, CancellationToken.None));
        Assert.True(prompt.Requests[1].Rejected);
        Assert.Equal(2, vault.Writes);
    }

    [Fact]
    public async Task StorageFailureDoesNotInviteRepeatingASuccessfulPush()
    {
        using var vault = new PersistentTestVault { FailWrites = true };
        var executor = new Executor(Missing, Success);
        var result = await new GitRepositoryClient(executor, TimeProvider.System,
            credentialPrompt: new Prompt(true), secretVault: vault).PushAsync(Repository, CancellationToken.None);
        var error = Assert.IsType<GitResult<GitUnit>.Failure>(result).Error;
        Assert.Equal(GitErrorCode.CredentialStorageFailed, error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(2, executor.Commands.Count);
    }

    [Theory]
    [InlineData("user\nextra", "token")]
    [InlineData("user", "token\rmore")]
    [InlineData("user", "token\0more")]
    public void CredentialsRejectProtocolInjection(string username, string password) =>
        Assert.Throws<ArgumentException>(() => GitCredentials.Create(username, password, false));

    private sealed class Prompt(bool save) : IGitCredentialPrompt
    {
        public bool Cancel { get; init; }
        public List<(Uri Remote, bool CanSave, bool Rejected)> Requests { get; } = [];
        public ValueTask<GitCredentials?> RequestAsync(Uri remote, bool canSave, bool rejected, CancellationToken cancellationToken)
        {
            Requests.Add((remote, canSave, rejected));
            return ValueTask.FromResult(Cancel ? null : GitCredentials.Create("test-user", "test-token", save));
        }
    }

    private sealed class Executor(params ConnectionCommandResult[] results) : IConnectionCommandExecutor
    {
        public List<ConnectionCommand> Commands { get; } = [];
        public List<string?> Inputs { get; } = [];
        public ValueTask<ConnectionCommandResult> ExecuteAsync(ConnectionCommand request, CancellationToken cancellationToken)
        {
            Commands.Add(request);
            if (request.StandardInput is { } input)
            {
                var bytes = new byte[input.Length];
                input.CopyTo(bytes);
                Inputs.Add(Encoding.UTF8.GetString(bytes));
            }
            else
            {
                Inputs.Add(null);
            }
            return ValueTask.FromResult(results[Commands.Count - 1]);
        }
        public ValueTask<ConnectionBinaryCommandResult> ExecuteBinaryAsync(ConnectionBinaryCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionStreamingCommandResult<T>> ExecuteStreamingAsync<T>(ConnectionBinaryCommand request,
            Func<Stream, CancellationToken, ValueTask<T>> consumeOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PersistentTestVault : ISecretVault
    {
        private readonly InMemorySecretVault _vault = new();
        public int Writes { get; private set; }
        public bool FailWrites { get; init; }
        public SecretVaultAvailability Availability => _vault.Availability with { Persistence = SecretVaultPersistenceKind.OsProtectedPersistent };
        public ValueTask<SecretVaultResult<SecretMetadata>> CreateAsync(CreateSecretRequest request, SecretMaterial material, CancellationToken cancellationToken)
        {
            Writes++;
            return FailWrites
                ? ValueTask.FromResult(SecretVaultResult<SecretMetadata>.Fail(SecretVaultError.Create(SecretVaultErrorCode.Unavailable)))
                : _vault.CreateAsync(request, material, cancellationToken);
        }
        public ValueTask<SecretVaultResult<SecretMetadata>> ReplaceAsync(ReplaceSecretRequest request, SecretMaterial material, CancellationToken cancellationToken)
        {
            Writes++;
            return _vault.ReplaceAsync(request, material, cancellationToken);
        }
        public ValueTask<SecretVaultResult<SecretMaterial>> ResolveAsync(ResolveSecretRequest request, CancellationToken cancellationToken) => _vault.ResolveAsync(request, cancellationToken);
        public ValueTask<SecretVaultResult<SecretMetadata>> RelabelAsync(RelabelSecretRequest request, CancellationToken cancellationToken) => _vault.RelabelAsync(request, cancellationToken);
        public ValueTask<SecretVaultResult<Unit>> DeleteAsync(DeleteSecretRequest request, CancellationToken cancellationToken) => _vault.DeleteAsync(request, cancellationToken);
        public ValueTask<SecretVaultResult<SecretMetadata>> GetMetadataAsync(GetSecretMetadataRequest request, CancellationToken cancellationToken) => _vault.GetMetadataAsync(request, cancellationToken);
        public ValueTask<SecretVaultResult<IReadOnlyList<SecretMetadata>>> ListMetadataAsync(ListSecretMetadataRequest request, CancellationToken cancellationToken) => _vault.ListMetadataAsync(request, cancellationToken);
        public void Dispose() => _vault.Dispose();
    }
}
