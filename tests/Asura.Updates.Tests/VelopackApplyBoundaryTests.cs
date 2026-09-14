using Asura.Application.ApplicationUpdates;
using Velopack;
using Velopack.Locators;

namespace Asura.Updates.Tests;

public sealed class VelopackApplyBoundaryTests
{
    [Theory]
    [InlineData("/Applications/Asura.app")]
    [InlineData("/Users/test/Applications/Asura.app")]
    [InlineData("/Users/test/Downloads/Asura.app")]
    public void Apply_allows_authorization_and_requests_graceful_restart(string installationDirectory)
    {
        var directory = Directory.CreateTempSubdirectory("asura-updater-boundary-").FullName;
        try
        {
            var updater = Path.Combine(directory, "Update");
            File.WriteAllText(updater, "test placeholder; never executed");
            var process = new CapturingProcess();
            var locator = new CapturingLocator(installationDirectory, directory, updater, process);
            var shutDown = false;
            var service = new VelopackApplicationUpdateService(
                new DistributionIdentity(DistributionSource.GitHubRelease, ApplicationUpdateStrategy.Velopack, "stable"),
                () => shutDown = true,
                locator);

            Assert.True(service.Snapshot.CanRestartToApply);
            service.RestartToApply();

            Assert.True(shutDown);
            Assert.Equal(updater, process.Executable);
            Assert.Contains("apply", process.Arguments, StringComparer.Ordinal);
            Assert.DoesNotContain("--silent", process.Arguments, StringComparer.Ordinal);
            Assert.DoesNotContain("--norestart", process.Arguments, StringComparer.Ordinal);
            Assert.Contains("--rootDir", process.Arguments, StringComparer.Ordinal);
            Assert.Contains(installationDirectory, process.Arguments, StringComparer.Ordinal);
            Assert.Contains("--waitPid", process.Arguments, StringComparer.Ordinal);
            Assert.Contains("42", process.Arguments, StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CapturingLocator(string rootDirectory, string directory, string updater, CapturingProcess process)
        : TestVelopackLocator(
            "Asura", "1.0.0", rootDirectory, directory, directory, updater,
            localPackage: new VelopackAsset { Version = SemanticVersion.Parse("2.0.0"), FileName = "test.nupkg" })
    {
        public override IProcessImpl Process => process;
    }

    private sealed class CapturingProcess : IProcessImpl
    {
        public string? Executable { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string GetCurrentProcessPath() => "/test/Asura";
        public uint GetCurrentProcessId() => 42;
        public void Exit(int exitCode) => throw new InvalidOperationException("The app owns shutdown.");
        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow)
        {
            Executable = exePath;
            Arguments = [.. args];
        }
    }
}
