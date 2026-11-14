using System.Diagnostics;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// Sends one message and plays the user for every card the turn raises:
/// action approvals, capability requests (ADR 0036) and the wait for the
/// system grant (ADR 0061 amendment 30), recording each one. A card the test
/// did not say how to answer fails the turn.
/// </summary>
internal static class RuntimeDriver
{
    private static readonly TimeSpan TurnBudget = TimeSpan.FromSeconds(90);

    public static async Task<RuntimeTurn> SendAsync(
        GovernedAgentRuntime runtime,
        GovernedAgentPrompt prompt,
        Func<GovernedAgentApproval, bool>? decide = null,
        Func<GovernedAgentCapabilityRequest, bool>? enableCapability = null,
        bool dismissDesktopAccess = false)
    {
        var cards = new List<GovernedAgentApproval>();
        var capabilityRequests = new List<GovernedAgentCapabilityRequest>();
        var desktopAccessRequests = new List<GovernedAgentDesktopAccessRequest>();
        var sending = runtime.SendAsync(prompt, CancellationToken.None).AsTask();
        // Monotonic: a Mac that sleeps mid-run must not read as a stalled turn.
        var elapsed = Stopwatch.StartNew();
        AgentApprovalId? answered = null;
        AgentCapabilityRequestId? answeredCapability = null;
        Guid? dismissedAccess = null;
        while (!sending.IsCompleted)
        {
            if (runtime.Snapshot.PendingCapabilityRequest is { } request && request.Id != answeredCapability)
            {
                answeredCapability = request.Id;
                capabilityRequests.Add(request);
                var enable = enableCapability?.Invoke(request)
                    ?? throw new Xunit.Sdk.XunitException($"Unexpected capability card for {request.CapabilityToken}.");
                Assert.True((await runtime.DecideCapabilityRequestAsync(
                    request.Id,
                    enable
                        ? new GovernedAgentCapabilityDecision.AllowAsk()
                        : new GovernedAgentCapabilityDecision.KeepOff(),
                    CancellationToken.None)).IsAccepted);
            }

            if (runtime.Snapshot.PendingDesktopAccess is { } access && access.Id != dismissedAccess)
            {
                if (!dismissDesktopAccess)
                {
                    throw new Xunit.Sdk.XunitException("Unexpected wait for the system grant.");
                }

                dismissedAccess = access.Id;
                desktopAccessRequests.Add(access);
                Assert.True(await runtime.DismissDesktopAccessRequestAsync(access.Id, CancellationToken.None));
            }

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

        return new RuntimeTurn(await sending, cards, capabilityRequests, desktopAccessRequests);
    }
}

internal sealed record RuntimeTurn(
    GovernedAgentSendResult Result,
    IReadOnlyList<GovernedAgentApproval> Cards,
    IReadOnlyList<GovernedAgentCapabilityRequest> CapabilityRequests,
    IReadOnlyList<GovernedAgentDesktopAccessRequest> DesktopAccessRequests);
