using Asura.Application;

namespace Asura.Infrastructure.Tests;

public sealed class SystemCodexVersionTests
{
    [Theory]
    [InlineData("codex-cli 0.154.0-alpha.6.2\n", "0.154.0")]
    [InlineData("codex-cli 1.23.45", "1.23.45")]
    [InlineData("codex-cli 2.0.1+build.7", "2.0.1")]
    [InlineData("codex-cli 1.2", null)]
    [InlineData("codex-cli 1.2.3.4", null)]
    [InlineData("codex-cli 1.2.3\nunexpected", null)]
    [InlineData("something-else 1.2.3", null)]
    [InlineData("codex-cli invalid", null)]
    public void Parses_only_a_codex_release_triplet(string output, string? expected) =>
        Assert.Equal(expected, SystemCodexVersion.Parse(output));

    [Fact]
    public async Task Reads_only_version_and_observes_updates()
    {
        var executable = Path.GetFullPath("installed-codex");
        var runner = new Runner("codex-cli 1.23.45-alpha.1");
        var reader = new SystemCodexVersion(new Locator(executable), runner);

        Assert.Equal("1.23.45", await reader.ReadAsync(CancellationToken.None));
        var launch = Assert.IsType<WorkspaceProcessLaunch>(runner.Launch);
        Assert.Equal(executable, launch.Executable);
        Assert.Equal(["--version"], launch.Arguments);
        Assert.Empty(launch.Environment);
        Assert.Null(launch.HostWorkingDirectory);
        runner.Output = "codex-cli 1.24.0";
        Assert.Equal("1.24.0", await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Missing_installation_does_not_launch_a_process()
    {
        var runner = new Runner("codex-cli 1.23.45");
        var reader = new SystemCodexVersion(new Locator(null), runner);
        Assert.Null(await reader.ReadAsync(CancellationToken.None));
        Assert.Null(runner.Launch);
    }

    [Fact]
    public async Task Failed_command_does_not_supply_a_version()
    {
        var reader = new SystemCodexVersion(new Locator("codex"),
            new Runner("codex-cli 1.23.45") { ExitCode = 1 });
        Assert.Null(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var reader = new SystemCodexVersion(new Locator("codex"), new Runner("unused"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadAsync(cancellation.Token));
    }

    private sealed class Locator(string? executable) : IConnectionExecutableLocator
    {
        public string? Find(string name) => name == "codex" ? executable : null;
    }

    private sealed class Runner(string output) : IWorkspaceIsolationCommandRunner
    {
        public string Output { get; set; } = output;
        public int ExitCode { get; init; }
        public WorkspaceProcessLaunch? Launch { get; private set; }

        public ValueTask<WorkspaceIsolationCommandResult> RunAsync(
            WorkspaceProcessLaunch launch,
            ReadOnlyMemory<byte> standardInput,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(standardInput.IsEmpty);
            Launch = launch;
            return ValueTask.FromResult(new WorkspaceIsolationCommandResult(ExitCode, Output, string.Empty));
        }
    }
}
