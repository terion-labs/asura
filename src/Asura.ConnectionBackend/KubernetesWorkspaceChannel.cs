using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Asura.Application;

namespace Asura.ConnectionBackend;

/// <summary>
/// Owns worker framing. Credential replies bypass the command queue so renewal during a
/// port-forward connection cannot compete with its input reader. Negative IDs identify
/// credential exchanges; positive IDs retain the existing command sequence.
/// </summary>
internal sealed class KubernetesWorkspaceChannel : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly Channel<(KubernetesWorkspaceRequest Request, int Bytes)> _commands = Channel.CreateUnbounded<(KubernetesWorkspaceRequest Request, int Bytes)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly object _gate = new();
    private readonly Task _reader;
    private TaskCompletionSource<KubernetesWorkspaceCredentialReply>? _pending;
    private long _credentialId;
    private int _queuedBytes;
    private bool _closed;

    internal KubernetesWorkspaceChannel(Stream input, Stream output, CancellationToken token)
    {
        _input = input;
        _output = output;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _reader = ReadFramesAsync();
    }

    internal async ValueTask<KubernetesWorkspaceRequest> ReadAsync(CancellationToken token)
    {
        try
        {
            var request = await _commands.Reader.ReadAsync(token).ConfigureAwait(false);
            Interlocked.Add(ref _queuedBytes, -request.Bytes);
            return request.Request;
        }
        catch (ChannelClosedException exception)
        {
            if (exception.InnerException is { } failure) { throw new IOException("The Kubernetes input channel failed.", failure); }
            throw new EndOfStreamException();
        }
    }

    internal async Task ReplyAsync(KubernetesWorkspaceResponse response, CancellationToken token)
    {
        await _writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await BackendJsonFrames.WriteAsync(_output, response with { IsResponse = true },
                KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceResponse, token).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    internal async ValueTask<KubernetesResolvedConnection> RefreshAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        TaskCompletionSource<KubernetesWorkspaceCredentialReply> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long id;
        lock (_gate)
        {
            if (_closed) { throw new IOException("The host credential channel closed."); }
            if (_pending is not null) { throw new InvalidOperationException("A credential renewal is already pending."); }
            id = checked(--_credentialId);
            _pending = pending;
        }
        try
        {
            await ReplyAsync(new(id, CredentialsRequested: true), linked.Token).ConfigureAwait(false);
            var reply = await pending.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            if (reply.Error is { } error && Enum.IsDefined(error) && reply.Connection is null)
            {
                throw new KubernetesRequestException(error,
                    reply.ErrorMessage is { Length: > 0 and <= 2048 } detail ? detail : "Host authentication failed. Sign in on the host and retry.");
            }
            if (reply.Error is not null || reply.Connection is null) { throw new InvalidDataException("Invalid credential response."); }
            return reply.Connection;
        }
        finally { lock (_gate) { _pending = null; } }
    }

    private async Task ReadFramesAsync()
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var bytes = await DatabaseOperationProtocol.ReadFrameAsync(_input, BackendJsonFrames.MaximumBytes, _lifetime.Token).ConfigureAwait(false);
                KubernetesWorkspaceRequest request;
                try
                {
                    request = JsonSerializer.Deserialize(bytes, KubernetesWorkspaceJsonContext.Default.KubernetesWorkspaceRequest)
                    ?? throw new InvalidDataException("The backend frame is invalid.");
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                if (request.Operation == KubernetesWorkspaceOperation.Credentials)
                {
                    lock (_gate)
                    {
                        if (_pending is null || request.Id != _credentialId || request.Credentials is null
                            || !_pending.TrySetResult(request.Credentials)) { throw new InvalidDataException("Unexpected credential response."); }
                    }
                    continue;
                }
                // Input may arrive while authentication is in flight. Bound buffered data
                // without blocking the reader that must deliver the credential reply.
                if (request.Credentials is not null || Interlocked.Add(ref _queuedBytes, bytes.Length) > BackendJsonFrames.MaximumBytes)
                { throw new InvalidDataException("The Kubernetes input queue exceeds its limit."); }
                await _commands.Writer.WriteAsync((request, bytes.Length), _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (EndOfStreamException) { }
        catch (Exception exception) { failure = exception; }
        finally
        {
            _commands.Writer.TryComplete(failure);
            lock (_gate)
            {
                _closed = true;
                _pending?.TrySetException(new IOException("The host credential channel closed."));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _reader.ConfigureAwait(false);
        while (_commands.Reader.TryRead(out var request))
        {
            if (request.Request.Data is { } data) { Array.Clear(data); }
        }
        _lifetime.Dispose();
    }
}
