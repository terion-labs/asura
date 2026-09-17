using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Asura.Application;
using k8s;

namespace Asura.Kubernetes;

/// <summary>
/// Authenticated Kubernetes transport for the owned connection worker. The owner supplies
/// resolved credentials and cancels/disposes this session when execution authority ends.
/// No kubeconfig files, ambient proxies or external commands are consulted here.
/// </summary>
public sealed partial class KubernetesClientSession : IKubernetesClientSession
{
    public KubernetesSessionFeatures Features => KubernetesSessionFeatures.Watch | KubernetesSessionFeatures.FollowLogs
        | KubernetesSessionFeatures.Exec | KubernetesSessionFeatures.PortForward | KubernetesSessionFeatures.Mutations
        | KubernetesSessionFeatures.ManifestConversion | KubernetesSessionFeatures.Metrics
        | KubernetesSessionFeatures.MetricHistory | KubernetesSessionFeatures.HelmRead
        | KubernetesSessionFeatures.NodeMaintenance | KubernetesSessionFeatures.HelmChanges;

    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private readonly KubernetesCredentialRefresh? _refresh;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly List<k8s.Kubernetes> _clients = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<DelegatingHandler[]>? _handlers;
    private readonly string _helmExecutable;
    private k8s.Kubernetes _client;
    private KubernetesResolvedConnection _connection;
    private bool _disposed;

