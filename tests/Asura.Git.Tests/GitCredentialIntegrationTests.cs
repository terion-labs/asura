using System.Net;
using System.Net.Sockets;
using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Git.Tests;

public sealed class GitCredentialIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealGitFetchAuthenticatesWithStdinAndPreservesWorkspaceRuntime(bool workspaceRuntime)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("asura-git-auth-");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeAsync(listener, deadline.Token);
        var runtime = new Runtime();
        var executor = new ConnectionCommandExecutor(workspaceRuntime ? runtime : new LocalRuntime(), new PathConnectionExecutableLocator());
        var prompt = new Prompt();
        try
        {
            foreach (var arguments in new string[][]
            {
                ["init", directory.FullName],
                ["-C", directory.FullName, "config", "credential.helper", ""],
                ["-C", directory.FullName, "remote", "add", "origin", $"http://127.0.0.1:{port}/repo"],
            })
            {
                var setup = await executor.ExecuteAsync(new ConnectionCommand(BuiltInConnections.Local, "git", arguments,
                    TimeSpan.FromSeconds(5), 4096), deadline.Token);
                Assert.True(setup.ExitCode == 0, setup.StandardError);
            }

            var result = await new GitRepositoryClient(executor, TimeProvider.System, credentialPrompt: prompt)
                .FetchRemoteAsync(new GitRepositoryHandle(BuiltInConnections.Local, directory.FullName), "origin", deadline.Token);
            Assert.IsType<GitResult<GitUnit>.Success>(result);
            Assert.Equal(1, prompt.Calls);
            Assert.Equal(workspaceRuntime ? 1 : 0, runtime.DuplexCalls);
            var config = await File.ReadAllTextAsync(Path.Combine(directory.FullName, ".git", "config"), deadline.Token);
            Assert.DoesNotContain("synthetic-token", config, StringComparison.Ordinal);
        }
        finally
        {
            await deadline.CancelAsync();
            try { await server; }
            catch (OperationCanceledException) { }
            directory.Delete(recursive: true);
        }
    }

    private static async Task ServeAsync(TcpListener listener, CancellationToken token)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            var authenticated = false;
            while (await reader.ReadLineAsync(token) is { Length: > 0 } line)
            {
                authenticated |= string.Equals(line, "Authorization: Basic "
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-user:synthetic-token $'\\")), StringComparison.Ordinal);
            }
            // Advertise a valid, empty smart-HTTP repository after the Basic challenge.
            var body = authenticated ? "001e# service=git-upload-pack\n00000000" : "";
            var headers = authenticated
                ? "HTTP/1.1 200 OK\r\nContent-Type: application/x-git-upload-pack-advertisement\r\n"
                : "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=Git\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"{headers}Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), token);
        }
    }

    private sealed class Prompt : IGitCredentialPrompt
    {
        public int Calls { get; private set; }
        public ValueTask<GitCredentials?> RequestAsync(Uri remote, bool canSave, bool rejected, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult<GitCredentials?>(GitCredentials.Create("synthetic-user", "synthetic-token $'\\", false));
        }
    }

    private sealed class Runtime : IConnectionRuntime, IConnectionCommandRuntime
    {
        public int DuplexCalls { get; private set; }
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanCommandAsync(
            ConnectionProfile connection, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ConnectionRuntimeResult<TerminalLaunchRequest>.Succeed(new TerminalLaunchRequest(
                null, executable, arguments, environment: new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_TERMINAL_PROMPT"] = "0" })));
        public ValueTask<ConnectionRuntimeResult<TerminalLaunchRequest>> PlanDuplexCommandAsync(
            ConnectionProfile connection, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            DuplexCalls++;
            return PlanCommandAsync(connection, executable, arguments, cancellationToken);
        }
        public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LocalRuntime : IConnectionRuntime
    {
        public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ConnectionRuntimeResult<ConnectionOpenPlan>.Succeed(new ConnectionOpenPlan(
                profile.Id, ConnectionKind.Local, new TerminalLaunchRequest(null,
                    environment: new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_TERMINAL_PROMPT"] = "0" }),
                ConnectionAuthenticationMode.None, SshHostKeyPolicy.NotApplicable, ConnectionReconnectMode.NotApplicable)));
        public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
