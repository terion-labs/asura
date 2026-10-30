using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Asura.Desktop;

internal enum SingleInstanceErrorCode
{
    Cancelled,
    ProfileUnavailable,
    ActivationUnavailable,
}

internal sealed record SingleInstanceError(
    SingleInstanceErrorCode Code,
    string StableCode,
    string Message);

internal abstract record SingleInstanceStartResult
{
    private SingleInstanceStartResult()
    {
    }

    public sealed record Primary(SingleInstanceCoordinator Coordinator)
        : SingleInstanceStartResult;

    public sealed record ExistingInstanceActivated : SingleInstanceStartResult;

    public sealed record Failure(SingleInstanceError Error) : SingleInstanceStartResult;
}

internal sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    internal const string LockFileName = ".asura.instance.lock";
    internal const string EndpointFileName = ".asura.instance.endpoint";

    private const string PipeNamePrefix = "gs-";
    private const int PipeIdentityByteCount = 12;
    private static readonly TimeSpan ActivationPollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan ConnectionAttemptTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly byte[] ActivationRequest = "GSA1"u8.ToArray();

    /// <summary>
    /// An activation that carries a request — a folder to open — as a
    /// two-byte little-endian length and that many bytes of UTF-8.
    /// </summary>
    private static readonly byte[] PayloadActivationRequest = "GSA2"u8.ToArray();

    /// <summary>The largest request an activation carries.</summary>
    internal const int MaximumPayloadBytes = 16 * 1024;

    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] ActivationAcknowledgement = "GSK1"u8.ToArray();
    private static readonly TimeSpan DefaultCoordinationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _coordinationTimeout;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activationDispatchGate = new();
    private readonly object _stateGate = new();
    private readonly FileStream _profileLock;
    private readonly NamedPipeServerStream _server;
    private readonly Task _serverLoop;
    private readonly TaskCompletionSource<Action<string?>> _activationHandlerReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<string?>? _activationHandler;
    private bool _acceptingActivations = true;
    private Task? _disposeTask;

    private SingleInstanceCoordinator(
        FileStream profileLock,
        NamedPipeServerStream server,
        TimeSpan activationTimeout)
    {
        _profileLock = profileLock;
        _server = server;
        _coordinationTimeout = activationTimeout;
        _serverLoop = ServeAsync();
    }

    public static ValueTask<SingleInstanceStartResult> StartAsync(
        string profileDirectory,
        CancellationToken cancellationToken) =>
        StartAsync(profileDirectory, DefaultCoordinationTimeout, cancellationToken);

    /// <summary>
    /// Becomes the profile's instance, or hands the running one
    /// <paramref name="activationPayload"/> along with the activation.
    /// </summary>
    public static ValueTask<SingleInstanceStartResult> StartAsync(
        string profileDirectory,
        string? activationPayload,
        CancellationToken cancellationToken) =>
        StartAsync(profileDirectory, DefaultCoordinationTimeout, activationPayload, cancellationToken);

    internal static ValueTask<SingleInstanceStartResult> StartAsync(
        string profileDirectory,
        TimeSpan activationTimeout,
        CancellationToken cancellationToken) =>
        StartAsync(profileDirectory, activationTimeout, activationPayload: null, cancellationToken);

    internal static async ValueTask<SingleInstanceStartResult> StartAsync(
        string profileDirectory,
        TimeSpan activationTimeout,
        string? activationPayload,
        CancellationToken cancellationToken)
    {
        var payload = activationPayload is null ? null : StrictUtf8.GetBytes(activationPayload);
        if (payload is { Length: > MaximumPayloadBytes })
        {
            throw new ArgumentException("The activation request is too large.", nameof(activationPayload));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        if (activationTimeout <= TimeSpan.Zero || activationTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(activationTimeout),
                "The activation timeout must be between zero and one minute.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }

        string fullProfileDirectory;
        try
        {
            fullProfileDirectory = Path.GetFullPath(profileDirectory);
            Directory.CreateDirectory(fullProfileDirectory);
        }
        catch (Exception exception) when (IsProfileBoundaryFailure(exception))
        {
            return ProfileUnavailable();
        }

        var lockPath = Path.Combine(fullProfileDirectory, LockFileName);
        var endpointPath = Path.Combine(fullProfileDirectory, EndpointFileName);
        using var coordinationLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        coordinationLifetime.CancelAfter(activationTimeout);
        var activationRejected = false;

        try
        {
            while (true)
            {
                FileStream? profileLock;
                try
                {
                    profileLock = TryAcquireProfileLock(lockPath);
                }
                catch (Exception exception) when (IsProfileBoundaryFailure(exception))
                {
                    return ProfileUnavailable();
                }

                if (profileLock is not null)
                {
                    return await BecomePrimaryAsync(
                            profileLock,
                            endpointPath,
                            activationTimeout,
                            coordinationLifetime.Token)
                        .ConfigureAwait(false);
                }

                if (!activationRejected && TryReadPipeName(endpointPath) is { } pipeName)
                {
                    var attempt = await TryActivateExistingInstanceAsync(
                            pipeName,
                            payload,
                            coordinationLifetime.Token)
                        .ConfigureAwait(false);
                    if (attempt == ActivationAttempt.Rejected && payload is not null)
                    {
                        // An instance from before requests could be carried
                        // knows only the bare activation; it comes forward,
                        // and the request goes no further.
                        attempt = await TryActivateExistingInstanceAsync(
                                pipeName,
                                payload: null,
                                coordinationLifetime.Token)
                            .ConfigureAwait(false);
                    }

                    if (attempt == ActivationAttempt.Activated)
                    {
                        return new SingleInstanceStartResult.ExistingInstanceActivated();
                    }

                    activationRejected = attempt == ActivationAttempt.Rejected;
                }

                await Task.Delay(
                        ActivationPollInterval,
                        coordinationLifetime.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (OperationCanceledException)
        {
            return ActivationUnavailable();
        }
    }

    public void RegisterActivationHandler(Action activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);
        RegisterActivationHandler(_ => activationHandler());
    }

    /// <summary>
    /// Registers what a second launch does here: the handler is given the
    /// request it carried, or null for a bare activation.
    /// </summary>
    public void RegisterActivationHandler(Action<string?> activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);

        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (!_acceptingActivations)
            {
                throw new InvalidOperationException(
                    "The instance no longer accepts activation requests.");
            }

            if (_activationHandler is not null)
            {
                throw new InvalidOperationException(
                    "The existing-instance activation handler is already registered.");
            }

            _activationHandler = activationHandler;
            _activationHandlerReady.TrySetResult(activationHandler);
        }
    }

    public void StopAcceptingActivations()
    {
        lock (_activationDispatchGate)
        {
            lock (_stateGate)
            {
                _acceptingActivations = false;
                _activationHandler = null;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_stateGate)
        {
            _acceptingActivations = false;
            _activationHandler = null;
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task ServeAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await _server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                await HandleConnectionAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // A connected client exceeded the bounded request lifetime. Disconnect it and keep
                // serving future activation attempts.
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (_shutdown.IsCancellationRequested)
            {
                // macOS can surface an internal pipe-transport exception after a pending accept
                // is canceled and the server is disposed. Once shutdown owns the endpoint, that
                // exception is completion of the teardown rather than an activation failure.
                return;
            }
            catch (Exception exception) when (IsActivationBoundaryFailure(exception))
            {
                // A malformed or abandoned same-user request must not terminate the activation
                // endpoint. Disconnect below and continue accepting bounded requests.
            }
            finally
            {
                DisconnectServer();
            }
        }
    }

    private async Task HandleConnectionAsync(CancellationToken shutdownToken)
    {
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        requestLifetime.CancelAfter(RequestTimeout);

        var request = new byte[ActivationRequest.Length];
        await _server.ReadExactlyAsync(request, requestLifetime.Token).ConfigureAwait(false);
        string? payload = null;
        if (CryptographicOperations.FixedTimeEquals(request, PayloadActivationRequest))
        {
            var length = new byte[2];
            await _server.ReadExactlyAsync(length, requestLifetime.Token).ConfigureAwait(false);
            var count = length[0] | (length[1] << 8);
            if (count > MaximumPayloadBytes)
            {
                return;
            }

            var bytes = new byte[count];
            await _server.ReadExactlyAsync(bytes, requestLifetime.Token).ConfigureAwait(false);
            payload = StrictUtf8.GetString(bytes);
        }
        else if (!CryptographicOperations.FixedTimeEquals(request, ActivationRequest))
        {
            return;
        }

        if (!await AcceptActivationAsync(payload, shutdownToken).ConfigureAwait(false))
        {
            return;
        }

        using var responseLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        responseLifetime.CancelAfter(RequestTimeout);
        await _server.WriteAsync(ActivationAcknowledgement, responseLifetime.Token)
            .ConfigureAwait(false);
        await _server.FlushAsync(responseLifetime.Token).ConfigureAwait(false);
    }

    private async ValueTask<bool> AcceptActivationAsync(string? payload, CancellationToken cancellationToken)
    {
        Action<string?>? activationHandler;
        lock (_stateGate)
        {
            if (!_acceptingActivations || _disposeTask is not null)
            {
                return false;
            }

            activationHandler = _activationHandler;
        }

        if (activationHandler is null)
        {
            using var readinessLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readinessLifetime.CancelAfter(_coordinationTimeout);
            try
            {
                activationHandler = await _activationHandlerReady.Task
                    .WaitAsync(readinessLifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (readinessLifetime.IsCancellationRequested)
            {
                return false;
            }
        }

        lock (_activationDispatchGate)
        {
            lock (_stateGate)
            {
                if (!_acceptingActivations
                    || _disposeTask is not null
                    || !ReferenceEquals(_activationHandler, activationHandler))
                {
                    return false;
                }
            }

            if (!InvokeHandler(activationHandler, payload))
            {
                return false;
            }

            lock (_stateGate)
            {
                return _acceptingActivations
                    && _disposeTask is null
                    && ReferenceEquals(_activationHandler, activationHandler);
            }
        }
    }

    private static bool InvokeHandler(Action<string?> activationHandler, string? payload)
    {
        try
        {
            activationHandler(payload);
            return true;
        }
        catch (Exception)
        {
            // The pipe is a process boundary. UI activation failures do not disclose application
            // state or terminate the listener; a later launch can request activation again.
            return false;
        }
    }

    private async Task DisposeCoreAsync()
    {
        _shutdown.Cancel();
        try
        {
            try
            {
                await _server.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsActivationBoundaryFailure(exception))
            {
            }

            try
            {
                await _serverLoop.ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or ObjectDisposedException or IOException)
            {
            }
        }
        finally
        {
            try
            {
                await _profileLock.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _shutdown.Dispose();
            }
        }
    }

    private void DisconnectServer()
    {
        if (!_server.IsConnected)
        {
            return;
        }

        try
        {
            _server.Disconnect();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private TimeSpan RequestTimeout =>
        _coordinationTimeout < DefaultRequestTimeout
            ? _coordinationTimeout
            : DefaultRequestTimeout;

    private static FileStream? TryAcquireProfileLock(string lockPath)
    {
        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async ValueTask<SingleInstanceStartResult> BecomePrimaryAsync(
        FileStream profileLock,
        string endpointPath,
        TimeSpan activationTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pipeName = CreatePipeName();
            try
            {
                PublishPipeName(endpointPath, pipeName);
            }
            catch (Exception exception) when (IsProfileBoundaryFailure(exception))
            {
                return ProfileUnavailable();
            }

            try
            {
                var server = CreateServer(pipeName);
                var coordinator = new SingleInstanceCoordinator(
                    profileLock,
                    server,
                    activationTimeout);
                profileLock = null!;
                return new SingleInstanceStartResult.Primary(coordinator);
            }
            catch (Exception exception) when (IsActivationBoundaryFailure(exception))
            {
                return ActivationUnavailable();
            }
        }
        finally
        {
            if (profileLock is not null)
            {
                await profileLock.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static void PublishPipeName(string endpointPath, string pipeName)
    {
        var bytes = Encoding.ASCII.GetBytes(pipeName);
        using var endpoint = new FileStream(
            endpointPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.None);
        endpoint.Write(bytes);
        endpoint.Flush(flushToDisk: true);
    }

    private static string? TryReadPipeName(string lockPath)
    {
        try
        {
            using var stream = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 1,
                FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > 64)
            {
                return null;
            }

            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            var pipeName = Encoding.ASCII.GetString(bytes);
            return IsValidPipeName(pipeName) ? pipeName : null;
        }
        catch (Exception exception) when (IsProfileBoundaryFailure(exception))
        {
            return null;
        }
    }

    private static async ValueTask<ActivationAttempt> TryActivateExistingInstanceAsync(
        string pipeName,
        byte[]? payload,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var connectionLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionLifetime.CancelAfter(ConnectionAttemptTimeout);
        try
        {
            await pipe.ConnectAsync(connectionLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ActivationAttempt.Unavailable;
        }
        catch (Exception exception) when (IsActivationBoundaryFailure(exception))
        {
            return ActivationAttempt.Unavailable;
        }

        try
        {
            if (payload is null)
            {
                await pipe.WriteAsync(ActivationRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var message = new byte[PayloadActivationRequest.Length + 2 + payload.Length];
                PayloadActivationRequest.CopyTo(message, 0);
                message[PayloadActivationRequest.Length] = (byte)(payload.Length & 0xFF);
                message[PayloadActivationRequest.Length + 1] = (byte)(payload.Length >> 8);
                payload.CopyTo(message, PayloadActivationRequest.Length + 2);
                await pipe.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            }

            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            var acknowledgement = new byte[ActivationAcknowledgement.Length];
            await pipe.ReadExactlyAsync(acknowledgement, cancellationToken)
                .ConfigureAwait(false);
            return CryptographicOperations.FixedTimeEquals(
                acknowledgement,
                ActivationAcknowledgement)
                ? ActivationAttempt.Activated
                : ActivationAttempt.Rejected;
        }
        catch (OperationCanceledException)
        {
            return ActivationAttempt.Rejected;
        }
        catch (Exception exception) when (IsActivationBoundaryFailure(exception))
        {
            return ActivationAttempt.Rejected;
        }
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static string CreatePipeName()
    {
        Span<byte> identity = stackalloc byte[PipeIdentityByteCount];
        RandomNumberGenerator.Fill(identity);
        return $"{PipeNamePrefix}{Convert.ToHexString(identity)}";
    }

    private static bool IsValidPipeName(string pipeName) =>
        pipeName.Length == PipeNamePrefix.Length + (PipeIdentityByteCount * 2)
        && pipeName.StartsWith(PipeNamePrefix, StringComparison.Ordinal)
        && pipeName.AsSpan(PipeNamePrefix.Length).IndexOfAnyExcept(
            "0123456789ABCDEF") < 0;

    private static bool IsProfileBoundaryFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;

    private static bool IsActivationBoundaryFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or TimeoutException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;

    private static SingleInstanceStartResult Cancelled() =>
        new SingleInstanceStartResult.Failure(new SingleInstanceError(
            SingleInstanceErrorCode.Cancelled,
            "single_instance_cancelled",
            "Application activation was canceled."));

    private static SingleInstanceStartResult ProfileUnavailable() =>
        new SingleInstanceStartResult.Failure(new SingleInstanceError(
            SingleInstanceErrorCode.ProfileUnavailable,
            "single_instance_profile_unavailable",
            "Asura couldn’t open the folder that holds your profile. Check that its disk is available, then try again."));

    private static SingleInstanceStartResult ActivationUnavailable() =>
        new SingleInstanceStartResult.Failure(new SingleInstanceError(
            SingleInstanceErrorCode.ActivationUnavailable,
            "single_instance_activation_unavailable",
            "Another Asura process still has your profile open and isn’t responding. Quit it in Activity Monitor, then try again."));

    private enum ActivationAttempt
    {
        Activated,
        Rejected,
        Unavailable,
    }
}
