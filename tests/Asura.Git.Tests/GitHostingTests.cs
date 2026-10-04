using System.Net;
using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Git.Tests;

public sealed class GitHostingTests
{
    private const string Token = "synthetic-hosting-token";
    private static readonly Uri GitHubApi = new("https://api.github.example/");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GitHubRepositoriesUseTheRequestedPageAndAuthenticatedEndpoint(bool starred)
    {
        var prompt = new Prompt();
        var requests = new List<Request>();
        var client = Client(prompt, (_, request, _) =>
        {
            requests.Add(Request.From(request));
            return Task.FromResult(Response(HttpStatusCode.OK,
                """
                [{"full_name":"owner/дані","clone_url":"https://github.example/owner/data.git","ssh_url":"git@github.example:owner/data.git","html_url":"https://github.example/owner/data","private":true}]
                """));
        });

        var result = Success(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, null, 3, starred, CancellationToken.None));

        Assert.Equal(3, result.Page);
        Assert.False(result.HasMore);
        Assert.Equal(new GitHostedRepository("owner/дані", "https://github.example/owner/data.git",
            "git@github.example:owner/data.git", "https://github.example/owner/data", true), Assert.Single(result.Repositories));
        var request = Assert.Single(requests);
        Assert.Equal(starred ? "/user/starred" : "/user/repos", request.Url.AbsolutePath);
        Assert.Contains("page=3", request.Url.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer " + Token, request.Authorization);
        Assert.Null(request.PrivateToken);
        Assert.Equal(1, prompt.Calls);
    }

    [Fact]
    public async Task GitLabRepositoriesPreserveTheApiPrefixAndUseItsTokenHeader()
    {
        var requests = new List<Request>();
        var client = Client(new Prompt(), (_, request, _) =>
        {
            requests.Add(Request.From(request));
            return Task.FromResult(Response(HttpStatusCode.OK,
                """
                [{"path_with_namespace":"team/repo","http_url_to_repo":"https://gitlab.example/team/repo.git","ssh_url_to_repo":"git@gitlab.example:team/repo.git","web_url":"https://gitlab.example/team/repo","visibility":"private"}]
                """));
        });

        var result = Success(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitLab, new Uri("https://gitlab.example/api/v4"), null, 2, true, CancellationToken.None));

