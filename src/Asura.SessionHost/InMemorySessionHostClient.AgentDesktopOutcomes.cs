using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

/// <summary>
/// Maps what the automation engine and the broker reported onto the closed
/// <see cref="AgentDesktopStableCodes"/> (ADR 0061, "Authorization and
/// dispatch"). Every message here is fixed host text: nothing read from
/// another application is ever placed in a <see cref="HostError"/> or in the
/// audit trail.
/// </summary>
public sealed partial class InMemorySessionHostClient
{
    private const string DesktopCompletedCode = "desktop_completed";
    private const string DesktopRequestedCode = "desktop_requested";
    private const string DesktopAlreadySatisfiedCode = "desktop_already_satisfied";
    private const string DesktopDeliveredUnverifiedCode = "desktop_delivered_unverified";

    // The runtime's own names for the two expiries; it renews an action whose
    // result carries either (GovernedAgentRuntime.IsRenewableApprovalExpiry).
    private const string AuthorizationExpiredCode = "authorization_expired";
    private const string ApprovalExpiredCode = "approval_expired";

    /// <summary>
    /// Accepts an engine success only when it proves what the request needs.
    /// An element action must report a verified effect, or that it delivered
    /// nothing: agent-desktop answers a successful <c>not_delivered</c> when
    /// the element is already in the requested state (a checked box to check,
    /// an expanded node to expand), so nothing reached the application.
    /// Focusing a window and closing an application can only ever claim that
    /// the request was made, and a launch is whatever the engine observed.
    /// </summary>
    private static HostResult<AgentDesktopResult> DesktopEngineSuccess(
        AgentDesktopRequest request,
        AgentDesktopResult result,
        long revision)
    {
        var proven = (request, result) switch
        {
            (AgentDesktopRequest.ListApps, AgentDesktopResult.AppList) => true,
            (AgentDesktopRequest.ListWindows, AgentDesktopResult.WindowList) => true,
            (AgentDesktopRequest.Snapshot, AgentDesktopResult.Snapshot) => true,
            (AgentDesktopRequest.Wait, AgentDesktopResult.WaitCompleted) => true,
            // Delivered input is a success whether or not the library could
            // observe its effect: a plain button press is never verifiable.
            // Only an uncertain or unknown delivery leaves the outcome open.
            (AgentDesktopRequest.ElementAction, AgentDesktopResult.ActionDelivered delivered) =>
                delivered.Delivery is AgentDesktopDelivery.DeliveredVerified
                    or AgentDesktopDelivery.DeliveredUnverified
                    or AgentDesktopDelivery.NotDelivered,
            (AgentDesktopRequest.FocusWindow, AgentDesktopResult.ActionDelivered) => true,
            (AgentDesktopRequest.CloseApp, AgentDesktopResult.AppCloseRequested) => true,
            (AgentDesktopRequest.LaunchApp, AgentDesktopResult.AppLaunched) => true,
            _ => false,
        };
        if (proven)
        {
            return HostResult<AgentDesktopResult>.Succeed(result, revision);
        }

        // The engine call returned, so input may have been delivered. Without
        // proof of the effect the only honest answer for a mutation is that
        // its outcome is unknown; an observation simply failed.
        return DesktopFailure(
            new AgentDesktopFailure(
                request.IsMutation
                    ? AgentDesktopFailureCode.OutcomeUnknown
                    : AgentDesktopFailureCode.ActionFailed,
                request.IsMutation
                    ? AgentDesktopDelivery.DeliveredUnverified
                    : AgentDesktopDelivery.NotDelivered),
            revision);
    }

    private HostResult<AgentDesktopResult> DesktopEngineFailure(
        AgentRunId runId,
        AgentDesktopFailure failure,
        long revision)
    {
        if (failure.Code == AgentDesktopFailureCode.EngineFaulted)
        {
            ReleaseFaultedAgentDesktopRun(runId);
        }

        return DesktopFailure(failure, revision);
    }

