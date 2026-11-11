using Asura.Core;

namespace Asura.Application;

/// <summary>
/// SessionHost's desktop automation surface for the agent runtime. Every
/// member is governed by the host: the runtime never reaches the automation
/// engine directly (ADR 0061).
/// </summary>
public interface IAgentDesktopSessionHost
{
    /// <summary>
    /// Whether the desktop family can be advertised right now. Anything other
    /// than <see cref="AgentDesktopAccessState.Granted"/> means the tools are
    /// withheld from the model.
    /// </summary>
    ValueTask<AgentDesktopAccessState> GetAgentDesktopAccessAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The self-operation guard, run before any approval is requested. A
    /// non-null failure means the action names Asura itself or a process whose
    /// identity cannot be established: the host has already audited the
    /// refusal as <c>denied</c> and the runtime must not request authority
    /// for it. The host repeats the guard after permit consumption, so
    /// skipping this call can never dispatch a refused action. The check may
    /// run native code (an application listing), so the runtime skips it when
    /// the run's policy turns the tool's capability off.
    /// </summary>
    ValueTask<AgentDesktopFailure?> CheckAgentDesktopTargetAsync(
        AgentDesktopAction action,
        CancellationToken cancellationToken);

    /// <summary>
    /// Consumes one permit and dispatches the action exactly once. A
    /// failure's <see cref="HostError.StableCode"/> is one of:
    /// <list type="bullet">
    /// <item>an <see cref="AgentDesktopStableCodes"/> code: what the engine,
    /// the repeated self-operation guard or a cancellation reported, mapped by
    /// <see cref="AgentDesktopStableCodes.For"/>;
    /// <see cref="AgentDesktopStableCodes.Unavailable"/> also when the family
    /// is not composed, the bridge failed before a permit was consumed, or
    /// the broker's audit trail is unavailable; and
    /// <see cref="AgentDesktopStableCodes.InvalidArguments"/> when the run's
    /// target changed or the prepared action no longer matches its typed
    /// request;</item>
    /// <item><c>authorization_expired</c> or <c>approval_expired</c>, with
    /// <see cref="HostErrorCode.DeadlineExceeded"/>, when the broker refused
    /// an expired permit; the runtime renews the approval from them;</item>
    /// <item><c>invalid_request</c> when the broker rejected the
    /// authorization for any other reason, or a consumed permit does not
    /// grant this desktop action;</item>
    /// <item><see cref="AgentActionFailureCodes.CompletionAuditUnavailable"/>
    /// when the outcome could not be recorded in the audit trail; it replaces
    /// whatever result the dispatch had.</item>
    /// </list>
    /// </summary>
    ValueTask<HostResult<AgentDesktopResult>> RunAgentDesktopActionAsync(
        AgentAuthorizationId authorizationId,
        AgentDesktopAction action,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends the run's desktop session and invalidates every element reference
    /// it produced. Called on run Clear, Stop and cancellation; safe for a run
    /// that never used the desktop.
    /// </summary>
    void ReleaseAgentDesktopRun(AgentRunId runId);
}
