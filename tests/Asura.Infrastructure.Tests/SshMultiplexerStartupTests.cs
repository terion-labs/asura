using System.Diagnostics;
using Asura.Application;
using Asura.Core;

namespace Asura.Infrastructure.Tests;

public sealed class SshMultiplexerStartupTests
{
    [Theory]
    [InlineData(false, null, false, 0, 240)]
    [InlineData(true, null, false, 0, 240)]
    [InlineData(true, "tmux", false, 0, 241)]
    [InlineData(true, "screen", false, 0, 241)]
    [InlineData(false, "tmux", false, 0, 0)]
    [InlineData(false, "screen", false, 0, 0)]
    [InlineData(true, "tmux", true, 7, 7)]
    [InlineData(true, "screen", true, 7, 7)]
    [InlineData(true, "tmux", true, 240, 1)]
    [InlineData(true, "screen", true, 241, 1)]
    [InlineData(false, "tmux", false, 241, 1)]
    [InlineData(false, "screen", false, 240, 1)]
    public async Task Remote_script_distinguishes_startup_failures_from_attached_shell_exits(
        bool established,
        string? tool,
        bool exists,
        int attachedExitCode,
        int expectedExitCode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("asura-multiplexer-test-");
        try
        {
            // An isolated PATH makes these real POSIX script executions independent
            // of the developer's installed multiplexers and existing sessions.
            if (tool is not null)
            {
                var executable = Path.Combine(directory.FullName, tool);
                await File.WriteAllTextAsync(executable,
                    "#!/bin/sh\ncase \"$*\" in\n"
                    + $"*has-session*|*' -X select '*) exit {(exists ? 0 : 1)};;\n"
                    + "*start-server*|*set-option*) exit 0;;\n"
                    + $"*) printf 'ATTACHED\\n'; exit {attachedExitCode};;\nesac\n");
                File.SetUnixFileMode(executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var endpoint = new ConnectionEndpoint.Ssh("host.example", username: "deploy");
            var profile = ConnectionRuntimeTestSupport.Profile(endpoint, new ConnectionAuthentication.SshAgent());
            var identity = new TerminalMultiplexerSession(
                TerminalMultiplexingMode.Automatic, "asura-startup-test", established);
            var command = SshConnectionArguments.Open(profile, endpoint, multiplexerSession: identity)[^1];
            var start = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["PATH"] = directory.FullName;
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(command);
            using var process = Process.Start(start)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(deadline.Token);
                var error = await process.StandardError.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token);
                Assert.True(process.ExitCode == expectedExitCode, error);
                Assert.Equal(expectedExitCode < 240, output.Contains("ATTACHED", StringComparison.Ordinal));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
