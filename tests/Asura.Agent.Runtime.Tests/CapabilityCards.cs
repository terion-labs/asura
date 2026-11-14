using Asura.Application;

namespace Asura.Agent.Runtime.Tests;

/// <summary>
/// Answers the capability card a call to an Off tool raises (ADR 0036), the
/// way a person at the desktop would.
/// </summary>
internal static class CapabilityCards
{
    /// <summary>Waits for the next card and keeps the capability off.</summary>
    public static async Task KeepNextOffAsync(IGovernedAgentRuntime runtime)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (runtime.Snapshot.PendingCapabilityRequest is null)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The capability card did not appear.");
            }

            await Task.Delay(10);
        }

        var decision = await runtime.DecideCapabilityRequestAsync(
            runtime.Snapshot.PendingCapabilityRequest!.Id,
            new GovernedAgentCapabilityDecision.KeepOff(),
            CancellationToken.None);
        if (!decision.IsAccepted)
        {
            throw new InvalidOperationException(decision.Message);
        }
    }
}
