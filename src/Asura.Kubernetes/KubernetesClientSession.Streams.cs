using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Asura.Application;
using k8s;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public ValueTask<string> ConvertManifestToJsonAsync(string manifest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> documents = KubernetesYaml.ToJsonDocuments(manifest);
        if (documents.Count != 1)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Edit one resource manifest at a time.");
        }

        return ValueTask.FromResult(documents[0]);
    }

    public async IAsyncEnumerable<string> FollowLogsAsync(
        KubernetesLogRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePod(request.Pod);
        if (request.TailLines is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await InspectAsync(request.Pod, linked.Token).ConfigureAwait(false);
        var query = new List<KeyValuePair<string, string>>
        {
            new("follow", "true"),
            new("previous", request.Previous ? "true" : "false"),
            new("tailLines", request.TailLines.ToString(CultureInfo.InvariantCulture)),
            new("timestamps", request.Timestamps ? "true" : "false"),
        };
        AddQuery(query, "container", request.Container);
        AddQuery(query, "sinceSeconds", request.SinceSeconds?.ToString(CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await GetAsync(WithQuery(ResourcePath(request.Pod) + "/log", query), linked.Token).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        await foreach (string line in ReadLinesAsync(stream, linked.Token, 262144).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    public async ValueTask<IKubernetesExecSession> OpenExecAsync(KubernetesExecRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePod(request.Pod);
        ValidateSegment(request.Container);
        if (request.Command.Count is < 1 or > 128 || request.Command.Any(static argument => argument.Length > 8192 || argument.Contains('\0', StringComparison.Ordinal)))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The pod command exceeds its argument limits.");
        }

        string[] command = [.. request.Command];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await InspectAsync(request.Pod, linked.Token).ConfigureAwait(false);
        WebSocket socket;
        try
        {
            socket = await _client.WebSocketNamespacedPodExecAsync(request.Pod.Name, request.Pod.Namespace,
                command, request.Container, stderr: !request.Tty, stdin: true, stdout: true, tty: request.Tty,
                webSocketSubProtocol: "v5.channel.k8s.io", cancellationToken: linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException or k8s.Autorest.HttpOperationException or k8s.KubernetesException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes API could not open the pod terminal.");
        }

        return new KubernetesExecSession(new KubernetesChannelConnection(socket, portForward: false, _lifetime.Token), request.Tty);
    }

    public ValueTask<IKubernetesPortForward> StartPortForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePod(request.Pod);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RemotePort is < 1 or > 65535 || request.LocalPort is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        return ValueTask.FromResult<IKubernetesPortForward>(new KubernetesPortForward(request, ConnectForwardAsync, _lifetime.Token));
    }

    private async ValueTask<KubernetesChannelConnection> ConnectForwardAsync(KubernetesPortForwardRequest request, CancellationToken cancellationToken)
    {
        await InspectAsync(request.Pod, cancellationToken).ConfigureAwait(false);
        try
        {
            WebSocket socket = await _client.WebSocketNamespacedPodPortForwardAsync(request.Pod.Name, request.Pod.Namespace,
                [request.RemotePort], WebSocketProtocol.V4BinaryWebsocketProtocol, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new KubernetesChannelConnection(socket, portForward: true, cancellationToken);
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException or k8s.Autorest.HttpOperationException or k8s.KubernetesException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes API could not open the pod port forward.");
        }
    }

    private sealed class NonRetryingWebSocketBuilder(string? tlsServerName) : WebSocketBuilder
    {
        public override async Task<WebSocket> BuildAndConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            // Do not inherit an ambient proxy. Execution placement already owns routing.
            Options.Proxy = new WebProxy();
            if (!string.IsNullOrEmpty(tlsServerName))
            {
                Options.SetRequestHeader("Host", tlsServerName);
            }

            try
            {
                return await base.BuildAndConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // The SDK's default upgrade-error fallback repeats the request and includes
                // response bodies. A failed exec handshake must not replay a command.
                throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes streaming connection could not be established.");
            }
        }
    }
}