        Assert.Equal(new GitHostedRepository("team/repo", "https://gitlab.example/team/repo.git",
            "git@gitlab.example:team/repo.git", "https://gitlab.example/team/repo", true), Assert.Single(result.Repositories));
        var request = Assert.Single(requests);
        Assert.Equal("/api/v4/projects", request.Url.AbsolutePath);
        Assert.Contains("membership=true", request.Url.Query, StringComparison.Ordinal);
        Assert.Contains("starred=true", request.Url.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", request.Url.Query, StringComparison.Ordinal);
        Assert.Equal(Token, request.PrivateToken);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsingTheRepositoryList()
    {
        var client = Client(new Prompt(), (_, _, _) => Task.FromResult(Response(HttpStatusCode.OK, new string('x', 4 * 1024 * 1024 + 1))));

        var error = Failure(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, null, 1, false, CancellationToken.None));

        Assert.Contains("limit", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RedirectsAndServerFailuresDoNotRetryOrExposeResponseContent(HttpStatusCode status)
    {
        var calls = 0;
        var client = Client(new Prompt(), (_, _, _) =>
        {
            calls++;
            var response = Response(status, Token);
            response.Headers.Location = new Uri("https://different.example/token-receiver");
            return Task.FromResult(response);
        });

        var error = Failure(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, null, 1, false, CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("different.example", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedTokensPromptOnlyOnceMoreAndAreNeverSaved(HttpStatusCode status)
    {
        using var vault = new PersistentVault();
        var prompt = new Prompt { Save = true };
        var calls = 0;
        var client = Client(prompt, (_, _, _) =>
        {
            calls++;
            return Task.FromResult(Response(status, Token));
        }, vault);

        var error = Failure(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, null, 1, false, CancellationToken.None));

        Assert.Equal(GitErrorCode.AuthenticationRequired, error.Code);
        Assert.Equal(2, prompt.Calls);
        Assert.Equal([false, true], prompt.Rejected);
        Assert.Equal(2, calls);
        Assert.Equal(0, vault.Writes);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("{\"message\":\"synthetic-hosting-token\"}")]
    [InlineData("[{\"full_name\":\"missing required fields\"}]")]
    public async Task MalformedProviderPayloadReturnsAnErrorWithoutResponseContent(string payload)
    {
        var client = Client(new Prompt(), (_, _, _) => Task.FromResult(Response(HttpStatusCode.OK, payload)));

        var error = Failure(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, null, 1, false, CancellationToken.None));

        Assert.Equal(GitErrorCode.CommandFailed, error.Code);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("missing required fields", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://api.example/")]
    [InlineData("https://user:token@api.example/")]
    [InlineData("https://api.example/?query=token")]
    [InlineData("https://api.example/#fragment")]
    public async Task InvalidApiAddressesFailBeforeRequestingCredentials(string address)
    {
        var prompt = new Prompt();
        var calls = 0;
        var client = Client(prompt, (_, _, _) =>
        {
            calls++;
            return Task.FromResult(Response(HttpStatusCode.OK, "[]"));
        });

        Assert.IsType<GitResult<GitHostedRepositoryPage>.Failure>(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, new Uri(address), null, 1, false, CancellationToken.None));

        Assert.Equal(0, calls);
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task StoredHostingAccountsStayInsideTheirConnectionAndWorkspace()
    {
        using var vault = new PersistentVault();
        var prompt = new Prompt { Save = true };
        var requests = new List<ConnectionProfile>();
        Task<HttpResponseMessage> Send(ConnectionProfile connection, HttpRequestMessage request, CancellationToken token)
        {
            requests.Add(connection);
            Assert.Equal("Bearer " + Token, request.Headers.Authorization?.ToString());
            return Task.FromResult(Response(HttpStatusCode.OK, "[]"));
        }
        var firstWorkspace = new WorkspaceId("workspace.first");
        var client = Client(prompt, Send, vault, firstWorkspace);
        Success(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local, GitHostingProvider.GitHub,
            GitHubApi, null, 1, false, CancellationToken.None));
        var account = Assert.Single(Success(await client.ReadHostingAccountsAsync(BuiltInConnections.Local, CancellationToken.None)));
        Assert.Equal("synthetic-user", account.Username);
        Assert.Equal(GitHostingProvider.GitHub, account.Provider);
        Assert.DoesNotContain(Token, account.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, vault.Writes);

        var reopened = Client(prompt, Send, vault, firstWorkspace);
        Success(await reopened.ReadHostedRepositoriesAsync(BuiltInConnections.Local, GitHostingProvider.GitHub,
            GitHubApi, account, 1, false, CancellationToken.None));
        Assert.Equal(1, prompt.Calls);
        prompt.Cancel = true;
        var otherConnection = Connection("connection.other");
        Assert.Empty(Success(await client.ReadHostingAccountsAsync(otherConnection, CancellationToken.None)));
        Assert.Equal(GitErrorCode.Cancelled, Failure(await client.ReadHostedRepositoriesAsync(otherConnection,
            GitHostingProvider.GitHub, GitHubApi, account, 1, false, CancellationToken.None)).Code);
        Assert.IsType<GitResult<GitUnit>.Failure>(await client.RemoveHostingAccountAsync(otherConnection, account, CancellationToken.None));

        var otherWorkspace = Client(prompt, Send, vault, new WorkspaceId("workspace.other"));
        Assert.Empty(Success(await otherWorkspace.ReadHostingAccountsAsync(BuiltInConnections.Local, CancellationToken.None)));
        Assert.Equal(GitErrorCode.Cancelled, Failure(await otherWorkspace.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, account, 1, false, CancellationToken.None)).Code);
        Assert.IsType<GitResult<GitUnit>.Failure>(await otherWorkspace.RemoveHostingAccountAsync(BuiltInConnections.Local, account, CancellationToken.None));
        Assert.Equal(2, requests.Count);
        Assert.All(requests, connection => Assert.Same(BuiltInConnections.Local, connection));
        Success(await client.RemoveHostingAccountAsync(BuiltInConnections.Local, account, CancellationToken.None));
        Assert.Empty(Success(await client.ReadHostingAccountsAsync(BuiltInConnections.Local, CancellationToken.None)));
    }

    [Fact]
    public async Task HostedCloneUsesTheSavedAccountOnlyAsSensitiveInputAndHidesCommandFailureContent()
    {
        using var vault = new PersistentVault();
        var account = await SaveAccountAsync(vault);
        var prompt = new Prompt { Cancel = true };
        var executor = new CloneExecutor();
        var client = new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt, secretVault: vault);

        var error = Failure(await client.CloneHostedRepositoryWithExecutableAsync(BuiltInConnections.Local, account,
            "https://api.github.example/owner/private.git", "/clone with space", "/custom tools/git", CancellationToken.None));

        var command = Assert.Single(executor.Commands);
        Assert.Equal("/bin/sh", command.Executable);
        Assert.Contains("/custom tools/git", command.Arguments, StringComparer.Ordinal);
        Assert.Equal("/clone with space", command.Arguments[^1]);
        Assert.Contains("clone", command.Arguments, StringComparer.Ordinal);
        Assert.Equal("synthetic-user\n" + Token + "\n", Assert.Single(executor.Inputs));
        Assert.True(command.StandardInput!.IsDisposed);
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains(Token, StringComparison.Ordinal));
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, prompt.Calls);
    }

