using System.Diagnostics;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// Sends one message and plays the user for every approval card the turn
/// raises, recording each card.
/// </summary>
internal static class RuntimeDriver
{
    private static readonly TimeSpan TurnBudget = TimeSpan.FromSeconds(90);

    public static async Task<RuntimeTurn> SendAsync(
        GovernedAgentRuntime runtime,
        GovernedAgentPrompt prompt,
        Func<GovernedAgentApproval, bool>? decide = null)
    {
        var cards = new List<GovernedAgentApproval>();
        var sending = runtime.SendAsync(prompt, CancellationToken.None).AsTask();
        // Monotonic: a Mac that sleeps mid-run must not read as a stalled turn.
        var elapsed = Stopwatch.StartNew();
        AgentApprovalId? answered = null;
        while (!sending.IsCompleted)
        {
            if (runtime.Snapshot.PendingApproval is { } card && card.Id != answered)
            {
                answered = card.Id;
                cards.Add(card);
                var approve = decide?.Invoke(card)
                    ?? throw new Xunit.Sdk.XunitException($"Unexpected approval card for {card.ToolName}.");
                Assert.True((await runtime.DecideAsync(card.Id, approve, CancellationToken.None)).IsAccepted);
            }

            if (elapsed.Elapsed >= TurnBudget)
            {
                throw new TimeoutException("The governed turn did not finish.");
            }

            await Task.WhenAny(sending, Task.Delay(10));
        }

        return new RuntimeTurn(await sending, cards);
    }
}

internal sealed record RuntimeTurn(GovernedAgentSendResult Result, IReadOnlyList<GovernedAgentApproval> Cards);
