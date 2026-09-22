using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.ConnectionBackend;
using Asura.Core;
using Asura.Infrastructure;
using Asura.Kubernetes;

namespace Asura.Architecture.Tests;

public sealed class KubernetesHostAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostPluginAuthenticatesRealWorkerAndRenewsAfterExpiryOrUnauthorized(bool expiring)
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var fixture = new Fixture(server, expiring);
        var serving = Task.Run(async () =>
        {
            if (!expiring) { await ServeAsync(server, "fixture-1", 401, timeout.Token); }
            await ServeAsync(server, "fixture-2", 200, timeout.Token);
        }, timeout.Token);
        await using var session = await fixture.Factory(host: true).OpenAsync(fixture.Profile, timeout.Token);
        var page = await session.ListAsync(new(new("", "v1", "pods", "Pod", true, ["list"]), "default"), timeout.Token);
        Assert.Empty(page.Items);
        Assert.Equal("2", File.ReadAllText(fixture.Calls).Trim());
        await serving;
        Assert.Equal(1, fixture.Launches);
    }

    [Fact]
    public async Task IsolatedWorkerDoesNotUseHostAuthentication()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var fixture = new Fixture(server);
        var error = await Assert.ThrowsAsync<KubernetesRequestException>(() =>
            fixture.Factory(host: false).OpenAsync(fixture.Profile, CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.Unauthorized, error.Code);
        Assert.False(File.Exists(fixture.Calls));
    }

    [Fact]
    public async Task ReviewNeverExecutesPluginAndChangedCommandRequiresNewTrust()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var fixture = new Fixture(server);
        var factory = fixture.Factory(host: true);
        var review = await factory.ReviewAsync(fixture.Profile, CancellationToken.None);
        Assert.Equal(fixture.Profile.TrustedExecFingerprint, Assert.Single(review.Contexts).ExecFingerprint);
        Assert.False(File.Exists(fixture.Calls));
        Assert.Equal(0, fixture.Launches);
        File.WriteAllText(fixture.Config, File.ReadAllText(fixture.Config).Replace("/bin/sh", "/bin/bash", StringComparison.Ordinal));
        var error = await Assert.ThrowsAsync<KubernetesRequestException>(() => factory.OpenAsync(fixture.Profile, CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.Unauthorized, error.Code);
        Assert.False(File.Exists(fixture.Calls));
    }

    [Fact]
    public async Task MissingHostConfigurationIsActionableAndDoesNotStartWorker()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var fixture = new Fixture(server);
        File.Delete(fixture.Config);
        var factory = fixture.Factory(host: true);
        var review = await Assert.ThrowsAsync<KubernetesRequestException>(() => factory.ReviewAsync(fixture.Profile, CancellationToken.None).AsTask());
        var open = await Assert.ThrowsAsync<KubernetesRequestException>(() => factory.OpenAsync(fixture.Profile, CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.InvalidConfiguration, review.Code);
        Assert.Equal(review.Message, open.Message);
        Assert.Contains("file permissions", open.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Launches);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationStopsHostRenewalAndPreservesCallerVersusRouteFailure(bool callerCancellation, bool podExec)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var route = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var fixture = new Fixture(server);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<KubernetesResolvedConnection> RefreshAsync(CancellationToken token)
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { canceled.TrySetResult(); }
            throw new InvalidOperationException();
        }
        async Task<KubernetesWorkspaceSession> OpenWorkerAsync(CancellationToken token)
        {
            var launch = await fixture.LaunchAsync(null, route.Token);
            var worker = new KubernetesWorkspaceSession(launch, OpenWorkerAsync, RefreshAsync);
            try
            {
                await worker.OpenAsync(new("context", "default", null, null, null,
                    new(fixture.Address, BearerToken: "fixture-1")), token);
                return worker;
            }
            catch { await worker.DisposeAsync(); throw; }
        }
        await using var session = await OpenWorkerAsync(timeout.Token);
        var serving = ServeAsync(server, "fixture-1", 401, timeout.Token, podExec);
        Task listing = podExec
            ? session.OpenExecAsync(new(new("", "v1", "pods", "default", "pod", "uid", "rv"), "container", ["sh"]), caller.Token).AsTask()
            : session.ListAsync(new(new("", "v1", "pods", "Pod", true, ["list"]), "default"), caller.Token).AsTask();
        await started.Task.WaitAsync(timeout.Token);
        await (callerCancellation ? caller : route).CancelAsync();
        await canceled.Task.WaitAsync(timeout.Token);
        if (callerCancellation) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listing); }
        else
        {
            var error = await Assert.ThrowsAsync<KubernetesRequestException>(() => listing);
            Assert.Equal(KubernetesErrorCode.ConnectionFailed, error.Code);
        }
        await serving;
        await session.DisposeAsync();
        Assert.Equal(podExec ? 2 : 1, fixture.Cleanups);
    }

    [Fact]
    public async Task FailedHostRenewalReturnsSafeErrorWithoutWorkerFallback()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var fixture = new Fixture(server);
        File.WriteAllText(fixture.Script, "if [ -f \"$1\" ]; then printf private-credential-detail >&2; exit 1; fi\n" + File.ReadAllText(fixture.Script));
        await using var session = await fixture.Factory(host: true).OpenAsync(fixture.Profile, timeout.Token);
        var serving = ServeAsync(server, "fixture-1", 401, timeout.Token);
        var error = await Assert.ThrowsAsync<KubernetesRequestException>(() =>
            session.ListAsync(new(new("", "v1", "pods", "Pod", true, ["list"]), "default"), timeout.Token).AsTask());
        Assert.Equal(KubernetesErrorCode.Unauthorized, error.Code);
        Assert.DoesNotContain("private-credential-detail", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("1", File.ReadAllText(fixture.Calls).Trim());
        Assert.Equal(1, fixture.Launches);
        await serving;
    }

    [Fact]
    public async Task CredentialRepliesCannotBeConsumedByConcurrentTerminalInputReader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var host = new TcpClient();
        var accepting = listener.AcceptTcpClientAsync(timeout.Token);
        await host.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token);
        using var worker = await accepting;
        await using var channel = new KubernetesWorkspaceChannel(worker.GetStream(), worker.GetStream(), timeout.Token);
        var readingInput = channel.ReadAsync(timeout.Token).AsTask();
        var refreshing = channel.RefreshAsync(timeout.Token).AsTask();
        var request = await BackendJsonFrames.ReadAsync(host.GetStream(),
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, timeout.Token);
        Assert.True(request.CredentialsRequested);
        Assert.Equal(-1, request.Id);
        await BackendJsonFrames.WriteAsync(host.GetStream(), new KubernetesWorkspaceRequest(2,
            KubernetesWorkspaceOperation.ExecInput, Data: [1, 2, 3]),
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, timeout.Token);
        var connection = new KubernetesResolvedConnection(new Uri("https://cluster.invalid"),
            ClientCertificatePem: "fixture-certificate", ClientKeyPem: "fixture-key",
            CredentialExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
        await BackendJsonFrames.WriteAsync(host.GetStream(), new KubernetesWorkspaceRequest(request.Id,
            KubernetesWorkspaceOperation.Credentials, Credentials: new(Connection: connection)),
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, timeout.Token);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await readingInput).Data);
        Assert.Equal(connection, await refreshing);
        Assert.DoesNotContain("fixture-key", new KubernetesWorkspaceCredentialReply(connection).ToString(), StringComparison.Ordinal);
        // A pending renewal must fail promptly if the host disappears.
        var disconnected = channel.RefreshAsync(timeout.Token).AsTask();
        _ = await BackendJsonFrames.ReadAsync(host.GetStream(), KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, timeout.Token);
        host.Dispose();
        await Assert.ThrowsAsync<IOException>(() => disconnected);
    }

    private static async Task ServeAsync(TcpListener server, string bearer, int status, CancellationToken token, bool podExec = false)
    {
        using var socket = await server.AcceptTcpClientAsync(token);
        await using var stream = socket.GetStream();
        using var headers = new MemoryStream();
        var single = new byte[1];
        while (!headers.GetBuffer().AsSpan(0, checked((int)headers.Length)).EndsWith("\r\n\r\n"u8))
        {
            Assert.True(headers.Length < 65536);
            await stream.ReadExactlyAsync(single, token);
            headers.WriteByte(single[0]);
        }
        var text = Encoding.ASCII.GetString(headers.ToArray());
        Assert.StartsWith(podExec ? "GET /api/v1/namespaces/default/pods/pod " : "GET /api/v1/namespaces/default/pods?", text, StringComparison.Ordinal);
        Assert.Contains($"Authorization: Bearer {bearer}\r\n", text, StringComparison.Ordinal);
        const string body = "{\"kind\":\"PodList\",\"apiVersion\":\"v1\",\"metadata\":{\"resourceVersion\":\"1\"},\"items\":[]}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), token);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("asura-host-kube-");
        private readonly InMemorySecretVault _vault = new();
        internal string Calls => Path.Combine(_directory.FullName, "calls");
        internal string Script => Path.Combine(_directory.FullName, "credential.sh");
        internal string Config => Path.Combine(_directory.FullName, "config");
        internal Uri Address { get; }
        internal KubernetesConnectionProfile Profile { get; }
        internal int Launches { get; private set; }
        internal int Cleanups { get; private set; }

        internal Fixture(TcpListener server, bool expiring = false)
        {
            Address = new($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}");
            var script = Script;
            var firstExpiry = DateTimeOffset.UtcNow.AddSeconds(expiring ? 25 : 3600).ToString("O", CultureInfo.InvariantCulture);
            var nextExpiry = DateTimeOffset.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture);
            File.WriteAllText(script, $$$$"""
                set -eu
                # This helper is deliberately unusable in the routed worker.
                test "${ASURA_TEST_ROUTED_WORKER:-}" != 1
                count=0
                if [ -f "$1" ]; then count=$(cat "$1"); fi
                count=$((count + 1))
                printf '%s\n' "$count" > "$1"
                expiry='{{{{nextExpiry}}}}'
                if [ "$count" = 1 ]; then expiry='{{{{firstExpiry}}}}'; fi
                printf '{"apiVersion":"client.authentication.k8s.io/v1","kind":"ExecCredential","status":{"token":"fixture-%s","expirationTimestamp":"%s"}}' "$count" "$expiry"
                """);
            var yaml = $$$$"""
                {"clusters":[{"name":"cluster","cluster":{"server":{{{{JsonSerializer.Serialize(Address.AbsoluteUri)}}}}}}],
                 "users":[{"name":"user","user":{"exec":{"command":"/bin/sh","args":[{{{{JsonSerializer.Serialize(script)}}}},{{{{JsonSerializer.Serialize(Calls)}}}}],"apiVersion":"client.authentication.k8s.io/v1","interactiveMode":"Never"}}}],
                 "contexts":[{"name":"context","context":{"cluster":"cluster","user":"user"}}]}
                """;
            File.WriteAllText(Config, yaml);
            Profile = new(KubernetesConnectionProfileId.New(), 1, "Cluster", Config, "context", "default",
                trustedExecFingerprint: Assert.Single(KubernetesKubeconfigReader.Read(yaml)).Exec!.Fingerprint);
        }

        internal KubernetesWorkspaceSessionFactory Factory(bool host) => new(LaunchAsync,
            DispatchProxy.Create<IDefinitionCatalog, KubernetesWorkspaceSessionFactoryTests.UnusedCatalog>(), _vault,
            useHostCredentials: host);

        internal Task<DatabaseWorkspaceOperationLaunch> LaunchAsync(ConnectionProfile? hop, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) { root = root.Parent; }
            Assert.NotNull(root);
            var backend = typeof(KubernetesHostAuthenticationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => string.Equals(attribute.Key, "DatabaseBackendPath", StringComparison.Ordinal)).Value;
            var id = Guid.NewGuid().ToString("N");
            DatabaseWorkspaceScratch.Prepare(id);
            var start = new ProcessStartInfo(Path.Combine(root.FullName, ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            start.ArgumentList.Add(backend!); start.ArgumentList.Add("kubernetes"); start.ArgumentList.Add(id);
            start.Environment["ASURA_TEST_ROUTED_WORKER"] = "1";
            Launches++;
            return Task.FromResult(new DatabaseWorkspaceOperationLaunch(start, async () =>
            {
                await DatabaseWorkspaceScratch.CleanupAsync(id, CancellationToken.None);
                Cleanups++;
            }, token));
        }

        public void Dispose()
        {
            _vault.Dispose();
            _directory.Delete(recursive: true);
        }
    }
}
