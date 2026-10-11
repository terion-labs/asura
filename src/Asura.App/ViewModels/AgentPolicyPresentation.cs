using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// The agent's policy vocabulary in the words of the person reading it. The
/// policy engine's enum names ("Cancelled", "HumanApproval",
/// "TargetOutsideRunScope") are identifiers; the agent panel shows these
/// instead and keeps the identifiers for Details.
/// </summary>
internal static class AgentPolicyPresentation
{
    public static string CapabilityName(AgentCapability capability) =>
        capability switch
        {
            AgentCapability.TerminalRead => "Terminal read",
            AgentCapability.RunCommands => "Run commands",
            AgentCapability.EditFiles => "Edit files",
            AgentCapability.ReadFiles => "Read files",
            AgentCapability.Search => "Search",
            AgentCapability.GitData => "Git data",
            AgentCapability.Git => "Git",
            AgentCapability.WebFetch => "Web search and fetch",
            AgentCapability.Docker => "Docker",
            AgentCapability.DestructiveTerminalActions => "Destructive terminal actions",
            AgentCapability.BrowserNavigation => "Browser navigation",
            AgentCapability.BrowserData => "Browser data",
            AgentCapability.ProcessControl => "Process control",
            AgentCapability.McpTools => "MCP tools",
            AgentCapability.SecretUse => "Secret use",
            AgentCapability.BrowserInteraction => "Browser interaction",
            AgentCapability.BrowserScripting => "Browser scripting",
            AgentCapability.BrowserDiagnostics => "Browser diagnostics",
            AgentCapability.DatabaseRead => "Database read",
            AgentCapability.DatabaseWrite => "Database write",
            AgentCapability.DockerData => "Docker data",
            AgentCapability.KubernetesData => "Kubernetes data",
            AgentCapability.KubernetesExec => "Kubernetes pod commands",
            AgentCapability.KubernetesControl => "Kubernetes changes",
            AgentCapability.SystemData => "System data",
            AgentCapability.ProcessData => "Process data",
            AgentCapability.ArtifactTransfer => "Artifact transfer",
            AgentCapability.WorkspaceLayout => "Workspace layout",
            _ => throw new ArgumentOutOfRangeException(nameof(capability)),
        };

    public static string PermissionName(AgentPermission permission) =>
        permission switch
        {
            AgentPermission.Off => "Off",
            AgentPermission.Ask => "Ask",
            AgentPermission.Auto => "Auto",
            AgentPermission.Yolo => "Full access",
            _ => throw new ArgumentOutOfRangeException(nameof(permission)),
        };

