using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Asura.Application;

namespace Asura.ConnectionBackend;

/// <summary>One owned request stream; watches get separate owned children so they cannot block commands.</summary>
internal sealed partial class KubernetesWorkspaceSession : IKubernetesClientSession
{
    private readonly DatabaseWorkspaceOperationLaunch _owned;
    private readonly Func<CancellationToken, Task<KubernetesWorkspaceSession>> _openWatch;
    private readonly Process _process;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenRegistration _termination;
    private readonly Task _errors;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _disposeGate = new();
    private Task? _dispose;
    private long _nextId;

    public KubernetesSessionFeatures Features => KubernetesSessionFeatures.Watch | KubernetesSessionFeatures.FollowLogs
        | KubernetesSessionFeatures.Exec | KubernetesSessionFeatures.PortForward | KubernetesSessionFeatures.Mutations
        | KubernetesSessionFeatures.ManifestConversion | KubernetesSessionFeatures.Metrics
        | KubernetesSessionFeatures.MetricHistory | KubernetesSessionFeatures.HelmRead
        | KubernetesSessionFeatures.NodeMaintenance | KubernetesSessionFeatures.HelmChanges;

    internal KubernetesWorkspaceSession(DatabaseWorkspaceOperationLaunch owned,
        Func<CancellationToken, Task<KubernetesWorkspaceSession>> openWatch)
    {
        _owned = owned;
        _openWatch = openWatch;
        owned.Lifetime.ThrowIfCancellationRequested();
        owned.StartInfo.UseShellExecute = false;
        owned.StartInfo.RedirectStandardInput = true;
        owned.StartInfo.RedirectStandardOutput = true;
        owned.StartInfo.RedirectStandardError = true;
        owned.StartInfo.CreateNoWindow = true;
        _process = Process.Start(owned.StartInfo) ?? throw new IOException("The Kubernetes backend could not start.");
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(owned.Lifetime);
        _termination = _lifetime.Token.Register(() => DatabaseOperationWorker.StopOwnedProcess(_process));
        _errors = DrainErrorsAsync();
        _ = ObserveExitAsync();
    }

    internal async Task OpenAsync(KubernetesWorkspaceOpen configuration, CancellationToken token) =>
        _ = await InvokeAsync(new(0, KubernetesWorkspaceOperation.Open, Open: configuration), token).ConfigureAwait(false);

