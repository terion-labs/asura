using Asura.Application;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// Hand-built targets: what a proposal would name if a snapshot had produced
/// it. The runtime only builds targets from observations, so these reach the
/// host directly; that is the point of the self-operation evidence.
/// </summary>
internal static class EvidenceTargets
{
    /// <summary>A name no running application has, so a launch can never start anything.</summary>
    public static AgentDesktopApplicationId AbsentApplication { get; } =
        new("Asura Evidence Absent " + Guid.NewGuid().ToString("N")[..8]);

    public static AgentDesktopElementTarget Element(
        uint processId,
        string executable,
        string application = "Evidence Helper",
        string? name = "Record Action",
        string? windowTitle = null,
        bool isSecure = false) =>
        new(
            new AgentDesktopElementRef("@evd:e1"),
            AgentDesktopDisplayText.FromUntrusted(application, "unknown"),
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, executable),
            processId,
            processInstance: null,
            windowId: null,
            AgentDesktopDisplayText.FromUntrusted(windowTitle),
            DateTimeOffset.UtcNow,
            AgentDesktopDisplayText.FromUntrusted("button", "unknown"),
            AgentDesktopDisplayText.FromUntrusted(name),
            isSecure);

    public static AgentDesktopWindowTarget Window(uint processId, string executable, string title = "Evidence Window") =>
        new(
            new AgentDesktopApplicationId("Evidence Helper"),
            new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, executable),
            "evidence-window-1",
            AgentDesktopDisplayText.FromUntrusted(title, "unknown"),
            processId,
            processInstance: null,
            DateTimeOffset.UtcNow);
}