    /// <summary>One step of a recorded action, as its timeline reads.</summary>
    public static string OutcomeName(AuditOutcome outcome) =>
        outcome switch
        {
            AuditOutcome.Requested => "Requested",
            AuditOutcome.Approved => "Approved",
            AuditOutcome.Started => "Started",
            AuditOutcome.Succeeded => "Succeeded",
            AuditOutcome.Denied => "Denied",
            AuditOutcome.Failed => "Failed",
            AuditOutcome.Cancelled => "Canceled",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

    /// <summary>Who let a recorded action run.</summary>
    public static string AuthorizationSourceName(AgentAuthorizationSource source) =>
        source switch
        {
            AgentAuthorizationSource.AutoPolicy => "Allowed automatically",
            AgentAuthorizationSource.YoloPolicy => "Allowed by full access",
            AgentAuthorizationSource.HumanApproval => "Approved by you",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    /// <summary>
    /// Why a recorded action didn't run or didn't finish, as a fragment that
    /// follows "Not run ·" in its result line.
    /// </summary>
    public static string ErrorName(AgentAuthorizationErrorCode code) =>
        code switch
        {
            AgentAuthorizationErrorCode.InvalidRequest => "The request couldn't be read",
            AgentAuthorizationErrorCode.RunNotFound => "The conversation had ended",
            AgentAuthorizationErrorCode.RunAlreadyRegistered => "The conversation was already running",
            AgentAuthorizationErrorCode.RunCancelled => "The conversation was stopped",
            AgentAuthorizationErrorCode.RunSuspended => "The conversation was paused",
            AgentAuthorizationErrorCode.RunActorMismatch => "The request came from another conversation",
            AgentAuthorizationErrorCode.TargetOutsideRunScope => "It was outside what the agent can use",
            AgentAuthorizationErrorCode.YoloConfirmationRequired => "Full access wasn't confirmed",
            AgentAuthorizationErrorCode.CapacityExceeded => "Too many actions were waiting",
            AgentAuthorizationErrorCode.DuplicateAction => "The same action was already requested",
            AgentAuthorizationErrorCode.UnknownTool => "The agent asked for a tool Asura doesn't have",
            AgentAuthorizationErrorCode.PolicyDenied => "The agent's permissions don't allow it",
            AgentAuthorizationErrorCode.ApprovalDenied => "You denied it",
            AgentAuthorizationErrorCode.ApprovalActorMismatch => "It was approved from another window",
            AgentAuthorizationErrorCode.ApprovalNotFound => "The approval was no longer waiting",
            AgentAuthorizationErrorCode.ApprovalExpired => "The approval expired",
            AgentAuthorizationErrorCode.AuthorizationNotFound => "Its permission was no longer available",
            AgentAuthorizationErrorCode.AuthorizationExpired => "Its permission expired",
            AgentAuthorizationErrorCode.AuthorizationMismatch => "The action changed after it was allowed",
            AgentAuthorizationErrorCode.PolicyChanged => "The agent's permissions changed before it ran",
            AgentAuthorizationErrorCode.AuditUnavailable => "Asura couldn't record it, so it didn't run",
            AgentAuthorizationErrorCode.AlreadyCompleted => "It had already finished",
            AgentAuthorizationErrorCode.Cancelled => "It was canceled",
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };

    /// <summary>A change to a run's permissions, as its recorded title.</summary>
    public static string PolicyTransitionName(AgentRunPolicyTransition transition) =>
        transition switch
        {
            AgentRunPolicyTransition.YoloEnabled => "Full access turned on",
            AgentRunPolicyTransition.YoloDisabled => "Full access turned off",
            AgentRunPolicyTransition.YoloExpired => "Full access ended",
            AgentRunPolicyTransition.Updated => "Permissions changed",
            _ => throw new ArgumentOutOfRangeException(nameof(transition)),
        };

    /// <summary>
    /// What an action can do, in the words of the person deciding on it. The
    /// policy engine's own risk names ("Mutation", "Observation") describe its
    /// classification, not the consequence someone is approving.
    /// </summary>
    public static string RiskName(AgentActionRisk risk) =>
        risk switch
        {
            AgentActionRisk.Observation => "Read only",
            AgentActionRisk.Routine => "Routine",
            AgentActionRisk.Mutation => "Makes changes",
            AgentActionRisk.Destructive => "Can delete data",
            AgentActionRisk.Privileged => "Needs elevated access",
            _ => throw new ArgumentOutOfRangeException(nameof(risk)),
        };

    public static string CapabilityDescription(AgentCapability capability) =>
        capability switch
        {
            AgentCapability.TerminalRead => "Read visible terminal output.",
            AgentCapability.RunCommands => "Send commands to terminal sessions.",
            AgentCapability.EditFiles => "Create, modify, and delete files.",
            AgentCapability.ReadFiles => "Read files and list folders.",
            AgentCapability.Search => "Search file contents and paths.",
            AgentCapability.GitData => "Inspect Git repositories without changing them.",
            AgentCapability.Git => "Change Git repositories.",
            AgentCapability.WebFetch => "Search and fetch internet resources.",
            AgentCapability.Docker => "Manage Docker workload lifecycle.",
            AgentCapability.DestructiveTerminalActions =>
                "Run terminal actions classified as destructive.",
            AgentCapability.BrowserNavigation => "Navigate browser panels.",
            AgentCapability.BrowserData => "Read browser page state.",
            AgentCapability.BrowserInteraction => "Interact with page controls.",
            AgentCapability.ProcessControl =>
                "Control local processes when mutation tools are available.",
            AgentCapability.McpTools => "Use tools exposed by configured MCP servers.",
            AgentCapability.SecretUse => "Resolve approved secret references for tools.",
            AgentCapability.BrowserScripting =>
                "Evaluate JavaScript in an exact browser document.",
            AgentCapability.BrowserDiagnostics =>
                "Use browser console, network, and approved DevTools diagnostics.",
            AgentCapability.DatabaseRead =>
                "Read bounded relational database and Redis data.",
            AgentCapability.DatabaseWrite =>
                "Modify relational database and Redis data.",
            AgentCapability.KubernetesControl => "Dry run and commit reviewed resource changes.",
            AgentCapability.KubernetesExec => "Send input to explicitly opened pod terminals.",
            AgentCapability.KubernetesData => "Read Kubernetes resources and bounded logs without changing the cluster.",
            AgentCapability.DockerData =>
                "Inspect Docker workloads without lifecycle control.",
            AgentCapability.SystemData =>
                "Read aggregate local system statistics.",
            AgentCapability.ProcessData =>
                "Read bounded local process information.",
            AgentCapability.ArtifactTransfer =>
                "Transfer approved browser and cross-panel artifacts.",
            AgentCapability.WorkspaceLayout =>
                "Create, split, and close tabs and panels in this workspace.",
            _ => throw new ArgumentOutOfRangeException(nameof(capability)),
        };
}