    /// <summary>
    /// A faulted adapter is poisoned (ADR 0061), so the run's desktop session
    /// ends here and is never handed another request.
    /// </summary>
    private void ReleaseFaultedAgentDesktopRun(AgentRunId runId)
    {
        try
        {
            _agentDesktopAutomationEngine?.ReleaseRun(runId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The fault is already the reported outcome; a failing release
            // must not replace it.
            _ = exception;
        }
    }

    /// <summary>
    /// The stable code, not the engine's cause, decides what the caller is
    /// told: <see cref="AgentDesktopStableCodes.For"/> folds every failure
    /// that may have delivered input into the unknown-outcome code, and the
    /// host error and its message must say the same thing.
    /// </summary>
    private static HostResult<AgentDesktopResult> DesktopFailure(
        AgentDesktopFailure failure,
        long revision)
    {
        var stableCode = AgentDesktopStableCodes.For(failure);
        return HostResult<AgentDesktopResult>.Fail(
            new HostError(
                DesktopHostErrorCode(stableCode),
                stableCode,
                DesktopFailureMessage(stableCode),
                failure.Retryable),
            revision);
    }

    private static HostErrorCode DesktopHostErrorCode(string stableCode) => stableCode switch
    {
        AgentDesktopStableCodes.Unavailable
            or AgentDesktopStableCodes.PermissionDenied => HostErrorCode.CapabilityNotSupported,
        AgentDesktopStableCodes.AppNotFound
            or AgentDesktopStableCodes.WindowNotFound => HostErrorCode.NotFound,
        AgentDesktopStableCodes.StaleRef
            or AgentDesktopStableCodes.AmbiguousTarget => HostErrorCode.RevisionConflict,
        AgentDesktopStableCodes.TimeoutBeforeDelivery => HostErrorCode.DeadlineExceeded,
        AgentDesktopStableCodes.Cancelled => HostErrorCode.Cancelled,
        AgentDesktopStableCodes.SelfTarget
            or AgentDesktopStableCodes.PolicyDenied
            or AgentDesktopStableCodes.NotSupported
            or AgentDesktopStableCodes.InvalidArguments
            or AgentDesktopStableCodes.SecureField => HostErrorCode.InvalidRequest,
        _ => HostErrorCode.EngineFailed,
    };

    private static string DesktopFailureMessage(string stableCode) => stableCode switch
    {
        AgentDesktopStableCodes.Unavailable =>
            "Desktop automation is unavailable on this machine.",
        AgentDesktopStableCodes.PermissionDenied =>
            "The operating system has not granted Asura accessibility access.",
        AgentDesktopStableCodes.SelfTarget =>
            "Desktop automation cannot observe or operate Asura itself.",
        AgentDesktopStableCodes.SelfTargetLaunched =>
            "The launch reached Asura itself; nothing further was done. Take a fresh desktop snapshot.",
        AgentDesktopStableCodes.AppNotFound =>
            "The desktop application was not found.",
        AgentDesktopStableCodes.WindowNotFound =>
            "The desktop window was not found.",
        AgentDesktopStableCodes.StaleRef =>
            "The element reference is stale; take a fresh desktop snapshot.",
        AgentDesktopStableCodes.AmbiguousTarget =>
            "The element reference no longer identifies one element; take a fresh desktop snapshot.",
        AgentDesktopStableCodes.TimeoutBeforeDelivery =>
            "The desktop action timed out before any input was delivered.",
        AgentDesktopStableCodes.PolicyDenied =>
            "The desktop automation policy refused the action.",
        AgentDesktopStableCodes.NotSupported =>
            "The element does not support the desktop action.",
        AgentDesktopStableCodes.InvalidArguments =>
            "The desktop action arguments were rejected.",
        AgentDesktopStableCodes.SecureField =>
            "Text cannot be entered into a secure field.",
        AgentDesktopStableCodes.ActionOutcomeUnknown =>
            "The desktop action may have been delivered; its outcome is unknown and it was not repeated.",
        AgentDesktopStableCodes.AdapterFaulted =>
            "The desktop automation adapter faulted and the run's desktop session was ended.",
        AgentDesktopStableCodes.Cancelled =>
            "The governed desktop action was canceled.",
        _ => "The desktop action failed.",
    };

    /// <summary>
    /// Maps a permit the broker would not grant. An expiry keeps the code the
    /// runtime renews from: it rebuilds the proposal and asks again, which an
    /// expired one-action approval needs and which nothing else may trigger.
    /// Every other refusal of the authorization itself (unknown, replayed,
    /// foreign, mismatched) is the generic non-retryable invalid request every
    /// family reports. It is never <see cref="AgentDesktopStableCodes.PolicyDenied"/>,
    /// which is the library's own retryable refusal of a request it was given.
    /// </summary>
    private static HostResult<AgentDesktopResult> MapDesktopAuthorizationFailure(
        AgentAuthorizationError error,
        long revision)
    {
        var hostError = error.Code switch
        {
            AgentAuthorizationErrorCode.AuthorizationExpired =>
                new HostError(
                    HostErrorCode.DeadlineExceeded,
                    AuthorizationExpiredCode,
                    "The one-action desktop authorization expired before dispatch."),
            AgentAuthorizationErrorCode.ApprovalExpired =>
                new HostError(
                    HostErrorCode.DeadlineExceeded,
                    ApprovalExpiredCode,
                    "The desktop approval expired before dispatch."),
            AgentAuthorizationErrorCode.Cancelled
                or AgentAuthorizationErrorCode.RunCancelled =>
                new HostError(
                    HostErrorCode.Cancelled,
                    AgentDesktopStableCodes.Cancelled,
                    "The governed desktop action was canceled."),
            AgentAuthorizationErrorCode.AuditUnavailable =>
                new HostError(
                    HostErrorCode.EngineFailed,
                    AgentDesktopStableCodes.Unavailable,
                    "The desktop action audit trail is unavailable."),
            _ => HostError.Create(
                HostErrorCode.InvalidRequest,
                "The exact one-action desktop authorization was rejected."),
        };
        return HostResult<AgentDesktopResult>.Fail(hostError, revision);
    }

    private static HostResult<AgentDesktopResult> DesktopAuthorityRefused(long revision) =>
        HostResult<AgentDesktopResult>.Fail(
            HostError.Create(
                HostErrorCode.InvalidRequest,
                "The consumed authorization does not grant this desktop action."),
            revision);

    private static HostResult<AgentDesktopResult> DesktopTargetChanged(long revision) =>
        InvalidDesktopAction(
            "The desktop run target changed before authorization.",
            revision);

    private static HostResult<AgentDesktopResult> InvalidDesktopAction(
        string message,
        long revision) =>
        HostResult<AgentDesktopResult>.Fail(
            new HostError(
                HostErrorCode.InvalidRequest,
                AgentDesktopStableCodes.InvalidArguments,
                message),
            revision);

    private static HostResult<AgentDesktopResult> DesktopBridgeUnavailable(long revision) =>
        HostResult<AgentDesktopResult>.Fail(
            new HostError(
                HostErrorCode.CapabilityNotSupported,
                AgentDesktopStableCodes.Unavailable,
                "The governed desktop automation bridge is unavailable."),
            revision);

    private static HostResult<AgentDesktopResult> CancelledDesktopAction(long revision) =>
        DesktopFailure(
            new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled),
            revision);

