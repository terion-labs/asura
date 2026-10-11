using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>What the Kubernetes panel was doing when a request failed.</summary>
internal enum KubernetesActivity
{
    /// <summary>Listing, inspecting, watching, or reading logs.</summary>
    Request,

    /// <summary>Opening the session with the connection's settings.</summary>
    Connect,

    /// <summary>Reviewing or applying a manifest the person edited.</summary>
    ManifestEdit,

    /// <summary>Reviewing or confirming a scale, restart, or delete.</summary>
    ResourceChange,

    /// <summary>Reviewing or confirming a Helm upgrade, rollback, or uninstall.</summary>
    HelmChange,

    /// <summary>Reviewing or confirming a cordon, uncordon, or drain.</summary>
    NodeMaintenance,

    /// <summary>Browsing Helm releases or a release's history.</summary>
    Helm,

    /// <summary>Reading current CPU and memory usage.</summary>
    Metrics,

    /// <summary>Reading a pod's usage history from a metrics service.</summary>
    MetricHistory,

    /// <summary>Keeping a port forward open.</summary>
    PortForward,

    /// <summary>Reading a kubeconfig file in the connection editor.</summary>
    ConfigurationReview,
}

/// <summary>
/// A Kubernetes request failure as a person reads it: what happened and what
/// to do next, chosen by the failure's code.
///
/// The panel used to show the request exception's own message, which is
/// written by the client layer for diagnostics: it names HTTP status codes
/// ("The Kubernetes API returned HTTP 503."), raw error codes ("Kubernetes
/// request failed: Forbidden."), and internal limits ("The mutation body is
/// not valid JSON."). The code says enough to choose a sentence; the message
/// goes to secret-safe diagnostics with the code.
///
/// <see cref="KubernetesErrorCode.InvalidConfiguration"/> covers both the
/// connection's settings and what the person entered (a namespace, a
/// manifest, Helm values), so the activity decides which one the sentence
/// points at.
/// </summary>
internal static class KubernetesErrorText
{
    public static string Describe(
        KubernetesRequestException exception,
        KubernetesActivity activity = KubernetesActivity.Request)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SecretSafeDiagnostics.WriteTrace(DiagnosticCode(activity), exception);
        return activity switch
        {
            KubernetesActivity.ConfigurationReview => ReviewText(exception.Code),
            KubernetesActivity.PortForward => ForwardText(exception.Code),
            _ when exception.Code == KubernetesErrorCode.InvalidConfiguration => InputText(activity),
            KubernetesActivity.Helm or KubernetesActivity.HelmChange when exception.Code == KubernetesErrorCode.Unsupported =>
                "Helm isn’t available where this workspace runs commands. Install Helm there, then refresh.",
            KubernetesActivity.Helm when exception.Code == KubernetesErrorCode.Forbidden =>
                "You don’t have permission to read Helm releases in this namespace. Choose another namespace, or review your Kubernetes permissions.",
            KubernetesActivity.Metrics or KubernetesActivity.MetricHistory
                when exception.Code is KubernetesErrorCode.Unsupported or KubernetesErrorCode.NotFound =>
                "This cluster doesn’t provide resource usage. Install a metrics server on the cluster, or choose a metrics service, then refresh.",
            KubernetesActivity.Metrics or KubernetesActivity.MetricHistory
                when exception.Code == KubernetesErrorCode.Forbidden =>
                "You don’t have permission to read resource usage. Resource browsing still works.",
            _ => RequestText(exception.Code),
        };
    }

    /// <summary>What to check when the request itself was refused before it was sent.</summary>
    private static string InputText(KubernetesActivity activity) => activity switch
    {
        KubernetesActivity.Connect =>
            "Asura can’t connect with this connection’s Kubernetes settings. Edit the connection to check its context, credentials, and server, then refresh.",
        KubernetesActivity.ManifestEdit =>
            "Asura can’t apply this manifest. Check that it’s one YAML or JSON document for the selected resource, with the same kind, name, and API version. Quote values such as 0123 or yes that YAML could read as a number or true/false, then review it again.",
        KubernetesActivity.ResourceChange =>
            "Asura can’t prepare this change for the selected resource. Refresh, select the resource again, then try again.",
        KubernetesActivity.HelmChange =>
            "Asura can’t prepare this Helm change. For an upgrade, enter an OCI chart reference pinned with @sha256 and its full digest, an exact chart version, and values as one YAML or JSON mapping. Then review the change again.",
        KubernetesActivity.NodeMaintenance =>
            "Asura can’t prepare this node change. Refresh, select the node again, then review the change again.",
        KubernetesActivity.Helm =>
            "Asura can’t read this Helm release. Check the namespace you entered, then refresh.",
        KubernetesActivity.Metrics or KubernetesActivity.MetricHistory =>
            "Asura can’t read usage from the selected metrics service. Choose another metrics service, then refresh.",
        _ =>
            "Asura can’t send this request. Check the namespace and label selector you entered, then refresh.",
    };

    /// <summary>
    /// True when the failure is about what the person entered for a change,
    /// not about the table or the connection, so the table's status stays.
    /// </summary>
    public static bool IsRefusedChange(KubernetesRequestException exception, KubernetesActivity activity) =>
        exception.Code == KubernetesErrorCode.InvalidConfiguration
        && activity is KubernetesActivity.ManifestEdit or KubernetesActivity.ResourceChange
            or KubernetesActivity.HelmChange or KubernetesActivity.NodeMaintenance;

    private static string RequestText(KubernetesErrorCode code) => code switch
    {
        KubernetesErrorCode.Unauthorized =>
            "The cluster didn’t accept the connection’s credentials. Sign in again with the cluster’s command-line tool where this workspace runs commands, then refresh.",
        KubernetesErrorCode.Forbidden =>
            "Access denied for this scope. Enter an allowed namespace and refresh, or review your Kubernetes permissions.",
        KubernetesErrorCode.NotFound =>
            "The resource no longer exists on the cluster. Refresh to see what’s there now.",
        KubernetesErrorCode.Conflict =>
            "The resource changed on the cluster after you loaded it. Refresh, review it again, then try again.",
        KubernetesErrorCode.ResourceExpired =>
            "The list is out of date. Refresh to load it again.",
        KubernetesErrorCode.TooManyRequests =>
            "The cluster is busy and asked Asura to slow down. Wait a moment, then refresh.",
        KubernetesErrorCode.ServerUnavailable =>
            "The cluster’s API server didn’t respond. Check that the cluster is running, then refresh.",
        KubernetesErrorCode.InvalidResponse =>
            "The cluster sent a response Asura couldn’t read. Refresh to try again.",
        KubernetesErrorCode.ResponseTooLarge =>
            "The cluster’s response is too large to show. Narrow it with a namespace or label selector, then refresh.",
        KubernetesErrorCode.TargetChanged =>
            "The resource was replaced on the cluster. Select it again before you continue.",
        KubernetesErrorCode.ConnectionFailed =>
            "Asura couldn’t reach the cluster. Check the workspace’s network connection and the cluster’s address, then refresh.",
        KubernetesErrorCode.Unsupported =>
            "This cluster or connection doesn’t support that action.",
        _ => "The cluster request didn’t finish. Refresh to try again.",
    };

    private static string ForwardText(KubernetesErrorCode code) => code switch
    {
        KubernetesErrorCode.ConnectionFailed or KubernetesErrorCode.ServerUnavailable =>
            "The forward stopped because the connection to the cluster was lost. Start it again.",
        KubernetesErrorCode.Forbidden =>
            "The forward stopped because the cluster denied access to the pod. Check your permissions, then start it again.",
        KubernetesErrorCode.NotFound or KubernetesErrorCode.TargetChanged =>
            "The forward stopped because its pod was replaced or removed. Start a new forward.",
        _ => "The forward stopped. Start it again.",
    };

    private static string ReviewText(KubernetesErrorCode code) => code switch
    {
        KubernetesErrorCode.InvalidConfiguration =>
            "Asura couldn’t read that kubeconfig file. Check that the path points to a readable kubeconfig file, then review it again.",
        KubernetesErrorCode.Unsupported =>
            "Reviewing a kubeconfig file isn’t available where this workspace runs commands.",
        _ => "Asura couldn’t review that kubeconfig file. Try again.",
    };

    private static string DiagnosticCode(KubernetesActivity activity) => activity switch
    {
        KubernetesActivity.Helm => "kubernetes.helm.failed",
        KubernetesActivity.Metrics => "kubernetes.metrics.failed",
        KubernetesActivity.MetricHistory => "kubernetes.metric-history.failed",
        KubernetesActivity.PortForward => "kubernetes.forward.failed",
        KubernetesActivity.ConfigurationReview => "kubernetes.kubeconfig.review.failed",
        KubernetesActivity.Connect => "kubernetes.connect.failed",
        KubernetesActivity.ManifestEdit => "kubernetes.manifest.failed",
        KubernetesActivity.ResourceChange => "kubernetes.change.failed",
        KubernetesActivity.HelmChange => "kubernetes.helm-change.failed",
        KubernetesActivity.NodeMaintenance => "kubernetes.node.failed",
        _ => "kubernetes.request.failed",
    };
}
