using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Asura.Application;
using Asura.ConnectionBackend;

namespace Asura.Architecture.Tests;

public sealed class KubernetesWorkspaceSessionTests
{
    [Theory]
    [InlineData("mutation")]
    [InlineData("nodeDrainResult")]
    [InlineData("helmChangeResult")]
    public async Task MissingMutationOutcomeCannotDefaultToSuccessfulWrite(string resultProperty)
    {
        var payload = Encoding.UTF8.GetBytes("{\"id\":1,\"isResponse\":true,\"" + resultProperty + "\":{}}");
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        using var input = new MemoryStream(frame);
        await Assert.ThrowsAsync<JsonException>(() => BackendJsonFrames.ReadAsync(input,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, CancellationToken.None));
    }

    [Fact]
    public async Task MalformedMutationReplyIsUnknownAndClosesLeaseWithoutReplay()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var cleanups = 0;
        await using var session = CreateEchoSession(() => { Interlocked.Increment(ref cleanups); return Task.CompletedTask; });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await session.MutateAsync(new(new("", "v1", "pods", "test", "pod", "uid", "rv"),
            KubernetesMutationKind.Delete, null, DryRun: false), timeout.Token);
        Assert.Equal(KubernetesMutationOutcome.OutcomeUnknown, result.Outcome);
        Assert.Equal(1, cleanups);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.DiscoverAsync(timeout.Token).AsTask());
        Assert.Equal(1, cleanups);
    }

    [Fact]
    public async Task CancellationBeforeDispatchDoesNotClaimAWriteOutcome()
    {
        if (OperatingSystem.IsWindows()) { return; }
        await using var session = CreateEchoSession(() => Task.CompletedTask);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.MutateAsync(
            new(new("", "v1", "pods", "test", "pod", "uid", "rv"), KubernetesMutationKind.Delete, null),
            cancelled.Token).AsTask());
    }

    [Fact]
    public async Task RouteRevocationReleasesAnIdleOwnedWorker()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var route = new CancellationTokenSource();
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateEchoSession(() => { cleaned.TrySetResult(); return Task.CompletedTask; }, route.Token);
        await route.CancelAsync();
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.DiscoverAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task PrivateFramesRejectOversizeTruncationAndInvalidInitialSequence()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, BackendJsonFrames.MaximumBytes + 1);
        using var oversized = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => BackendJsonFrames.ReadAsync(oversized,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, CancellationToken.None));
        BinaryPrimitives.WriteInt32LittleEndian(header, 100);
        using var truncated = new MemoryStream(header);
        await Assert.ThrowsAsync<EndOfStreamException>(() => BackendJsonFrames.ReadAsync(truncated,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, CancellationToken.None));
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await BackendJsonFrames.WriteAsync(input, new KubernetesWorkspaceRequest(2, KubernetesWorkspaceOperation.Discover),
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, CancellationToken.None);
        input.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => KubernetesWorkspaceChild.RunAsync(input, output, CancellationToken.None));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ConfigurationErrorsNeverEchoCredentialContent()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await BackendJsonFrames.WriteAsync(input, new KubernetesWorkspaceRequest(1, KubernetesWorkspaceOperation.Open,
            Open: new("missing-context", "default", null, "secret-sensitive-invalid-yaml: [", null)),
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, CancellationToken.None);
        input.Position = 0;
        await KubernetesWorkspaceChild.RunAsync(input, output, CancellationToken.None);
        Assert.DoesNotContain("secret-sensitive", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        output.Position = 0;
        var response = await BackendJsonFrames.ReadAsync(output,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, CancellationToken.None);
        Assert.Equal(KubernetesErrorCode.InvalidConfiguration, response.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkerPreservesSafeTypedFailuresButNeverRawExceptionMessages(bool safe)
    {
        const string detail = "Install the approved credential helper in this workspace.";
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        await BackendJsonFrames.WriteAsync(input, new KubernetesWorkspaceRequest(1, KubernetesWorkspaceOperation.Open,
            Open: new("context", "default", null, null, null)),
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, CancellationToken.None);
        input.Position = 0;
        await KubernetesWorkspaceChild.RunAsync(input, output, (_, _) =>
            Task.FromException<IKubernetesClientSession>(safe
                ? new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, detail)
                : new IOException("private-credential-content")), CancellationToken.None);
        Assert.DoesNotContain("private-credential-content", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        output.Position = 0;
        var response = await BackendJsonFrames.ReadAsync(output,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, CancellationToken.None);
        Assert.Equal(safe ? detail : null, response.ErrorMessage);
    }

    [Fact]
    public async Task ParentDisplaysTheWorkersSafeErrorDetail()
    {
        if (OperatingSystem.IsWindows()) { return; }
        const string detail = "Install the approved credential helper in this workspace.";
        var directory = Directory.CreateTempSubdirectory("asura-kube-error-");
        try
        {
            var path = Path.Combine(directory.FullName, "response");
            await using (var output = File.Create(path))
            {
                await BackendJsonFrames.WriteAsync(output, new KubernetesWorkspaceResponse(1,
                    KubernetesErrorCode.InvalidConfiguration, IsResponse: true, ErrorMessage: detail),
                    KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, CancellationToken.None);
            }
            var start = new ProcessStartInfo("/bin/sh");
            foreach (var argument in new[] { "-c", "cat -- \"$1\"; cat >/dev/null", "kube-error", path }) { start.ArgumentList.Add(argument); }
            await using var session = new KubernetesWorkspaceSession(new(start, () => Task.CompletedTask),
                _ => throw new InvalidOperationException("No watch expected."));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var error = await Assert.ThrowsAsync<KubernetesRequestException>(() => session.DiscoverAsync(timeout.Token).AsTask());
            Assert.Equal(detail, error.Message);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static KubernetesWorkspaceSession CreateEchoSession(Func<Task> cleanup, CancellationToken lifetime = default) =>
        new(new(new ProcessStartInfo("/bin/cat"), cleanup, lifetime),
            _ => throw new InvalidOperationException("This test does not open a watch."));
}
