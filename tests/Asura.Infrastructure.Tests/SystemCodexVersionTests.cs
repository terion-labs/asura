using Asura.Application;

namespace Asura.Infrastructure.Tests;

public sealed class SystemCodexVersionTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("asura-codex-version-");

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

    [Theory]
    [InlineData("Codex.app/Contents/Resources/codex")]
    [InlineData("ChatGPT.app/Contents/Resources/codex")]
    [InlineData("Codex.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex")]
    [InlineData("ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex")]
    public async Task Desktop_path_reads_real_bundled_cli_and_observes_updates(string relativePath)
    {
        if (OperatingSystem.IsWindows()) { return; }
        var executable = CreateExecutable(Path.Combine("User Applications", relativePath), "1.23.45-alpha.1");
        var reader = CreateReader("/usr/bin:/bin:/usr/sbin:/sbin");

        Assert.Equal("1.23.45", await reader.ReadAsync(CancellationToken.None));
        _ = CreateExecutable(Path.GetRelativePath(_directory.FullName, executable), "1.24.0");
        Assert.Equal("1.24.0", await reader.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Working_path_cli_takes_precedence_over_bundled_cli()
    {
        if (OperatingSystem.IsWindows()) { return; }
        _ = CreateExecutable("path/codex", "1.23.45");
        _ = CreateExecutable("User Applications/ChatGPT.app/Contents/Resources/codex", "2.0.0");

        Assert.Equal("1.23.45", await CreateReader(Path.Combine(_directory.FullName, "path"))
            .ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("1.23.45", 1)]
    [InlineData("invalid", 0)]
    public async Task Unusable_path_cli_does_not_hide_working_bundled_cli(string version, int exitCode)
    {
        if (OperatingSystem.IsWindows()) { return; }
        _ = CreateExecutable("path/codex", version, exitCode);
        _ = CreateExecutable("User Applications/ChatGPT.app/Contents/Resources/codex", "2.0.0");

        Assert.Equal("2.0.0", await CreateReader(Path.Combine(_directory.FullName, "path"))
            .ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Path_launcher_with_missing_interpreter_falls_back_to_bundled_cli()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var executable = CreateExecutable("path/codex", "1.0.0");
        File.WriteAllText(executable, "#!/missing-codex-interpreter\n");
        _ = CreateExecutable("User Applications/ChatGPT.app/Contents/Resources/codex", "2.0.0");

        Assert.Equal("2.0.0", await CreateReader(Path.Combine(_directory.FullName, "path"))
            .ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Non_executable_and_dangling_candidates_do_not_hide_a_later_installation()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var denied = CreateExecutable("User Applications/Codex.app/Contents/Resources/codex", "1.0.0");
        File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(denied)!, "codex-cli"),
            Path.Combine(_directory.FullName, "removed-installation"));
        _ = CreateExecutable("User Applications/ChatGPT.app/Contents/Resources/codex", "2.0.0");

        Assert.Equal("2.0.0", await CreateReader().ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_real_version_process_preserves_caller_cancellation()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var executable = CreateExecutable("path/codex", "1.23.45");
        var ready = Path.Combine(_directory.FullName, "version-process-ready");
        File.WriteAllText(executable, $"#!/bin/sh\ntouch '{ready.Replace("'", "'\\''", StringComparison.Ordinal)}'\nexec /bin/sleep 30\n");
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reading = CreateReader(Path.Combine(_directory.FullName, "path")).ReadAsync(cancellation.Token).AsTask();
        while (!File.Exists(ready))
        {
            Assert.False(reading.IsCompleted, "The version process must start before cancellation.");
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reading);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private SystemCodexVersion CreateReader(string? inheritedPath = null) => new(
        new PathConnectionExecutableLocator(inheritedPath, []),
        new WorkspaceIsolationCommandRunner(),
        [Path.Combine(_directory.FullName, "User Applications")]);

    private string CreateExecutable(string relativePath, string version, int exitCode = 0)
    {
        var executable = Path.Combine(_directory.FullName, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, $"""
            #!/bin/sh
            test "$#" -eq 1 && test "$1" = "--version" || exit 23
            printf '%s\n' 'codex-cli {version}'
            exit {exitCode}
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return executable;
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
