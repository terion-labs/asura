using System.Runtime.CompilerServices;

namespace Asura.DesktopAutomation.Tests;

[Collection(AgentDesktopNativeCollection.Name)]
public sealed unsafe class AgentDesktopLibraryNativeTests
{
    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public void The_release_library_loads_and_passes_the_abi_and_struct_size_handshake()
    {
        var library = AgentDesktopLibrary.TryLoadFrom(
            AgentDesktopTestRuntime.LibraryPath(AgentDesktopFlavor.Release),
            out var detail);

        Assert.True(library is not null, detail);
        Assert.Equal("The agent-desktop library is loaded.", detail);
        Assert.Equal(AgentDesktopTestRuntime.LibraryPath(AgentDesktopFlavor.Release), library.Path);
        Assert.Equal(AdConstants.AbiMajor, library.AbiVersion());
        Assert.Equal(AdResult.Ok, library.Init(AdConstants.AbiMajor));
        Assert.Equal(AdResult.InvalidArguments, library.Init(AdConstants.AbiMajor + 1));
        Assert.Null(AgentDesktopTestRuntime.Engine(AgentDesktopFlavor.Release).UnavailableReason);
    }

    [Fact]
    public void The_struct_mirrors_have_the_sizes_the_header_declares()
    {
        // AD_ACTION_SIZE, AD_EXACT_WINDOW_INFO_SIZE, AD_WAIT_ARGS_SIZE and
        // AD_DELIVERY_SEMANTICS_SIZE in agent_desktop.h, ABI major 7. The
        // loader compares the first three with the library's own getters;
        // this pins them on platforms where no library is staged.
        Assert.Equal(96, Unsafe.SizeOf<AdAction>());
        Assert.Equal(96, Unsafe.SizeOf<AdExactWindowInfo>());
        Assert.Equal(104, Unsafe.SizeOf<AdWaitArgs>());
        Assert.Equal(8, Unsafe.SizeOf<AdDeliverySemantics>());
        Assert.Equal(24, Unsafe.SizeOf<AdAppInfo>());
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public void The_shipped_library_has_no_panic_injection_export()
    {
        Assert.True(AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Release).TestPanicBoundary is null);
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release, AgentDesktopFlavor.Stub, AgentDesktopFlavor.Panic)]
    public void The_three_flavors_load_as_separate_images_in_one_process()
    {
        // All three share the install name @rpath/libagent_desktop_ffi.dylib.
        // Were the loader to reuse the first image for the others, the
        // evidence tests would silently run against the release library.
        var release = AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Release);
        var stub = AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Stub);
        var panic = AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Panic);

        var entryPoints = new[] { (nint)release.AbiVersion, (nint)stub.AbiVersion, (nint)panic.AbiVersion };
        Assert.Equal(3, entryPoints.Distinct().Count());
        Assert.True(release.TestPanicBoundary is null);
        Assert.True(stub.TestPanicBoundary is null);
        Assert.True(panic.TestPanicBoundary is not null);
    }

    [StagedLibraryFact(AgentDesktopFlavor.Release)]
    public void Loading_the_same_path_twice_yields_the_same_image()
    {
        var first = AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Release);
        var second = AgentDesktopTestRuntime.Load(AgentDesktopFlavor.Release);

        Assert.Equal((nint)first.AbiVersion, (nint)second.AbiVersion);
    }

    [Fact]
    public void A_missing_library_is_reported_not_thrown()
    {
        var library = AgentDesktopLibrary.TryLoadFrom(
            Path.Combine(Path.GetTempPath(), "asura-no-such-directory", "libagent_desktop_ffi.dylib"),
            out var detail);

        Assert.True(library is null);
        Assert.Equal("The agent-desktop library could not be loaded.", detail);
    }

    [UnixFact]
    public void A_library_without_the_agent_desktop_exports_is_rejected()
    {
        var library = AgentDesktopLibrary.TryLoadFrom(
            OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6",
            out var detail);

        Assert.True(library is null);
        Assert.Equal("The agent-desktop library is missing a required export.", detail);
    }
}
