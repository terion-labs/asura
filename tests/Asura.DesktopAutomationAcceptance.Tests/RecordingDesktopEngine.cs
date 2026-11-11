using Asura.Application;
using Asura.Core;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// A pass-through around the real native engine that only writes down what
/// crossed it. Every call reaches the real engine unchanged and every answer
/// comes back unchanged; the record is how the evidence counts dispatch
/// attempts without putting a fake between SessionHost and the library.
/// </summary>
internal sealed class RecordingDesktopEngine(IAgentDesktopAutomationEngine inner) : IAgentDesktopAutomationEngine
{
    private readonly object _gate = new();
    private readonly List<EngineCall> _calls = [];

    public IReadOnlyList<EngineCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>The <c>ExecuteAsync</c> calls: the only way a request reaches a native action entrypoint.</summary>
    public IReadOnlyList<EngineCall> Executions => [.. Calls.Where(call => call.Kind == EngineCallKind.Execute)];

    public IReadOnlyList<EngineCall> Checks => [.. Calls.Where(call => call.Kind == EngineCallKind.Check)];

    public IReadOnlyList<AgentRunId> Releases =>
        [.. Calls.Where(call => call.Kind == EngineCallKind.Release).Select(call => call.RunId)];

    public ValueTask<AgentDesktopAccessState> GetAccessStateAsync(CancellationToken cancellationToken) =>
        inner.GetAccessStateAsync(cancellationToken);

    public async ValueTask<AgentDesktopFailure?> CheckTargetAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken)
    {
        var refusal = await inner.CheckTargetAsync(runId, request, cancellationToken);
        Record(new EngineCall(EngineCallKind.Check, runId, request.ToolName, refusal?.Code, Succeeded: refusal is null));
        return refusal;
    }

    public async ValueTask<AgentDesktopExecutionResult> ExecuteAsync(
        AgentRunId runId,
        AgentDesktopRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inner.ExecuteAsync(runId, request, cancellationToken);
        Record(new EngineCall(
            EngineCallKind.Execute,
            runId,
            request.ToolName,
            (result as AgentDesktopExecutionResult.Failed)?.Failure.Code,
            Succeeded: result is AgentDesktopExecutionResult.Succeeded));
        return result;
    }

    public void ReleaseRun(AgentRunId runId)
    {
        inner.ReleaseRun(runId);
        Record(new EngineCall(EngineCallKind.Release, runId, ToolName: null, FailureCode: null, Succeeded: true));
    }

    private void Record(EngineCall call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}

internal enum EngineCallKind
{
    Check,
    Execute,
    Release,
}

internal sealed record EngineCall(
    EngineCallKind Kind,
    AgentRunId RunId,
    string? ToolName,
    AgentDesktopFailureCode? FailureCode,
    bool Succeeded);

/// <summary>
/// The real session host with one answer replaced: it reports the desktop
/// family as granted for the first <paramref name="grantedAnswers"/> access
/// queries and forwards the real answer afterwards. On a host without the
/// Accessibility grant the runtime would otherwise refuse every call before
/// authority, so this is the only way to show the runtime, broker and host
/// paths up to the library's own permission check. Every other member is the
/// real host's. It is never used where the grant exists.
/// </summary>
internal sealed class GrantReportingDesktopHost(IAgentDesktopSessionHost inner, int grantedAnswers = int.MaxValue)
    : IAgentDesktopSessionHost
{
    private int _answers;

    public ValueTask<AgentDesktopAccessState> GetAgentDesktopAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Interlocked.Increment(ref _answers) <= grantedAnswers
            ? ValueTask.FromResult(AgentDesktopAccessState.Granted)
            : inner.GetAgentDesktopAccessAsync(cancellationToken);
    }

    public ValueTask<AgentDesktopFailure?> CheckAgentDesktopTargetAsync(
        AgentDesktopAction action,
        CancellationToken cancellationToken) =>
        inner.CheckAgentDesktopTargetAsync(action, cancellationToken);

    public ValueTask<HostResult<AgentDesktopResult>> RunAgentDesktopActionAsync(
        AgentAuthorizationId authorizationId,
        AgentDesktopAction action,
        CancellationToken cancellationToken) =>
        inner.RunAgentDesktopActionAsync(authorizationId, action, cancellationToken);

    public void ReleaseAgentDesktopRun(AgentRunId runId) => inner.ReleaseAgentDesktopRun(runId);
}