    public KubernetesClientSession(
        KubernetesResolvedConnection connection,
        KubernetesCredentialRefresh? refresh = null,
        Func<DelegatingHandler[]>? handlers = null,
        string helmExecutable = "helm")
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        _refresh = refresh;
        _handlers = handlers;
        _helmExecutable = helmExecutable;
        _client = CreateClient(connection);
        _clients.Add(_client);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_reviewGate) { _reviews.Clear(); }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _refreshLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            foreach (k8s.Kubernetes client in _clients)
            {
                client.Dispose();
            }
        }
        finally
        {
            _refreshLock.Release();
        }

        _lifetime.Dispose();
    }

    private k8s.Kubernetes CreateClient(KubernetesResolvedConnection connection)
    {
        try
        {
            return CreateClientCore(connection);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Kubernetes authentication or TLS configuration is invalid.");
        }
    }

    private k8s.Kubernetes CreateClientCore(KubernetesResolvedConnection connection)
    {
        if (!connection.ApiServer.IsAbsoluteUri
            || connection.ApiServer.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(connection.ApiServer.UserInfo)
            || !string.IsNullOrEmpty(connection.ApiServer.Query)
            || !string.IsNullOrEmpty(connection.ApiServer.Fragment))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The Kubernetes API server URL is invalid.");
        }

        if (connection.BearerToken is { } token && (token.Length == 0 || token.Any(static character => character <= ' ' || character > '~')))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The Kubernetes bearer token contains invalid characters.");
        }

        if (connection.TlsServerName is { Length: > 0 } serverName && Uri.CheckHostName(serverName) == UriHostNameType.Unknown)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The Kubernetes TLS server name is invalid.");
        }

        var configuration = new KubernetesClientConfiguration
        {
            Host = connection.ApiServer.AbsoluteUri,
            Namespace = connection.Namespace,
            AccessToken = connection.BearerToken,
            SkipTlsVerify = connection.AllowInsecureTls,
            TlsServerName = connection.TlsServerName,
            ClientCertificateData = EncodePem(connection.ClientCertificatePem),
            ClientCertificateKeyData = EncodePem(connection.ClientKeyPem),
            HttpClientTimeout = Timeout.InfiniteTimeSpan,
            FirstMessageHandlerSetup = static handler =>
            {
                handler.UseProxy = false;
                handler.UseCookies = false;
                handler.AllowAutoRedirect = false;
                handler.ConnectTimeout = TimeSpan.FromSeconds(20);
                handler.MaxResponseHeadersLength = 32;
                handler.MaxConnectionsPerServer = 16;
            },
        };
        if (connection.CertificateAuthorityPem is { } pem)
        {
            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPem(pem);
            configuration.SslCaCerts = certificates;
        }

        return new k8s.Kubernetes(configuration, _handlers?.Invoke() ?? [])
        {
            CreateWebSocketBuilder = () => new NonRetryingWebSocketBuilder(connection.TlsServerName),
        };
    }

    private static string? EncodePem(string? pem) => pem is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));

    private async ValueTask<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_refresh is not null && _connection.CredentialExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            await RefreshAsync(_client, cancellationToken).ConfigureAwait(false);
        }

        k8s.Kubernetes client = _client;
        if (client.Credentials is { } credentials)
        {
            await credentials.ProcessHttpRequestAsync(message, cancellationToken).ConfigureAwait(false);
        }

        if (_connection.TlsServerName is { Length: > 0 } serverName)
        {
            message.Headers.Host = serverName;
        }

        try
        {
            return await client.HttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes API connection failed.", retryable: true);
        }
    }

    private async ValueTask<JsonDocument> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(path, cancellationToken).ConfigureAwait(false);
        byte[] body = await ReadBoundedAsync(response, MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "The Kubernetes API returned invalid JSON.");
        }
    }

    private async ValueTask<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken)
    {
        k8s.Kubernetes original = _client;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_client.BaseUri, path));
        HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized && _refresh is not null)
        {
            response.Dispose();
            await RefreshAsync(original, cancellationToken).ConfigureAwait(false);
            using var retry = new HttpRequestMessage(HttpMethod.Get, new Uri(_client.BaseUri, path));
            response = await SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            HttpStatusCode status = response.StatusCode;
            response.Dispose();
            throw Failure(status);
        }

        return response;
    }

    private async ValueTask RefreshAsync(k8s.Kubernetes original, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(original, _client) && _refresh is not null)
            {
                KubernetesResolvedConnection replacement = await _refresh(cancellationToken).ConfigureAwait(false);
                if (replacement.ApiServer != _connection.ApiServer
                    || !string.Equals(replacement.TlsServerName, _connection.TlsServerName, StringComparison.Ordinal)
                    || !string.Equals(replacement.CertificateAuthorityPem, _connection.CertificateAuthorityPem, StringComparison.Ordinal)
                    || replacement.AllowInsecureTls != _connection.AllowInsecureTls)
                {
                    throw new KubernetesRequestException(KubernetesErrorCode.TargetChanged, "Credential refresh cannot change the Kubernetes server identity.");
                }

                k8s.Kubernetes client = CreateClient(replacement);
                _clients.Add(client);
                // Old active streams are allowed a short overlap; do not retain an unbounded
                // collection of credentials and connection pools after repeated renewal.
                if (_clients.Count > 4)
                {
                    _clients[0].Dispose();
                    _clients.RemoveAt(0);
                }
                _connection = replacement;
                _client = client;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "The Kubernetes response exceeds the configured size limit.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "The Kubernetes response exceeds the configured size limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static KubernetesRequestException Failure(HttpStatusCode status) => new(
        status switch
        {
            HttpStatusCode.Unauthorized => KubernetesErrorCode.Unauthorized,
            HttpStatusCode.Forbidden => KubernetesErrorCode.Forbidden,
            HttpStatusCode.NotFound => KubernetesErrorCode.NotFound,
            HttpStatusCode.Conflict => KubernetesErrorCode.Conflict,
            HttpStatusCode.Gone => KubernetesErrorCode.ResourceExpired,
            HttpStatusCode.TooManyRequests => KubernetesErrorCode.TooManyRequests,
            _ => KubernetesErrorCode.ServerUnavailable,
        },
        $"The Kubernetes API returned HTTP {(int)status}.",
        (int)status,
        status is HttpStatusCode.TooManyRequests || (int)status >= 500);
}
