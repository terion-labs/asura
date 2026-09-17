using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes;

public sealed partial class KubernetesClientSession
{
    public async ValueTask<KubernetesHelmChangeReview> ReviewHelmChangeAsync(KubernetesHelmChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? values = ValidateHelmChange(request);
        KubernetesResourceReference storage = await RequireHelmRevisionAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Kind == KubernetesHelmChangeKind.Upgrade)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (_refresh is not null) { await RefreshAsync(_client, lifetime.Token).ConfigureAwait(false); }
            string chart = await KubernetesHelmCommand.ReadChartAsync(_connection, request.PinnedChartReference!, request.ChartVersion!, lifetime.Token, _helmExecutable).ConfigureAwait(false);
            IReadOnlyList<string> documents = KubernetesYaml.ToJsonDocuments(chart);
            if (documents.Count != 1) { throw InvalidHelm(); }
            using JsonDocument metadata = JsonDocument.Parse(documents[0]);
            if (!string.Equals(Text(metadata.RootElement, "version"), request.ChartVersion, StringComparison.Ordinal))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The pinned chart artifact does not have the reviewed chart version.");
            }
        }

        string token = Guid.NewGuid().ToString("N");
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(5);
        RememberReview(token, new(expires, Helm: request with { ValuesYaml = null }, ValuesJson: values, HelmStorage: storage));
        return new(token, request.Kind, request.Namespace, request.Release, request.ExpectedRevision, request.PinnedChartReference,
            request.ChartVersion, values is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(values))),
            request.RollbackRevision, request.AllowHooks, request.ReuseValues, request.TimeoutSeconds, expires);
    }

    public async ValueTask<KubernetesHelmChangeResult> ExecuteHelmChangeAsync(string reviewToken, CancellationToken cancellationToken)
    {
        PendingReview review = ConsumeReview(reviewToken);
        KubernetesHelmChangeRequest request = review.Helm
            ?? throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The review is not a Helm change review.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        KubernetesResourceReference storage = await RequireHelmRevisionAsync(request, lifetime.Token).ConfigureAwait(false);
        if (storage != review.HelmStorage)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The Helm release storage changed after review. Review the operation again.");
        }
        if (_refresh is not null) { await RefreshAsync(_client, lifetime.Token).ConfigureAwait(false); }
        lifetime.Token.ThrowIfCancellationRequested();
        return await KubernetesHelmCommand.ChangeAsync(_connection, HelmChangeArguments(request), review.ValuesJson,
            request.TimeoutSeconds + 15, lifetime.Token, _helmExecutable).ConfigureAwait(false);
    }

    private async ValueTask<KubernetesResourceReference> RequireHelmRevisionAsync(KubernetesHelmChangeRequest request, CancellationToken cancellationToken)
    {
        // Helm has no server-side resourceVersion precondition. Recheck the exact
        // release immediately before dispatch; the review exposes the remaining race.
        string escaped = request.Release.Replace(".", "\\.", StringComparison.Ordinal);
        using JsonDocument response = await ReadHelmAsync(["list", "--deployed", "--failed", "--pending", "--uninstalled", "--superseded", "--uninstalling", "--namespace", request.Namespace,
            "--filter", "^" + escaped + "$", "--max", "2"], cancellationToken).ConfigureAwait(false);
        KubernetesHelmReleasePage page = ProjectHelmList(response.RootElement, new(request.Namespace, 2));
        if (page.Releases.Count != 1 || page.Releases[0].Revision != request.ExpectedRevision
            || !string.Equals(page.Releases[0].Name, request.Release, StringComparison.Ordinal)
            || !string.Equals(page.Releases[0].Namespace, request.Namespace, StringComparison.Ordinal))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The Helm release changed after selection. Reload and review the change again.");
        }

        if (page.Releases[0].Status.StartsWith("pending-", StringComparison.Ordinal))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.Conflict, "The Helm release already has a pending operation.");
        }

        if (request.Kind == KubernetesHelmChangeKind.Rollback)
        {
            KubernetesHelmHistory history = await ReadHelmHistoryAsync(new(request.Namespace, request.Release, 500), cancellationToken).ConfigureAwait(false);
            if (!history.Revisions.Any(revision => revision.Revision == request.RollbackRevision))
            {
                throw new KubernetesRequestException(KubernetesErrorCode.NotFound, "The selected Helm rollback revision is unavailable.");
            }
        }

        var identity = new KubernetesResourceReference("", "v1", "secrets", request.Namespace,
            "sh.helm.release.v1." + request.Release + ".v" + request.ExpectedRevision.ToString(CultureInfo.InvariantCulture), "", "");
        KubernetesResourceDocument secret = await InspectAsync(identity, cancellationToken).ConfigureAwait(false);
        if (secret.Reference.Uid.Length == 0 || secret.Reference.ResourceVersion.Length == 0) { throw InvalidHelm(); }
        return secret.Reference;
    }

    internal static string? ValidateHelmChange(KubernetesHelmChangeRequest request)
    {
        ValidateHelmName(request.Namespace, 63);
        ValidateHelmName(request.Release, 53);
        if (!Enum.IsDefined(request.Kind) || request.ExpectedRevision < 1 || request.TimeoutSeconds is < 5 or > 900)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The Helm operation requires a loaded release revision and a timeout of 5–900 seconds.");
        }

        if (request.Kind == KubernetesHelmChangeKind.Rollback && (request.RollbackRevision is null or < 1 || request.RollbackRevision >= request.ExpectedRevision))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Rollback requires an explicit older release revision.");
        }

        if (request.Kind != KubernetesHelmChangeKind.Upgrade)
        {
            if (request.ValuesYaml is not null || request.PinnedChartReference is not null || request.ChartVersion is not null)
            {
                throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Only upgrades accept chart and values inputs.");
            }

            return null;
        }

        string chart = request.PinnedChartReference ?? "";
        int digest = chart.LastIndexOf("@sha256:", StringComparison.Ordinal);
        if (chart.Length > 2048 || !Uri.TryCreate(chart, UriKind.Absolute, out Uri? uri) || uri.Scheme is not "oci"
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || digest < 0 || chart.Length - digest != 72 || chart[(digest + 8)..].Any(static character => !char.IsAsciiHexDigit(character)))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Upgrade requires an OCI chart reference pinned with @sha256 and its complete 64-character digest.");
        }

        string version = request.ChartVersion ?? "";
        string[] numericVersion = version.Split('-', '+')[0].Split('.');
        if (version.Length is 0 or > 128 || version.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+'))
            || numericVersion.Length != 3 || numericVersion.Any(static part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Upgrade requires an exact chart version.");
        }

        if (request.ValuesYaml is null) { return null; }
        if (request.ValuesYaml.Length > 1024 * 1024) { throw new KubernetesRequestException(KubernetesErrorCode.ResponseTooLarge, "Helm values exceed the one MiB character limit."); }
        IReadOnlyList<string> documents = KubernetesYaml.ToJsonDocuments(request.ValuesYaml);
        if (documents.Count != 1) { throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Helm values must be one YAML or JSON mapping."); }
        using JsonDocument values = JsonDocument.Parse(documents[0]);
        if (values.RootElement.ValueKind != JsonValueKind.Object) { throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "Helm values must be a mapping."); }
        return documents[0];
    }

    internal static IReadOnlyList<string> HelmChangeArguments(KubernetesHelmChangeRequest request)
    {
        var arguments = request.Kind switch
        {
            KubernetesHelmChangeKind.Upgrade => new List<string> { "upgrade", request.Release, request.PinnedChartReference!,
                "--version", request.ChartVersion!, request.ReuseValues ? "--reuse-values" : "--reset-values" },
            KubernetesHelmChangeKind.Rollback => ["rollback", request.Release, request.RollbackRevision!.Value.ToString(CultureInfo.InvariantCulture)],
            KubernetesHelmChangeKind.Uninstall => ["uninstall", request.Release],
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        arguments.AddRange(["--namespace", request.Namespace, "--wait", "--timeout", request.TimeoutSeconds.ToString(CultureInfo.InvariantCulture) + "s"]);
        if (!request.AllowHooks) { arguments.Add("--no-hooks"); }
        return arguments;
    }
}
