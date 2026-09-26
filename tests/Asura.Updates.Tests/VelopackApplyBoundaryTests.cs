using Asura.Application.ApplicationUpdates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;

namespace Asura.Updates.Tests;

public sealed class VelopackApplyBoundaryTests
{
    [Theory]
    [InlineData("/Applications/Asura.app")]
    [InlineData("/Users/test/Applications/Asura.app")]
    [InlineData("/Users/test/Downloads/Asura.app")]
    public async Task Apply_allows_authorization_and_requests_graceful_restart(string installationDirectory)
    {
        var directory = Directory.CreateTempSubdirectory("asura-updater-boundary-").FullName;
        try
        {
            var updater = Path.Combine(directory, "Update");
            File.WriteAllText(updater, "test placeholder; never executed");
            var process = new CapturingProcess();
            var locator = new CapturingLocator(installationDirectory, directory, updater, process);
            var shutDown = false;
            var preparation = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new VelopackApplicationUpdateService(
                new DistributionIdentity(DistributionSource.GitHubRelease, ApplicationUpdateStrategy.Velopack, "stable"),
                async handoff =>
                {
                    await preparation.Task;
                    handoff();
                    shutDown = true;
                },
                locator);

            Assert.True(service.Snapshot.CanRestartToApply);
            var restart = service.RestartToApplyAsync();
            Assert.Equal(ApplicationUpdateStage.PreparingToRestart, service.Snapshot.Stage);
            Assert.False(service.Snapshot.CanRestartToApply);
            Assert.False(service.Snapshot.CanCheck);
            Assert.False(restart.IsCompleted);
            Assert.Null(process.Executable);
            await service.RestartToApplyAsync();
            Assert.Null(process.Executable);
            preparation.SetResult();
            await restart.WaitAsync(TimeSpan.FromSeconds(5));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsynchronousPreparationFailureReportsApplyFailureWithoutLaunchingUpdater(bool hasStep)
    {
        var directory = Directory.CreateTempSubdirectory("asura-updater-failure-").FullName;
        try
        {
            var updater = Path.Combine(directory, "Update");
            File.WriteAllText(updater, "test placeholder; never executed");
            var process = new CapturingProcess();
            var preparation = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var log = new CapturingLogger();
            var locator = new CapturingLocator("/Applications/Asura.app", directory, updater, process, log);
            var service = new VelopackApplicationUpdateService(
                new DistributionIdentity(DistributionSource.GitHubRelease, ApplicationUpdateStrategy.Velopack, "stable"),
                _ => preparation.Task,
                locator);

            var restart = service.RestartToApplyAsync();
            var underlying = new IOException("Private path /Users/example/secret and credential=private-value");
            preparation.SetException(hasStep
                ? new UpdateRestartPreparationException(UpdateRestartStep.ClosePresentation, underlying)
                : underlying);
            await restart.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(ApplicationUpdateStage.Failed, service.Snapshot.Stage);
            Assert.Equal(ApplicationUpdateError.ApplyFailed, service.Snapshot.Error);
            Assert.Null(process.Executable);
            var error = Assert.Single(log.Errors);
            Assert.Contains(hasStep ? "update.restart.presentation-close.failed" : "update.restart.failed", error,
                StringComparison.Ordinal);
            Assert.Contains("type=io", error, StringComparison.Ordinal);
            Assert.DoesNotContain("private", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users/", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CapturingLogger : IVelopackLogger
    {
        public List<string> Errors { get; } = [];

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            Assert.Null(exception);
            if (logLevel == VelopackLogLevel.Error)
            {
                Errors.Add(message ?? string.Empty);
            }
        }
    }

    private sealed class CapturingLocator(string rootDirectory, string directory, string updater, CapturingProcess process,
        IVelopackLogger? logger = null)
        : TestVelopackLocator(
            "Asura", "1.0.0", rootDirectory, directory, directory, updater,
            logger: logger,
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
