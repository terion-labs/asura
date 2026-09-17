using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.Kubernetes;

/// <summary>Runs only inside the owning backend. Every invocation receives an isolated resolved configuration.</summary>
internal static class KubernetesHelmCommand
{
    internal static async ValueTask<JsonDocument> ReadAsync(KubernetesResolvedConnection connection,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken, string executable = "helm")
    {
        byte[] bytes = await RunAsync(connection, arguments, cancellationToken, executable).ConfigureAwait(false);
        try { return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "Helm returned invalid release metadata."); }
    }

    internal static async ValueTask<string> ReadChartAsync(KubernetesResolvedConnection connection, string reference, string version, CancellationToken cancellationToken, string executable = "helm") =>
        Encoding.UTF8.GetString(await RunAsync(connection, ["show", "chart", reference, "--version", version], cancellationToken,
            executable, jsonOutput: false).ConfigureAwait(false));

    internal static async ValueTask<KubernetesHelmChangeResult> ChangeAsync(KubernetesResolvedConnection connection,
        IReadOnlyList<string> arguments, string? valuesJson, int timeoutSeconds, CancellationToken cancellationToken, string executable = "helm")
    {
        bool dispatched = false;
        try
        {
            _ = await RunAsync(connection, arguments, cancellationToken, executable, jsonOutput: false, valuesJson,
                timeoutSeconds, () => dispatched = true).ConfigureAwait(false);
            return new(KubernetesMutationOutcome.Applied);
        }
        catch (Exception exception) when (exception is KubernetesRequestException or OperationCanceledException or IOException or Win32Exception or InvalidOperationException)
        {
            return new(dispatched ? KubernetesMutationOutcome.OutcomeUnknown : KubernetesMutationOutcome.NotDispatched,
                dispatched ? "helm_outcome_unknown" : exception is KubernetesRequestException request ? request.Code.ToString() : "helm_not_dispatched");
        }
    }

    private static async ValueTask<byte[]> RunAsync(KubernetesResolvedConnection connection,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken, string executable = "helm", bool jsonOutput = true,
        string? valuesJson = null, int timeoutSeconds = 30, Action? onDispatch = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "asura-helm-" + Path.GetRandomFileName());
        CreatePrivateDirectory(directory);
        try
        {
            string configPath = Path.Combine(directory, "config.json");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            await using (var config = new FileStream(configPath, options))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Configuration(connection));
                await config.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            var start = CreateStart(executable, directory, configPath, arguments, jsonOutput);
            if (valuesJson is not null)
            {
                string valuesPath = Path.Combine(directory, "values.json");
                await using var values = new FileStream(valuesPath, options);
                await values.WriteAsync(Encoding.UTF8.GetBytes(valuesJson), cancellationToken).ConfigureAwait(false);
                await values.FlushAsync(cancellationToken).ConfigureAwait(false);
                start.ArgumentList.Add("--values");
                start.ArgumentList.Add(valuesPath);
            }

            return await ExecuteAsync(start, timeoutSeconds, onDispatch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidResponse, "Helm could not read release metadata in this execution environment.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static ProcessStartInfo CreateStart(string executable, string directory, string configPath, IReadOnlyList<string> arguments, bool jsonOutput = true)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        foreach (string argument in new[] { "--kubeconfig", configPath, "--kube-context", "asura" }) { start.ArgumentList.Add(argument); }
        if (jsonOutput) { start.ArgumentList.Add("--output"); start.ArgumentList.Add("json"); }
        foreach (string key in start.Environment.Keys.ToArray())
        {
            if (key.StartsWith("HELM_", StringComparison.OrdinalIgnoreCase) || key.Equals("KUBECONFIG", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("_PROXY", StringComparison.OrdinalIgnoreCase)) { start.Environment.Remove(key); }
        }

        start.Environment["HELM_DRIVER"] = "secret";
        start.Environment["HELM_CACHE_HOME"] = Path.Combine(directory, "cache");
        start.Environment["HELM_CONFIG_HOME"] = Path.Combine(directory, "helm-config");
        start.Environment["HELM_DATA_HOME"] = Path.Combine(directory, "data");
        start.Environment["HELM_PLUGINS"] = Path.Combine(directory, "plugins");
        return start;
    }

    private static async ValueTask<byte[]> ExecuteAsync(ProcessStartInfo start, int timeoutSeconds, Action? onDispatch, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start };
        bool started = false;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started) { throw HelmFailure(); }
            onDispatch?.Invoke();
            process.StandardInput.Close();
            return await ReadProcessAsync(process, lifetime).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Install Helm in this workspace's execution environment to browse releases.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Helm metadata request timed out or exceeded its output limit.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<byte[]> ReadProcessAsync(Process process, CancellationTokenSource lifetime)
    {
        Task<byte[]> output = ReadOutputAsync(process.StandardOutput.BaseStream, 4 * 1024 * 1024, lifetime);
        Task<byte[]> errors = ReadOutputAsync(process.StandardError.BaseStream, 65536, lifetime);
        await Task.WhenAll(output, errors, process.WaitForExitAsync(lifetime.Token)).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string error = Encoding.UTF8.GetString(await errors.ConfigureAwait(false));
            if (error.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.Forbidden, "Access to Helm release metadata is forbidden.");
            }

            throw HelmFailure();
        }

        return await output.ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadOutputAsync(Stream input, int maximum, CancellationTokenSource lifetime)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum)
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
                throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "Helm output exceeded its size limit.");
            }

            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    internal static string Configuration(KubernetesResolvedConnection connection)
    {
        var cluster = new JsonObject { ["server"] = connection.ApiServer.AbsoluteUri, ["insecure-skip-tls-verify"] = connection.AllowInsecureTls };
        if (connection.TlsServerName is not null) { cluster["tls-server-name"] = connection.TlsServerName; }
        if (connection.CertificateAuthorityPem is not null) { cluster["certificate-authority-data"] = Pem(connection.CertificateAuthorityPem); }
        var user = new JsonObject();
        if (connection.BearerToken is not null) { user["token"] = connection.BearerToken; }
        if (connection.ClientCertificatePem is not null) { user["client-certificate-data"] = Pem(connection.ClientCertificatePem); }
        if (connection.ClientKeyPem is not null) { user["client-key-data"] = Pem(connection.ClientKeyPem); }
        return new JsonObject
        {
            ["apiVersion"] = "v1",
            ["kind"] = "Config",
            ["current-context"] = "asura",
            ["clusters"] = new JsonArray(new JsonObject { ["name"] = "asura", ["cluster"] = cluster }),
            ["users"] = new JsonArray(new JsonObject { ["name"] = "asura", ["user"] = user }),
            ["contexts"] = new JsonArray(new JsonObject
            {
                ["name"] = "asura",
                ["context"] = new JsonObject
                {
                    ["cluster"] = "asura",
                    ["user"] = "asura",
                    ["namespace"] = connection.Namespace,
                }
            }),
        }.ToJsonString();
    }

    private static string Pem(string pem) => Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));

    private static void CreatePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier owner = identity.User ?? throw new UnauthorizedAccessException("The execution identity is unavailable.");
        var permissions = new DirectorySecurity();
        permissions.SetOwner(owner);
        permissions.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        permissions.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(permissions);
    }

    private static KubernetesRequestException HelmFailure() => new(KubernetesErrorCode.ConnectionFailed, "Helm could not read release metadata. Check its installation and this context's access.");
}
