using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// A session host that only answers the desktop access question, with the
/// answer a test sets. The members that would act on another app are never
/// reached from Settings.
/// </summary>
internal sealed class AgentDesktopAccessTestHost : IAgentDesktopSessionHost
{
    public AgentDesktopAccessState State { get; set; } = AgentDesktopAccessState.NotGranted;

    /// <summary>When set, the next reading waits for it and answers what it completes with.</summary>
    public TaskCompletionSource<AgentDesktopAccessState>? Pending { get; set; }

    public Exception? Failure { get; set; }

    public int ReadCount { get; private set; }

    public ValueTask<AgentDesktopAccessState> GetAgentDesktopAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        if (Failure is { } failure)
        {
            throw failure;
        }

        if (Pending is { } pending)
        {
            Pending = null;
            return new ValueTask<AgentDesktopAccessState>(pending.Task);
        }

        return ValueTask.FromResult(State);
    }

    public ValueTask<AgentDesktopFailure?> CheckAgentDesktopTargetAsync(
        AgentDesktopAction action,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<HostResult<AgentDesktopResult>> RunAgentDesktopActionAsync(
        AgentAuthorizationId authorizationId,
        AgentDesktopAction action,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public void ReleaseAgentDesktopRun(AgentRunId runId) => throw new NotSupportedException();
}

internal sealed class AgentDesktopAccessTestDispatcher : IUiThreadDispatcher
{
    public static AgentDesktopAccessTestDispatcher Instance { get; } = new();

    public Task InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
