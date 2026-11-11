using System.Text.Json.Nodes;
using Asura.Application;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// What the rest of the evidence ran against: the receipted release library,
/// loaded from the application directory only (ADR 0061 decision 6), and the
/// grant state of the host, which decides which items could be collected.
/// </summary>
[Collection(DesktopEvidenceCollection.Name)]
public sealed class LibraryIdentityEvidenceTests
{
    [NativeEvidenceFact(EvidenceFlavor.Release)]
    public async Task The_shipped_library_is_the_receipted_build_and_loads_from_the_application_directory()
    {
        var receipt = EvidenceLibraries.Receipt(EvidenceFlavor.Release);
        var loaded = Path.Combine(AppContext.BaseDirectory, EvidenceLibraries.LibraryFileName);

        Assert.Equal(
            receipt.GetProperty("library").GetProperty("sha256").GetString(),
            EvidenceLibraries.Sha256(loaded));
        Assert.Equal(
            receipt.GetProperty("helper").GetProperty("sha256").GetString(),
            EvidenceLibraries.Sha256(Path.Combine(AppContext.BaseDirectory, "agent-desktop-macos-helper")));
        Assert.Equal("release", receipt.GetProperty("flavor").GetString());
        Assert.False(receipt.GetProperty("abi").GetProperty("panicInjectionExport").GetBoolean());
        Assert.Equal("unwind", receipt.GetProperty("toolchain").GetProperty("panicStrategy").GetString());

        var access = await EvidenceLibraries.Engine(EvidenceFlavor.Release).GetAccessStateAsync(CancellationToken.None);
        Assert.NotEqual(AgentDesktopAccessState.Unavailable, access);
        EvidenceRun.Observe(
            "A0",
            "host",
            new JsonObject
            {
                ["accessibility"] = access.ToString(),
                ["librarySha256"] = EvidenceLibraries.Sha256(loaded),
            });
    }
}
