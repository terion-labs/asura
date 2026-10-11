using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>What the Kubernetes panel was doing when a request failed.</summary>
internal enum KubernetesActivity
{
    /// <summary>Listing, inspecting, reading logs, or changing a resource.</summary>
    Request,

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
            KubernetesActivity.Helm when exception.Code == KubernetesErrorCode.Unsupported =>
                "Helm isn’t available where this workspace runs commands. Install Helm there, then refresh.",
            KubernetesActivity.Helm when exception.Code == KubernetesErrorCode.Forbidden =>
                "You don’t have permission to read Helm releases in this namespace. Choose another namespace, or review your Kubernetes permissions.",
            KubernetesActivity.Metrics or KubernetesActivity.MetricHistory
                when exception.Code is KubernetesErrorCode.Unsupported or KubernetesErrorCode.NotFound =>
                "This cluster doesn’t provide resource usage. Install a metrics server on the cluster, or choose a metrics service, then refresh.",
            KubernetesActivity.Metrics or KubernetesActivity.MetricHistory
                when exception.Code == KubernetesErrorCode.Forbidden =>
                "You don’t have permission to read resource usage. Resource browsing still works.",
            KubernetesActivity.PortForward => ForwardText(exception.Code),
            _ => RequestText(exception.Code),
        };
    }

    private static string RequestText(KubernetesErrorCode code) => code switch
    {
        KubernetesErrorCode.InvalidConfiguration =>
            "Asura can’t connect with this connection’s Kubernetes settings. Edit the connection to check its context, credentials, and server, then refresh.",
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
        _ => "kubernetes.request.failed",
    };
}
