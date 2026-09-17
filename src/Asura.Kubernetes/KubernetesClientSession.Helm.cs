using System.Globalization;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public async ValueTask<KubernetesHelmReleasePage> ListHelmReleasesAsync(KubernetesHelmListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 500 || request.Offset is < 0 or > 100000) { throw new ArgumentOutOfRangeException(nameof(request)); }
        var arguments = new List<string> { "list", "--deployed", "--failed", "--pending", "--uninstalled", "--superseded", "--uninstalling", "--max", request.Limit.ToString(CultureInfo.InvariantCulture),
            "--offset", request.Offset.ToString(CultureInfo.InvariantCulture) };
        if (request.Namespace is null) { arguments.Add("--all-namespaces"); }
        else { ValidateHelmName(request.Namespace, 63); arguments.AddRange(["--namespace", request.Namespace]); }
        using JsonDocument response = await ReadHelmAsync(arguments, cancellationToken).ConfigureAwait(false);
        return ProjectHelmList(response.RootElement, request);
    }

    public async ValueTask<KubernetesHelmHistory> ReadHelmHistoryAsync(KubernetesHelmHistoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHelmName(request.Namespace, 63);
        ValidateHelmName(request.Release, 53);
        if (request.MaximumRevisions is < 1 or > 500) { throw new ArgumentOutOfRangeException(nameof(request)); }
        string[] arguments = ["history", request.Release, "--namespace", request.Namespace, "--max", request.MaximumRevisions.ToString(CultureInfo.InvariantCulture)];
        using JsonDocument response = await ReadHelmAsync(arguments, cancellationToken).ConfigureAwait(false);
        return ProjectHelmHistory(response.RootElement, request.MaximumRevisions);
    }

    private async ValueTask<JsonDocument> ReadHelmAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_refresh is not null) { await RefreshAsync(_client, lifetime.Token).ConfigureAwait(false); }
        return await KubernetesHelmCommand.ReadAsync(_connection, arguments, lifetime.Token, _helmExecutable).ConfigureAwait(false);
    }

    internal static KubernetesHelmReleasePage ProjectHelmList(JsonElement root, KubernetesHelmListRequest request)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > request.Limit) { throw InvalidHelm(); }
        var releases = new List<KubernetesHelmRelease>();
        foreach (JsonElement item in root.EnumerateArray())
        {
            releases.Add(new(HelmText(item, "name"), HelmText(item, "namespace"), HelmRevision(item), HelmText(item, "status"),
                HelmText(item, "chart"), HelmText(item, "app_version"), HelmText(item, "updated")));
        }

        return new(releases, releases.Count == request.Limit ? request.Offset + request.Limit : null);
    }

    internal static KubernetesHelmHistory ProjectHelmHistory(JsonElement root, int maximum)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > maximum) { throw InvalidHelm(); }
        var revisions = new List<KubernetesHelmRevision>();
        foreach (JsonElement item in root.EnumerateArray())
        {
            revisions.Add(new(HelmRevision(item), HelmText(item, "status"), HelmText(item, "chart"),
                HelmText(item, "app_version"), HelmText(item, "updated")));
        }

        return new(revisions);
    }

    private static int HelmRevision(JsonElement item)
    {
        JsonElement value = Property(item, "revision");
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number > 0) { return number; }
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0) { return number; }
        throw InvalidHelm();
    }

    private static string HelmText(JsonElement item, string key)
    {
        string value = Text(item, key);
        if (value.Length > 1024 || value.Any(char.IsControl)) { throw InvalidHelm(); }
        return value;
    }

    private static void ValidateHelmName(string value, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum || !char.IsAsciiLetterOrDigit(value[0])
            || !char.IsAsciiLetterOrDigit(value[^1]) || value.Any(static character => !(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.')))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A Helm namespace or release name is invalid.");
        }
    }

    private static KubernetesRequestException InvalidHelm() => new(KubernetesErrorCode.InvalidResponse, "Helm returned invalid release metadata.");
}