    [Theory]
    [InlineData("https://different.example/owner/private.git")]
    [InlineData("https://intruder@api.github.example/owner/private.git")]
    public async Task HostedCloneRejectsAnAddressOutsideTheSelectedAccountBeforeExecuting(string url)
    {
        using var vault = new PersistentVault();
        var account = await SaveAccountAsync(vault);
        var executor = new CloneExecutor();
        var client = new GitRepositoryClient(executor, TimeProvider.System, secretVault: vault);

        var error = Failure(await client.CloneHostedRepositoryWithExecutableAsync(BuiltInConnections.Local, account,
            url, "/unused", "git", CancellationToken.None));

        Assert.Equal(GitErrorCode.AuthenticationRequired, error.Code);
        Assert.Empty(executor.Commands);
    }

    [Fact]
    public async Task HostedCloneCannotUseAnAccountFromAnotherWorkspace()
    {
        using var vault = new PersistentVault();
        var account = await SaveAccountAsync(vault);
        var executor = new CloneExecutor();
        var client = new GitRepositoryClient(executor, TimeProvider.System, secretVault: vault,
            credentialWorkspaceId: new WorkspaceId("workspace.other"));

        var error = Failure(await client.CloneHostedRepositoryWithExecutableAsync(BuiltInConnections.Local, account,
            "https://api.github.example/owner/private.git", "/unused", "git", CancellationToken.None));

        Assert.Equal(GitErrorCode.AuthenticationRequired, error.Code);
        Assert.Empty(executor.Commands);
    }

    [Fact]
    public async Task HostedCloneRejectsAnUnsupportedConnectionBeforeReleasingItsSavedCredential()
    {
        using var vault = new PersistentVault();
        var account = await SaveAccountAsync(vault);
        var local = BuiltInConnections.Local;
        var unsupported = new ConnectionProfile(local.Id, local.SchemaVersion, "Container",
            new ConnectionEndpoint.Docker("git-container"), local.Authentication, local.Startup, local.KeepAlive, local.HostKeyPolicy);
        var executor = new CloneExecutor();
        var client = new GitRepositoryClient(executor, TimeProvider.System, secretVault: vault);

        var error = Failure(await client.CloneHostedRepositoryWithExecutableAsync(unsupported, account,
            "https://api.github.example/owner/private.git", "/unused", "git", CancellationToken.None));

        Assert.Equal(GitErrorCode.Unsupported, error.Code);
        Assert.Empty(executor.Commands);
    }

    [Fact]
    public async Task AccountStorageFailureReportsThatSignInSucceededWithoutExposingTheToken()
    {
        using var vault = new PersistentVault { FailWrites = true };
        var client = Client(new Prompt { Save = true }, (_, _, _) => Task.FromResult(Response(HttpStatusCode.OK, "[]")), vault);

        var error = Failure(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local,
            GitHostingProvider.GitHub, GitHubApi, null, 1, false, CancellationToken.None));

