using Asura.Application;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// Evidence that needs only the staged libraries. It runs wherever they are
/// staged, the default gate included, and never needs the Accessibility grant.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeEvidenceFactAttribute : FactAttribute
{
    public NativeEvidenceFactAttribute(params EvidenceFlavor[] flavors)
    {
        Flavors = flavors;
        Skip = flavors.Select(EvidenceLibraries.SkipReason).FirstOrDefault(reason => reason is not null);
    }

    public IReadOnlyList<EvidenceFlavor> Flavors { get; }
}

/// <inheritdoc cref="NativeEvidenceFactAttribute"/>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeEvidenceTheoryAttribute : TheoryAttribute
{
    public NativeEvidenceTheoryAttribute(params EvidenceFlavor[] flavors)
    {
        Flavors = flavors;
        Skip = flavors.Select(EvidenceLibraries.SkipReason).FirstOrDefault(reason => reason is not null);
    }

    public IReadOnlyList<EvidenceFlavor> Flavors { get; }
}

/// <summary>
/// Evidence that launches the fixture application and so puts windows on the
/// screen. It runs only under <c>collect-evidence.sh</c>, which sets
/// <see cref="EvidenceRun.RunnerVariable"/> after building the fixture. It
/// needs no Accessibility grant.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class FixtureEvidenceFactAttribute : FactAttribute
{
    public FixtureEvidenceFactAttribute()
    {
        Skip = EvidenceLibraries.SkipReason(EvidenceFlavor.Release) ?? EvidenceRun.FixtureSkipReason();
    }
}

/// <summary>
/// Live evidence (ADR 0061 part B): the fixture driven through the governed
/// path with the operating system's Accessibility grant. Without the grant it
/// is skipped with that reason; it never passes without exercising the path.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class GrantedEvidenceFactAttribute : FactAttribute
{
    public GrantedEvidenceFactAttribute()
    {
        Skip = GrantedEvidenceSkipReason();
    }

    internal static string? GrantedEvidenceSkipReason() =>
        EvidenceLibraries.SkipReason(EvidenceFlavor.Release)
        ?? (EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.Granted
            ? EvidenceRun.FixtureSkipReason()
            : "Requires the macOS Accessibility grant for the test host (System Settings > Privacy & Security > "
                + $"Accessibility); agent-desktop reported {EvidenceLibraries.ReleaseAccess()}.");
}

/// <inheritdoc cref="GrantedEvidenceFactAttribute"/>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class GrantedEvidenceTheoryAttribute : TheoryAttribute
{
    public GrantedEvidenceTheoryAttribute()
    {
        Skip = GrantedEvidenceFactAttribute.GrantedEvidenceSkipReason();
    }
}

/// <summary>
/// Evidence of behaviour that exists only while the grant is absent, such as
/// the library's own permission refusal.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NotGrantedEvidenceFactAttribute : FactAttribute
{
    public NotGrantedEvidenceFactAttribute()
    {
        Skip = EvidenceLibraries.SkipReason(EvidenceFlavor.Release)
            ?? (EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.NotGranted
                ? null
                : "The test host holds the Accessibility grant, so the not-granted path cannot be observed; "
                    + $"agent-desktop reported {EvidenceLibraries.ReleaseAccess()}.");
    }
}