    /// <summary>
    /// The audit code of a success. Focus and close report that the request
    /// was made, never that the window is focused or the application gone.
    /// An element action that found its element already in the requested
    /// state is recorded as such, so the trail never shows delivered input
    /// that was not sent; one whose input was delivered but whose effect the
    /// library could not observe is recorded as unverified.
    /// </summary>
    private static string DesktopSuccessCode(AgentDesktopRequest request, AgentDesktopResult result) =>
        request switch
        {
            AgentDesktopRequest.FocusWindow or AgentDesktopRequest.CloseApp => DesktopRequestedCode,
            AgentDesktopRequest.ElementAction
                when result is AgentDesktopResult.ActionDelivered { Delivery: AgentDesktopDelivery.NotDelivered } =>
                DesktopAlreadySatisfiedCode,
            AgentDesktopRequest.ElementAction
                when result is AgentDesktopResult.ActionDelivered { Delivery: AgentDesktopDelivery.DeliveredUnverified } =>
                DesktopDeliveredUnverifiedCode,
            _ => DesktopCompletedCode,
        };

    private static int? DesktopResultCount(AgentDesktopResult result) => result switch
    {
        AgentDesktopResult.AppList apps => apps.Apps.Count,
        AgentDesktopResult.WindowList windows => windows.Windows.Count,
        AgentDesktopResult.Snapshot snapshot => snapshot.Nodes.Count,
        _ => null,
    };
}
