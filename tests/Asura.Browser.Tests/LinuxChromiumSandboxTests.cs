namespace Asura.Browser.Tests;

public sealed class LinuxChromiumSandboxTests
{
    private static readonly Func<bool> NamespaceProbeMustNotRun =
        () => throw new InvalidOperationException("The namespace probe ran although the helper settled it.");

    [Fact]
    public void RootRefusesBeforeAnyProbe()
    {
        var message = LinuxChromiumSandbox.FindObstacle(
            runningAsRoot: true,
            helperIsSetuidRoot: true,
            NamespaceProbeMustNotRun,
            runningFromAppImage: false,
            "/opt/asura/chrome-sandbox");

        Assert.Contains("as root", message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetuidHelperSettlesItWithoutTheNamespaceProbe()
    {
        Assert.Null(LinuxChromiumSandbox.FindObstacle(
            runningAsRoot: false,
            helperIsSetuidRoot: true,
            NamespaceProbeMustNotRun,
            runningFromAppImage: false,
            "/opt/asura/chrome-sandbox"));
    }

    [Fact]
    public void UsableUserNamespacesSettleItWithoutTheHelper()
    {
        Assert.Null(LinuxChromiumSandbox.FindObstacle(
            runningAsRoot: false,
            helperIsSetuidRoot: false,
            () => true,
            runningFromAppImage: true,
            "/tmp/.mount_Asura/usr/bin/chrome-sandbox"));
    }

    [Fact]
    public void AppImageWithoutNamespacesPointsAtThePackagesAndTheReadme()
    {
        var message = LinuxChromiumSandbox.FindObstacle(
            runningAsRoot: false,
            helperIsSetuidRoot: false,
            () => false,
            runningFromAppImage: true,
            "/tmp/.mount_Asura/usr/bin/chrome-sandbox");

        Assert.NotNull(message);
        Assert.Contains(".deb or .rpm", message, StringComparison.Ordinal);
        Assert.Contains("README", message, StringComparison.Ordinal);
        Assert.DoesNotContain("4755", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualInstallWithoutNamespacesNamesTheHelperFix()
    {
        var message = LinuxChromiumSandbox.FindObstacle(
            runningAsRoot: false,
            helperIsSetuidRoot: false,
            () => false,
            runningFromAppImage: false,
            "/opt/asura/chrome-sandbox");

        Assert.NotNull(message);
        Assert.Contains("/opt/asura/chrome-sandbox", message, StringComparison.Ordinal);
        Assert.Contains("4755", message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOrOrdinaryHelperIsNotSetuidRoot()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var missing = Path.Combine(Path.GetTempPath(), $"asura-no-helper-{Guid.NewGuid():N}");
        Assert.False(LinuxChromiumSandbox.HelperIsSetuidRoot(missing));

        var ordinary = Path.Combine(Path.GetTempPath(), $"asura-helper-{Guid.NewGuid():N}");
        File.WriteAllBytes(ordinary, []);
        try
        {
            File.SetUnixFileMode(ordinary, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.OtherExecute);
            Assert.False(LinuxChromiumSandbox.HelperIsSetuidRoot(ordinary));
        }
        finally
        {
            File.Delete(ordinary);
        }
    }

    [Fact]
    public void NamespaceProbeAnswersAndLeavesTheProcessIntact()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Whether the kernel grants a user namespace depends on the host;
        // the probe must answer consistently and leave no child behind.
        var first = LinuxChromiumSandbox.CanCreateUserNamespace();
        var second = LinuxChromiumSandbox.CanCreateUserNamespace();

        Assert.Equal(first, second);
    }
}