    internal async Task<KubernetesConfigurationReview> ReviewAsync(KubernetesWorkspaceOpen configuration, CancellationToken token) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.Review, Open: configuration), token).ConfigureAwait(false)).Review
        ?? throw InvalidResponse();

    public async ValueTask<string> ConvertManifestToJsonAsync(string manifest, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.ConvertManifest, Manifest: manifest), cancellationToken).ConfigureAwait(false)).ManifestJson
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesDiscovery> DiscoverAsync(CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.Discover), cancellationToken).ConfigureAwait(false)).Discovery
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesResourcePage> ListAsync(KubernetesListRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.List, List: request), cancellationToken).ConfigureAwait(false)).Page
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesResourceDocument> InspectAsync(KubernetesResourceReference resource, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.Inspect, Resource: resource), cancellationToken).ConfigureAwait(false)).Resource
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesLogPage> ReadLogsAsync(KubernetesLogRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.Logs, Logs: request), cancellationToken).ConfigureAwait(false)).Logs
        ?? throw InvalidResponse();

    public async ValueTask<KubernetesMutationResult> MutateAsync(KubernetesMutationRequest request, CancellationToken cancellationToken) =>
        (await InvokeAsync(new(0, KubernetesWorkspaceOperation.Mutate, Mutation: request), cancellationToken).ConfigureAwait(false)).Mutation
        ?? throw InvalidResponse();

    public async IAsyncEnumerable<KubernetesWatchEvent> WatchAsync(KubernetesWatchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await using var watcher = await _openWatch(linked.Token).ConfigureAwait(false);
        await watcher._requests.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var id = checked(++watcher._nextId);
            await BackendJsonFrames.WriteAsync(watcher._process.StandardInput.BaseStream,
                new KubernetesWorkspaceRequest(id, KubernetesWorkspaceOperation.Watch, Watch: request),
                KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, linked.Token).ConfigureAwait(false);
            while (true)
            {
                var response = await watcher.ReadAsync(id, linked.Token).ConfigureAwait(false);
                if (response.Completed) { yield break; }
                yield return response.WatchEvent ?? throw InvalidResponse();
            }
        }
        finally { watcher._requests.Release(); }
    }

    private async Task<KubernetesWorkspaceResponse> InvokeAsync(KubernetesWorkspaceRequest request, CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        var dispatched = false;
        var confirmed = false;
        byte[]? serialized = null;
        try
        {
            ObjectDisposedException.ThrowIf(_lifetime.IsCancellationRequested, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            request = request with { Id = checked(++_nextId) };
            serialized = await BackendJsonFrames.SerializeAsync(request,
                KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            // A pipe failure can occur after the worker received a complete write request.
            dispatched = true;
            await DatabaseOperationProtocol.WriteFrameAsync(_process.StandardInput.BaseStream, serialized, linked.Token).ConfigureAwait(false);
            var response = await ReadAsync(request.Id, linked.Token).ConfigureAwait(false);
            ValidateResponse(request.Operation, response);
            confirmed = true;
            return response;
        }
        catch (KubernetesRequestException)
        {
            confirmed = true;
            throw;
        }
        catch when (dispatched && !confirmed)
        {
            await DisposeAsync().ConfigureAwait(false);
            if (request.Operation is KubernetesWorkspaceOperation.Mutate or KubernetesWorkspaceOperation.NodeScheduling)
            {
                return new(request.Id, Mutation: new(KubernetesMutationOutcome.OutcomeUnknown, null));
            }
            if (request.Operation == KubernetesWorkspaceOperation.NodeDrainExecute)
            { return new(request.Id, NodeDrainResult: new(KubernetesDrainOutcome.OutcomeUnknown, null, [], "backend_disconnected")); }
            if (request.Operation == KubernetesWorkspaceOperation.HelmChangeExecute)
            { return new(request.Id, HelmChangeResult: new(KubernetesMutationOutcome.OutcomeUnknown, "backend_disconnected")); }
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed,
                "The Kubernetes backend stopped. Reconnect before retrying.");
        }
        finally
        {
            if (serialized is not null) { CryptographicOperations.ZeroMemory(serialized); }
            _requests.Release();
        }
    }

    private async Task<KubernetesWorkspaceResponse> ReadAsync(long id, CancellationToken token)
    {
        var response = await BackendJsonFrames.ReadAsync(_process.StandardOutput.BaseStream,
            KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, token).ConfigureAwait(false);
        if (!response.IsResponse || response.Id != id) { throw InvalidResponse(); }
        if (response.Error is { } error)
        {
            if (!Enum.IsDefined(error)) { throw InvalidResponse(); }
            var message = response.ErrorMessage is { Length: > 0 and <= 2048 } detail
                ? detail : $"Kubernetes request failed: {error}.";
            throw new KubernetesRequestException(error, message, response.StatusCode, response.Retryable);
        }
        return response;
    }

    private static InvalidDataException InvalidResponse() => new("The Kubernetes backend returned an invalid response.");

    private static void ValidateResponse(KubernetesWorkspaceOperation operation, KubernetesWorkspaceResponse response)
    {
        var present = (response.Discovery is null ? 0 : 1) + (response.Page is null ? 0 : 1)
            + (response.Resource is null ? 0 : 1) + (response.Logs is null ? 0 : 1)
            + (response.WatchEvent is null ? 0 : 1) + (response.Mutation is null ? 0 : 1)
            + (response.Review is null ? 0 : 1) + (response.ManifestJson is null ? 0 : 1)
            + (response.ForwardPort is null ? 0 : 1) + (response.Metrics is null ? 0 : 1)
            + (response.MetricHistory is null ? 0 : 1) + (response.HelmReleases is null ? 0 : 1)
            + (response.HelmHistory is null ? 0 : 1) + (response.NodeDrainReview is null ? 0 : 1)
            + (response.NodeDrainResult is null ? 0 : 1) + (response.HelmChangeReview is null ? 0 : 1)
            + (response.HelmChangeResult is null ? 0 : 1);
        var valid = operation switch
        {
            KubernetesWorkspaceOperation.Open => present == 0,
            KubernetesWorkspaceOperation.Discover => response.Discovery is not null,
            KubernetesWorkspaceOperation.List => response.Page is not null,
            KubernetesWorkspaceOperation.Inspect => response.Resource is not null,
            KubernetesWorkspaceOperation.Logs => response.Logs is not null,
            KubernetesWorkspaceOperation.Mutate or KubernetesWorkspaceOperation.NodeScheduling => response.Mutation is { } mutation && Enum.IsDefined(mutation.Outcome),
            KubernetesWorkspaceOperation.Review => response.Review is not null,
            KubernetesWorkspaceOperation.ConvertManifest => response.ManifestJson is not null,
            KubernetesWorkspaceOperation.ForwardStart => response.StreamReady && response.ForwardPort is > 0 and <= 65535,
            KubernetesWorkspaceOperation.Metrics => response.Metrics is not null,
            KubernetesWorkspaceOperation.MetricHistory => response.MetricHistory is not null,
            KubernetesWorkspaceOperation.HelmList => response.HelmReleases is not null,
            KubernetesWorkspaceOperation.HelmHistory => response.HelmHistory is not null,
            KubernetesWorkspaceOperation.NodeDrainReview => response.NodeDrainReview is not null,
            KubernetesWorkspaceOperation.NodeDrainExecute => response.NodeDrainResult is { Pods: not null } drain && Enum.IsDefined(drain.Outcome),
            KubernetesWorkspaceOperation.HelmChangeReview => response.HelmChangeReview is not null,
            KubernetesWorkspaceOperation.HelmChangeExecute => response.HelmChangeResult is { } helm && Enum.IsDefined(helm.Outcome),
            _ => false,
        };
        if (!valid || response.Completed || present != (operation == KubernetesWorkspaceOperation.Open ? 0 : 1))
        {
            throw InvalidResponse();
        }
    }

    private async Task ObserveExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            await _requests.WaitAsync().ConfigureAwait(false);
            try { await DisposeAsync().ConfigureAwait(false); }
            finally { _requests.Release(); }
        }
        catch (Exception)
        {
            // Explicit disposal observes cleanup errors; never leak child stderr or unobserved background faults.
        }
    }

    private async Task DrainErrorsAsync()
    {
        try { await _process.StandardError.BaseStream.CopyToAsync(Stream.Null, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) { return new(_dispose ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        DatabaseOperationWorker.StopOwnedProcess(_process);
        try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { }
        try { await _errors.ConfigureAwait(false); }
        finally
        {
            _termination.Dispose();
            _process.Dispose();
            _lifetime.Dispose();
            await _owned.CleanupAsync().ConfigureAwait(false);
        }
    }
}