        Assert.Equal(GitErrorCode.CredentialStorageFailed, error.Code);
        Assert.Contains("Sign-in succeeded", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Empty(Success(await client.ReadHostingAccountsAsync(BuiltInConnections.Local, CancellationToken.None)));
    }

    [Fact]
    public async Task CancellingTheHostingRequestStopsTheTransportWithoutSavingAnAccount()
    {
        using var vault = new PersistentVault();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Client(new Prompt { Save = true }, async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "[]");
        }, vault);
        var read = client.ReadHostedRepositoriesAsync(BuiltInConnections.Local, GitHostingProvider.GitHub,
            GitHubApi, null, 1, false, cancellation.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, vault.Writes);
    }

    private static GitRepositoryClient Client(Prompt prompt,
        Func<ConnectionProfile, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        ISecretVault? vault = null, WorkspaceId? workspace = null) =>
        new(new UnusedExecutor(), TimeProvider.System, credentialPrompt: prompt, secretVault: vault,
            credentialWorkspaceId: workspace, hostingHttpHandlerFactory: connection => new Handler((request, token) => send(connection, request, token)));

    private static async Task<GitHostingAccount> SaveAccountAsync(ISecretVault vault)
    {
        var client = Client(new Prompt { Save = true }, (_, _, _) => Task.FromResult(Response(HttpStatusCode.OK, "[]")), vault);
        Success(await client.ReadHostedRepositoriesAsync(BuiltInConnections.Local, GitHostingProvider.GitHub,
            GitHubApi, null, 1, false, CancellationToken.None));
        return Assert.Single(Success(await client.ReadHostingAccountsAsync(BuiltInConnections.Local, CancellationToken.None)));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static ConnectionProfile Connection(string id)
    {
        var local = BuiltInConnections.Local;
        return new(new ConnectionId(id), local.SchemaVersion, "Other local", local.Endpoint,
            local.Authentication, local.Startup, local.KeepAlive, local.HostKeyPolicy);
    }

    private static T Success<T>(GitResult<T> result)
    {
        Assert.True(result is GitResult<T>.Success, result is GitResult<T>.Failure failure ? failure.Error.Message : "Unknown result");
        return ((GitResult<T>.Success)result).Value;
    }

    private static GitError Failure<T>(GitResult<T> result) => Assert.IsType<GitResult<T>.Failure>(result).Error;

    private sealed record Request(Uri Url, string? Authorization, string? PrivateToken)
    {
        public static Request From(HttpRequestMessage request) => new(request.RequestUri!, request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("PRIVATE-TOKEN", out var tokens) ? tokens.Single() : null);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class Prompt : IGitCredentialPrompt
    {
        public bool Save { get; init; }
        public bool Cancel { get; set; }
        public int Calls { get; private set; }
        public List<bool> Rejected { get; } = [];

        public ValueTask<GitCredentials?> RequestAsync(Uri remote, bool canSave, bool rejected, CancellationToken cancellationToken)
        {
            Calls++;
            Rejected.Add(rejected);
            return ValueTask.FromResult(Cancel ? null : GitCredentials.Create("synthetic-user", Token, Save));
        }
    }

    private sealed class PersistentVault : ISecretVault
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

    private sealed class UnusedExecutor : IConnectionCommandExecutor
    {
        public ValueTask<ConnectionCommandResult> ExecuteAsync(ConnectionCommand request, CancellationToken cancellationToken) => throw new InvalidOperationException("Hosting requests must use the HTTP transport.");
        public ValueTask<ConnectionBinaryCommandResult> ExecuteBinaryAsync(ConnectionBinaryCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionStreamingCommandResult<T>> ExecuteStreamingAsync<T>(ConnectionBinaryCommand request,
            Func<Stream, CancellationToken, ValueTask<T>> consumeOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CloneExecutor : IConnectionCommandExecutor
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
            return ValueTask.FromResult(new ConnectionCommandResult(ConnectionCommandOutcome.Exited, 128, Token, Token));
        }

        public ValueTask<ConnectionBinaryCommandResult> ExecuteBinaryAsync(ConnectionBinaryCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionStreamingCommandResult<T>> ExecuteStreamingAsync<T>(ConnectionBinaryCommand request,
            Func<Stream, CancellationToken, ValueTask<T>> consumeOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
