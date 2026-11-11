using Asura.Core;

namespace Asura.Application;

/// <summary>Whether desktop automation can run right now on this machine.</summary>
public enum AgentDesktopAccessState
{
    /// <summary>The native library is not loaded on this platform or build.</summary>
    Unavailable,

    /// <summary>The library is loaded but the operating system has not granted accessibility access.</summary>
    NotGranted,

    Granted,
}

/// <summary>
/// The platform seam SessionHost dispatches one authorized desktop request
/// through. The implementation owns the native agent-desktop library, one
/// adapter per run, the self-target guard, and the projection of untrusted
/// accessibility data into bounded results. It never sees authorization.
/// </summary>
public interface IAgentDesktopAutomationEngine
{
    /// <summary>
    /// Reports the library and operating-system grant state. The grant is
    /// re-read because the user can change it while Asura runs.
    /// </summary>
    ValueTask<AgentDesktopAccessState> GetAccessStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Answers, without touching the target, whether the request names Asura
    /// itself or a process whose identity cannot be established: a name any
    /// running Asura or uninspectable process could answer to, an observed
    /// application whose process is gone or changed identity, or a listing
    /// that cannot be read. SessionHost calls this before any approval is
    /// requested; the engine repeats the same check inside
    /// <see cref="ExecuteAsync"/>, immediately before the native call. Null
    /// means the target may be proposed.
    /// </summary>
    ValueTask<AgentDesktopFailure?> CheckTargetAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes exactly one request for the run. Mutations are never retried
    /// by the engine and are never abandoned once dispatched: cancellation
    /// after dispatch still returns the real outcome. A delivery result other
    /// than not-delivered is final.
    /// </summary>
    ValueTask<AgentDesktopExecutionResult> ExecuteAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends the run's adapter and removes the element references agent-desktop
    /// stored for it. Safe to call for a run that never used the desktop.
    /// </summary>
    void ReleaseRun(AgentRunId runId);
}
