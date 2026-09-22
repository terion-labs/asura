using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.Kubernetes;

/// <summary>
/// Resolves a reviewed context in its owning execution environment. Exec output and file
/// contents never appear in failures. The owner reuses ResolveAsync for credential renewal.
/// </summary>
public sealed class KubernetesCredentialResolver
{
    private const int MaximumCredentialBytes = 4 * 1024 * 1024;
    private readonly KubernetesKubeconfigPlan _plan;
    private readonly string? _trustedExecFingerprint;
    private readonly Func<string, string?>? _findExecutable;

    public KubernetesCredentialResolver(KubernetesKubeconfigPlan plan, string? trustedExecFingerprint = null,
        Func<string, string?>? findExecutable = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        _trustedExecFingerprint = trustedExecFingerprint;
        _findExecutable = findExecutable;
    }

    public async ValueTask<KubernetesResolvedConnection> ResolveAsync(CancellationToken cancellationToken)
    {
        KubernetesResolvedConnection connection = _plan.Connection with
        {
            CertificateAuthorityPem = await ReadMaterialAsync(_plan.CertificateAuthorityPath, _plan.Connection.CertificateAuthorityPem, cancellationToken).ConfigureAwait(false),
            ClientCertificatePem = await ReadMaterialAsync(_plan.ClientCertificatePath, _plan.Connection.ClientCertificatePem, cancellationToken).ConfigureAwait(false),
            ClientKeyPem = await ReadMaterialAsync(_plan.ClientKeyPath, _plan.Connection.ClientKeyPem, cancellationToken).ConfigureAwait(false),
            BearerToken = (await ReadMaterialAsync(_plan.TokenFilePath, _plan.Connection.BearerToken, cancellationToken).ConfigureAwait(false))?.Trim(),
        };
        if (_plan.Exec is { } exec)
        {
            if (!string.Equals(exec.Fingerprint, _trustedExecFingerprint, StringComparison.Ordinal))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.Unauthorized, "Review and trust this context's credential executable before connecting.");
            }

            if (string.Equals(exec.InteractiveMode, "Always", StringComparison.Ordinal))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.Unauthorized, "The credential executable requires interactive login. Sign in using a terminal in this execution environment.");
            }

            connection = await ExecuteAsync(exec, connection, cancellationToken).ConfigureAwait(false);
        }

        if ((connection.ClientCertificatePem is null) != (connection.ClientKeyPem is null))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Client certificate authentication requires both certificate and private key.");
        }

        return connection;
    }

    private static async ValueTask<string?> ReadMaterialAsync(string? path, string? embedded, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return embedded;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
            if (stream.Length > MaximumCredentialBytes)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A Kubernetes credential file exceeds its size limit.");
            }

            return await ReadTextAsync(stream, MaximumCredentialBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A Kubernetes credential file is unavailable in this execution environment.");
        }
    }

    private async ValueTask<KubernetesResolvedConnection> ExecuteAsync(KubernetesExecPlan exec, KubernetesResolvedConnection connection, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        // Resolve only after verifying the exact reviewed command. The backend supplies
        // its own environment's lookup; an isolated worker never searches the host.
        var executable = _findExecutable is null ? exec.Command : _findExecutable(exec.Command)
            ?? throw MissingExecutable();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in exec.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in exec.Environment)
        {
            start.Environment[key] = value;
        }

        start.Environment["KUBERNETES_EXEC_INFO"] = ExecInfo(exec, connection);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                throw CredentialFailure();
            }

            process.StandardInput.Close();
            Task<string> output = ReadTextAsync(process.StandardOutput.BaseStream, MaximumCredentialBytes, timeout.Token).AsTask();
            Task<string> errors = ReadTextAsync(process.StandardError.BaseStream, 65536, timeout.Token).AsTask();
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw CredentialFailure();
            }

            return ParseCredential(await output.ConfigureAwait(false), exec.ApiVersion, connection);
        }
        catch (Win32Exception)
        {
            throw MissingExecutable();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw CredentialFailure();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Unauthorized, "The Kubernetes credential executable timed out.");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                // Starting may have failed; no child was acquired in that case.
            }
        }
    }

    private static string ExecInfo(KubernetesExecPlan exec, KubernetesResolvedConnection connection)
    {
        var spec = new JsonObject { ["interactive"] = false };
        if (exec.ProvideClusterInfo)
        {
            spec["cluster"] = new JsonObject
            {
                ["server"] = connection.ApiServer.AbsoluteUri,
                ["tls-server-name"] = connection.TlsServerName,
                ["insecure-skip-tls-verify"] = connection.AllowInsecureTls,
                ["certificate-authority-data"] = connection.CertificateAuthorityPem is null ? null
                    : Convert.ToBase64String(Encoding.UTF8.GetBytes(connection.CertificateAuthorityPem)),
            };
        }

        return new JsonObject { ["apiVersion"] = exec.ApiVersion, ["kind"] = "ExecCredential", ["spec"] = spec }.ToJsonString();
    }

    private static KubernetesResolvedConnection ParseCredential(string json, string apiVersion, KubernetesResolvedConnection connection)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (!string.Equals(Text(root, "apiVersion"), apiVersion, StringComparison.Ordinal)
                || !string.Equals(Text(root, "kind"), "ExecCredential", StringComparison.Ordinal)
                || !root.TryGetProperty("status", out JsonElement status))
            {
                throw CredentialFailure();
            }

            string? token = Text(status, "token");
            string? certificate = Text(status, "clientCertificateData");
            string? key = Text(status, "clientKeyData");
            if (string.IsNullOrEmpty(token) && (string.IsNullOrEmpty(certificate) || string.IsNullOrEmpty(key)))
            {
                throw CredentialFailure();
            }

            DateTimeOffset? expiry = null;
            if (Text(status, "expirationTimestamp") is { Length: > 0 } expiration)
            {
                if (!DateTimeOffset.TryParse(expiration, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                    || parsed <= DateTimeOffset.UtcNow)
                {
                    throw CredentialFailure();
                }

                expiry = parsed;
            }

            return connection with { BearerToken = token, ClientCertificatePem = certificate, ClientKeyPem = key, CredentialExpiresAt = expiry };
        }
        catch (JsonException)
        {
            throw CredentialFailure();
        }
    }

    private static async ValueTask<string> ReadTextAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Kubernetes credential output exceeds its size limit.");
            }

            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static string? Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement result) && result.ValueKind == JsonValueKind.String
            ? result.GetString() : null;

    private static KubernetesRequestException MissingExecutable() =>
        new(KubernetesErrorCode.InvalidConfiguration,
            "The approved Kubernetes credential command could not be started in this execution environment. "
            + "Review the connection to see the command, then install its CLI here or correct its executable path. "
            + "Isolated workspaces do not use CLI tools or login files installed on the host.");

    private static KubernetesRequestException CredentialFailure() =>
        new(KubernetesErrorCode.Unauthorized, "The Kubernetes credential executable failed or returned invalid credentials. Sign in with its CLI in this execution environment, then retry.");
}
