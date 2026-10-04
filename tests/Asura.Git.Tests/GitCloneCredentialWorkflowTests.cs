using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Git.Tests;

[SupportedOSPlatform("macos")]
public sealed class GitCloneCredentialWorkflowTests
{
    [Fact]
    public async Task PrivateCloneUsesTheCredentialPromptBeforeTheDestinationIsARepository()
    {
        var directory = Directory.CreateTempSubdirectory("asura-private-clone-");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var remote = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/private-repo");
        var authenticatedRequests = new List<string>();
        var server = ServeEmptyRepositoryAsync(listener, authenticatedRequests, deadline.Token);
        var destination = Path.Combine(directory.FullName, "new clone");
        var prompt = new CredentialPrompt();
        var executor = new ConnectionCommandExecutor(new Runtime(), new PathConnectionExecutableLocator());
        try
        {
            var result = await new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt)
                .CloneRepositoryAsync(BuiltInConnections.Local, remote.AbsoluteUri, destination, deadline.Token);

            Assert.True(result is GitResult<GitRepositoryHandle>.Success,
                result is GitResult<GitRepositoryHandle>.Failure failure ? failure.Error.Message : "Unknown clone result");
            var canonicalRoot = await executor.ExecuteAsync(new ConnectionCommand(BuiltInConnections.Local, "git",
                ["-C", destination, "rev-parse", "--show-toplevel"], TimeSpan.FromSeconds(5), 4096), deadline.Token);
            Assert.Equal(0, canonicalRoot.ExitCode);
            Assert.Equal(canonicalRoot.StandardOutput.TrimEnd('\r', '\n'), ((GitResult<GitRepositoryHandle>.Success)result).Value.WorkingTreeRoot);
            Assert.Equal(1, prompt.Calls);
            Assert.Equal(remote.GetLeftPart(UriPartial.Authority), prompt.Remote?.GetLeftPart(UriPartial.Authority));
            Assert.NotEmpty(authenticatedRequests);
            var config = await File.ReadAllTextAsync(Path.Combine(destination, ".git", "config"), deadline.Token);
            Assert.Contains(remote.AbsoluteUri, config, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-clone-token", config, StringComparison.Ordinal);
        }
        finally
        {
            await deadline.CancelAsync();
            try
            {
                await server;
            }
            catch (OperationCanceledException)
            {
            }
            directory.Delete(recursive: true);
        }
    }

    private static async Task ServeEmptyRepositoryAsync(TcpListener listener, List<string> authenticatedRequests, CancellationToken token)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            var authenticated = false;
            var request = await reader.ReadLineAsync(token) ?? "";
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
            {
                authenticated |= string.Equals(line, "Authorization: Basic "
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-user:synthetic-clone-token $'\\")), StringComparison.Ordinal);
            }
            if (authenticated)
            {
                authenticatedRequests.Add(request);
            }
            var body = authenticated ? "001e# service=git-upload-pack\n00000000" : "";
            var headers = authenticated
                ? "HTTP/1.1 200 OK\r\nContent-Type: application/x-git-upload-pack-advertisement\r\n"
                : "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=Git\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"{headers}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), token);
        }
    }

    private sealed class CredentialPrompt : IGitCredentialPrompt
    {
        public int Calls { get; private set; }
        public Uri? Remote { get; private set; }

        public ValueTask<GitCredentials?> RequestAsync(Uri remote, bool canSave, bool rejected, CancellationToken cancellationToken)
        {
            Calls++;
            Remote = remote;
            return ValueTask.FromResult<GitCredentials?>(GitCredentials.Create("synthetic-user", "synthetic-clone-token $'\\", false));
        }
    }

    private sealed class Runtime : IConnectionRuntime
    {
        public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => ValueTask.FromResult(
                ConnectionRuntimeResult<ConnectionOpenPlan>.Succeed(new(profile.Id, ConnectionKind.Local,
                    new TerminalLaunchRequest(null, environment: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["GIT_TERMINAL_PROMPT"] = "0",
                        ["GIT_CONFIG_COUNT"] = "1",
                        ["GIT_CONFIG_KEY_0"] = "credential.helper",
                        ["GIT_CONFIG_VALUE_0"] = "",
                    }), ConnectionAuthenticationMode.None, SshHostKeyPolicy.NotApplicable, ConnectionReconnectMode.NotApplicable)));

        public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
