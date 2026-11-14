using Asura.Core;

namespace Asura.Application;

/// <summary>
/// The agent called a desktop tool on a computer where the system has not
/// yet let Asura operate other applications, and it is waiting for the
/// person to turn Asura on in the system's settings (ADR 0061). It carries
/// no model text: the card that shows it is written entirely by Asura, and
/// the system's own settings are the only place the grant is given.
/// </summary>
/// <param name="Id">Names this wait, so dismissing a card cannot end a later one.</param>
/// <param name="RunId">The run whose tool call is waiting.</param>
/// <param name="ExpiresAtUtc">When the call gives up and reports that access was not granted.</param>
public sealed record GovernedAgentDesktopAccessRequest(
    Guid Id,
    AgentRunId RunId,
    DateTimeOffset ExpiresAtUtc)
{
    /// <summary>
    /// Long enough to find the setting, unlock it and come back; short
    /// enough that a forgotten card does not hold the turn indefinitely.
    /// </summary>
    public static readonly TimeSpan WaitLifetime = TimeSpan.FromMinutes(5);
}
