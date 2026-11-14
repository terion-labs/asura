using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

public sealed partial class GovernedAgentRuntime
{
    /// <summary>How often a waiting desktop call asks whether the grant has arrived.</summary>
    private static readonly TimeSpan DesktopAccessPollInterval = TimeSpan.FromSeconds(1);

    private DesktopAccessWait? _desktopAccessWait;

    public ValueTask<bool> DismissDesktopAccessRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(
                _desktopAccessWait is { } wait
                && wait.Request.Id == requestId
                && wait.Dismissed.TrySetResult());
        }
    }

    /// <summary>
    /// Whether a call without the system grant may wait for the person to
    /// give it. Not when the policy refuses the call anyway, which would make
    /// the person grant access for nothing, and not for an external operator,
    /// whose calls come from outside the chat that shows the card.
    /// </summary>
    private bool CanWaitForDesktopAccess(AgentCapability capability)
    {
        lock (_gate)
        {
            return !_externalRun
                && _effectivePolicy.GetPermission(capability) != AgentPermission.Off;
        }
    }

    /// <summary>
    /// Holds a desktop tool call until the system lets Asura operate other
    /// applications, the person dismisses the card, the wait expires or the
    /// turn ends (ADR 0061). The snapshot carries the pending request, and the
    /// app shows a card that opens the system's settings; Asura itself never
    /// asks the system for the grant. Returns the access state the call
    /// continues with: Granted once the person has turned Asura on.
    /// </summary>
    private async ValueTask<AgentDesktopAccessState> AwaitDesktopAccessAsync(
        AgentContextSnapshot context,
        CancellationToken cancellationToken)
    {
        DesktopAccessWait wait;
        lock (_gate)
        {
            // One call waits at a time; a parallel call reports the missing
            // grant instead of stacking a second card.
            if (_desktopAccessWait is not null)
            {
                return AgentDesktopAccessState.NotGranted;
            }

            wait = new DesktopAccessWait(new GovernedAgentDesktopAccessRequest(
                Guid.NewGuid(),
                GetRequiredSession().RunId,
                _timeProvider.GetUtcNow() + GovernedAgentDesktopAccessRequest.WaitLifetime));
            _desktopAccessWait = wait;
            _snapshot = _snapshot with
            {
                PendingDesktopAccess = wait.Request,
                Status = "Waiting for you to let Asura operate other apps…",
            };
        }

        NotifyChanged();
        try
        {
            while (true)
            {
                var remaining = wait.Request.ExpiresAtUtc - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return AgentDesktopAccessState.NotGranted;
                }

                var delay = Task.Delay(
                    remaining < DesktopAccessPollInterval ? remaining : DesktopAccessPollInterval,
                    _timeProvider,
                    cancellationToken);
                if (await Task.WhenAny(delay, wait.Dismissed.Task).ConfigureAwait(false) == wait.Dismissed.Task)
                {
                    return AgentDesktopAccessState.NotGranted;
                }

                // Rethrows a cancelled turn.
                await delay.ConfigureAwait(false);
                var access = await ReadDesktopAccessAsync(context, cancellationToken)
                    .ConfigureAwait(false);
                if (access != AgentDesktopAccessState.NotGranted)
                {
                    return access;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_desktopAccessWait, wait))
                {
                    _desktopAccessWait = null;
                    _snapshot = _snapshot with { PendingDesktopAccess = null };
                }
            }

            NotifyChanged();
        }
    }

    private sealed class DesktopAccessWait(GovernedAgentDesktopAccessRequest request)
    {
        public GovernedAgentDesktopAccessRequest Request { get; } = request;

        public TaskCompletionSource Dismissed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
