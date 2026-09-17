using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes;

/// <summary>Reads kubeconfig as data only. Does not open referenced files or execute authentication plugins.</summary>
public static class KubernetesKubeconfigReader
{
    public static IReadOnlyList<KubernetesKubeconfigPlan> Read(string yaml, string? sourceDirectory = null)
    {
        IReadOnlyList<string> documents = KubernetesYaml.ToJsonDocuments(yaml);
        if (documents.Count != 1)
        {
            throw Invalid("A kubeconfig must contain exactly one document.");
        }

        using JsonDocument document = JsonDocument.Parse(documents[0]);
        JsonElement root = document.RootElement;
        var clusters = Entries(root, "clusters");
        var users = Entries(root, "users");
        var contexts = Entries(root, "contexts");
        var plans = new List<KubernetesKubeconfigPlan>();
        foreach ((string name, JsonElement entry) in contexts)
        {
            JsonElement context = Property(entry, "context");
            if (!clusters.TryGetValue(Text(context, "cluster"), out JsonElement clusterEntry))
            {
                throw Invalid("A kubeconfig context refers to an unknown cluster.");
            }

            JsonElement cluster = Property(clusterEntry, "cluster");
            if (!Uri.TryCreate(Text(cluster, "server"), UriKind.Absolute, out Uri? endpoint)
                || endpoint.Scheme is not ("https" or "http") || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            {
                throw Invalid("A kubeconfig cluster contains an invalid API server URL.");
            }

            if (Text(cluster, "proxy-url").Length > 0)
            {
                throw Invalid("Kubeconfig proxy-url must be migrated to an explicit Asura workspace route.");
            }

            JsonElement user = default;
            string userName = Text(context, "user");
            if (userName.Length > 0)
            {
                if (!users.TryGetValue(userName, out JsonElement userEntry))
                {
                    throw Invalid("A kubeconfig context refers to an unknown user.");
                }

                user = Property(userEntry, "user");
            }

            if (Property(user, "auth-provider").ValueKind != JsonValueKind.Undefined || Text(user, "username").Length > 0)
            {
                throw Invalid("This kubeconfig authentication method requires migration to token, certificate or exec credentials.");
            }

            string ns = Text(context, "namespace");
            var connection = new KubernetesResolvedConnection(endpoint, ns.Length == 0 ? "default" : ns,
                NullText(user, "token"), Decode(cluster, "certificate-authority-data"),
                Decode(user, "client-certificate-data"), Decode(user, "client-key-data"),
                NullText(cluster, "tls-server-name"), Property(cluster, "insecure-skip-tls-verify").ValueKind == JsonValueKind.True);
            KubernetesExecPlan? exec = ReadExec(Property(user, "exec"), endpoint, sourceDirectory);
            if (exec is not null && (connection.BearerToken is not null || connection.ClientCertificatePem is not null || Text(user, "client-certificate").Length > 0 || Text(user, "tokenFile").Length > 0))
            {
                throw Invalid("A kubeconfig user cannot combine exec credentials with another authentication method.");
            }

            plans.Add(new KubernetesKubeconfigPlan(name, connection,
                connection.CertificateAuthorityPem is null ? ResolvePath(cluster, "certificate-authority", sourceDirectory) : null,
                connection.ClientCertificatePem is null ? ResolvePath(user, "client-certificate", sourceDirectory) : null,
                connection.ClientKeyPem is null ? ResolvePath(user, "client-key", sourceDirectory) : null,
                connection.BearerToken is null ? ResolvePath(user, "tokenFile", sourceDirectory) : null, exec));
        }

        return plans;
    }

    private static KubernetesExecPlan? ReadExec(JsonElement exec, Uri endpoint, string? sourceDirectory)
    {
        if (exec.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        string command = Text(exec, "command");
        string version = Text(exec, "apiVersion");
        string interactive = Text(exec, "interactiveMode");
        if (string.IsNullOrWhiteSpace(command) || command.IndexOf('\0') >= 0
            || version is not ("client.authentication.k8s.io/v1" or "client.authentication.k8s.io/v1beta1"))
        {
            throw Invalid("The kubeconfig exec credential command or API version is invalid.");
        }

        if (interactive.Length == 0 && version.EndsWith("/v1beta1", StringComparison.Ordinal))
        {
            interactive = "IfAvailable";
        }

        if (interactive is not ("Never" or "IfAvailable" or "Always"))
        {
            throw Invalid("The kubeconfig exec credential interactiveMode is invalid.");
        }

        if ((command.Contains('/', StringComparison.Ordinal) || command.Contains('\\', StringComparison.Ordinal)) && !Path.IsPathFullyQualified(command))
        {
            command = ResolveRelative(command, sourceDirectory);
        }

        var arguments = new List<string>();
        if (Property(exec, "args") is { ValueKind: JsonValueKind.Array } args)
        {
            foreach (JsonElement argument in args.EnumerateArray())
            {
                if (argument.ValueKind != JsonValueKind.String || arguments.Count >= 128 || argument.GetString()!.Length > 8192)
                {
                    throw Invalid("The kubeconfig exec arguments exceed their limits.");
                }

                arguments.Add(argument.GetString()!);
            }
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Property(exec, "env") is { ValueKind: JsonValueKind.Array } env)
        {
            foreach (JsonElement item in env.EnumerateArray())
            {
                string key = Text(item, "name");
                string value = Text(item, "value");
                if (key.Length is < 1 or > 128 || value.Length > 8192 || key.Contains('=', StringComparison.Ordinal)
                    || key.Contains('\0', StringComparison.Ordinal) || value.Contains('\0', StringComparison.Ordinal)
                    || key is "KUBERNETES_EXEC_INFO" || environment.Count >= 128 || !environment.TryAdd(key, value))
                {
                    throw Invalid("The kubeconfig exec environment is invalid.");
                }
            }
        }

        string identity = endpoint.AbsoluteUri + "\n" + command + "\n" + exec.GetRawText();
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return new KubernetesExecPlan(command, arguments, environment, version, interactive,
            Property(exec, "provideClusterInfo").ValueKind == JsonValueKind.True, fingerprint);
    }

    private static Dictionary<string, JsonElement> Entries(JsonElement root, string property)
    {
        var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (Property(root, property) is not { ValueKind: JsonValueKind.Array } array)
        {
            return entries;
        }

        foreach (JsonElement entry in array.EnumerateArray())
        {
            string name = Text(entry, "name");
            if (name.Length is < 1 or > 1024 || entries.Count >= 1024 || !entries.TryAdd(name, entry))
            {
                throw Invalid("Kubeconfig entries must have bounded, unique names.");
            }
        }

        return entries;
    }

    private static string? Decode(JsonElement element, string name)
    {
        string? value = NullText(element, name);
        try
        {
            return value is null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            throw Invalid("A kubeconfig credential contains invalid base64 data.");
        }
    }

    private static string? ResolvePath(JsonElement element, string name, string? directory) =>
        NullText(element, name) is { } value ? ResolveRelative(value, directory) : null;

    private static string ResolveRelative(string path, string? directory)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        if (directory is null || !Path.IsPathFullyQualified(directory))
        {
            throw Invalid("Relative kubeconfig paths require the source file directory.");
        }

        return Path.GetFullPath(path, directory);
    }

    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement result) ? result : default;

    private static string Text(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } result ? result.GetString()! : string.Empty;

    private static string? NullText(JsonElement value, string name) => Text(value, name) is { Length: > 0 } text ? text : null;

    private static KubernetesRequestException Invalid(string message) => new(KubernetesErrorCode.InvalidConfiguration, message);
}
