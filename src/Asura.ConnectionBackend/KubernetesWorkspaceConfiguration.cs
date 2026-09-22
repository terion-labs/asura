using Asura.Application;
using Asura.Kubernetes;

namespace Asura.ConnectionBackend;

/// <summary>Reads configuration in its owning filesystem without executing credential commands.</summary>
internal static class KubernetesWorkspaceConfiguration
{
    internal static async Task<KubernetesConfigurationReview> ReviewAsync(KubernetesWorkspaceOpen configuration, CancellationToken token)
    {
        var plans = await ReadAsync(configuration, token).ConfigureAwait(false);
        return new([.. plans.Select(plan => new KubernetesContextReview(plan.ContextName, plan.Connection.Namespace,
            plan.Connection.ApiServer.AbsoluteUri, plan.Exec is not null ? "Credential executable"
                : plan.Connection.ClientCertificatePem is not null || plan.ClientCertificatePath is not null ? "Client certificate"
                : plan.Connection.BearerToken is not null || plan.TokenFilePath is not null ? "Bearer token" : "Anonymous",
            plan.Connection.AllowInsecureTls, plan.Exec?.Command, plan.Exec?.Arguments ?? [],
            plan.Exec?.Environment.Keys.ToArray() ?? [], plan.Exec?.Fingerprint))]);
    }

    internal static async Task<IReadOnlyList<KubernetesKubeconfigPlan>> ReadAsync(KubernetesWorkspaceOpen configuration, CancellationToken token)
    {
        try
        {
            if ((configuration.KubeconfigPath is null) == (configuration.ManagedKubeconfig is null))
            {
                throw InvalidRequest();
            }
            var path = configuration.KubeconfigPath;
            if (path is not null)
            {
                if (string.Equals(path, "~", StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
                {
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : "");
                }
                path = Path.GetFullPath(path);
            }
            var yaml = configuration.ManagedKubeconfig ?? await ReadConfigurationAsync(path!, token).ConfigureAwait(false);
            return KubernetesKubeconfigReader.Read(yaml, path is null ? null : Path.GetDirectoryName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration,
                "The kubeconfig could not be read in the authentication environment. Check its path and file permissions, then review the configuration again.");
        }
    }

    private static async Task<string> ReadConfigurationAsync(string path, CancellationToken token)
    {
        const int maximumBytes = 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) { throw InvalidRequest(); }
        var bytes = new byte[maximumBytes + 1];
        try
        {
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                if (read == 0) { break; }
                count += read;
            }
            if (count > maximumBytes) { throw InvalidRequest(); }
            return System.Text.Encoding.UTF8.GetString(bytes, 0, count);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }

    private static KubernetesRequestException InvalidRequest() => new(KubernetesErrorCode.InvalidConfiguration,
        "The Kubernetes backend configuration or request is invalid.");
}
