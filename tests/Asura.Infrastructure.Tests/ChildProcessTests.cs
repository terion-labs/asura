using System.ComponentModel;
using System.Diagnostics;
using Asura.Processes;

namespace Asura.Infrastructure.Tests;

/// <summary>
/// Programs start through posix_spawn, not a fork of the whole app: a forked
/// copy of a Chromium-loaded Asura once deadlocked before it could load its
/// program, held every later launch behind it, and kept the profile's locks
/// after a force quit. These pin what callers rely on from the launcher.
/// </summary>
public sealed class ChildProcessTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Arguments_environment_and_folder_reach_the_program_as_given()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Directory.CreateTempSubdirectory("asura-child-");
        try
        {
            var start = Shell("printf '%s|%s|%s' \"$1\" \"$ASURA_CHILD_TEST\" \"$(pwd -P)\"", "two words");
            start.Environment["ASURA_CHILD_TEST"] = "value with = sign";
            start.WorkingDirectory = folder.FullName;
            using var child = ChildProcess.Start(start);
            var output = await child.StandardOutput.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(Patience);

            Assert.Equal(0, child.ExitCode);
            Assert.Equal(
                $"two words|value with = sign|{ResolvePhysical(folder.FullName)}",
                output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Standard_input_reaches_the_program_and_its_output_comes_back()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var start = new ProcessStartInfo("/bin/cat")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var child = ChildProcess.Start(start);
        await child.StandardInput.WriteAsync("héllo\nworld\n");
        child.StandardInput.Close();
        var output = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(Patience);

        Assert.Equal("héllo\nworld\n", output);
        Assert.True(child.HasExited);
        Assert.Equal(0, child.ExitCode);
    }

    [Fact]
    public async Task Exit_status_and_standard_error_are_reported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var child = ChildProcess.Start(Shell("echo failed >&2; exit 7"));
        child.Exited += (_, _) => exited.TrySetResult();
        var error = await child.StandardError.ReadToEndAsync();
        await exited.Task.WaitAsync(Patience);

        Assert.Equal("failed\n", error);
        Assert.Equal(7, child.ExitCode);
    }

    [Fact]
    public async Task A_killed_program_reports_its_signal_like_Process_does()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var child = ChildProcess.Start(Shell("sleep 30"));
        Assert.False(child.HasExited);
        Assert.Throws<InvalidOperationException>(() => child.ExitCode);
        child.Kill();
        await child.WaitForExitAsync().WaitAsync(Patience);

        Assert.Equal(128 + 9, child.ExitCode);
        child.Kill();
    }

    [Fact]
    public async Task Signals_Asura_ignores_are_back_to_their_defaults_in_the_program()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // .NET ignores SIGPIPE, and an ignored signal survives exec. A program
        // that inherited that would never die of a closed pipe.
        using var child = ChildProcess.Start(Shell("kill -PIPE $$; echo survived"));
        var output = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(Patience);

        Assert.Equal(string.Empty, output);
        Assert.Equal(128 + 13, child.ExitCode);
    }

    [Fact]
    public async Task The_program_gets_no_descriptor_Asura_holds()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var lockPath = Path.Combine(Path.GetTempPath(), $"asura-child-lock-{Guid.NewGuid():N}");
        try
        {
            await using var held = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var descriptor = held.SafeFileHandle.DangerousGetHandle().ToInt32();
            using var child = ChildProcess.Start(Shell(
                $"if [ -e /dev/fd/{descriptor} ]; then echo inherited; else echo closed; fi"));
            var output = await child.StandardOutput.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(Patience);

            Assert.Equal("closed\n", output);
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    [Fact]
    public async Task Killing_the_tree_ends_the_programs_it_started_too()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var child = ChildProcess.Start(Shell("sleep 30 & echo $!; wait"));
        var grandchild = int.Parse(
            (await child.StandardOutput.ReadLineAsync())!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(IsRunning(grandchild));

        child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync().WaitAsync(Patience);

        var deadline = DateTime.UtcNow + Patience;
        while (IsRunning(grandchild) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.False(IsRunning(grandchild), $"The grandchild {grandchild} outlived the tree kill.");
    }

    [Fact]
    public async Task Every_exit_is_announced_however_fast_the_program_ends()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The wrapper used to subscribe after the reaper had started, so a
        // program that ended at once could announce its exit to nobody.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var child = ChildProcess.Start(new ProcessStartInfo("/usr/bin/true") { UseShellExecute = false });
            child.Exited += (_, _) => exited.TrySetResult();
            await exited.Task.WaitAsync(Patience);
        }

        // A handler added after the program has gone still runs.
        using var ended = ChildProcess.Start(new ProcessStartInfo("/usr/bin/true") { UseShellExecute = false });
        await ended.WaitForExitAsync().WaitAsync(Patience);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ended.Exited += (_, _) => late.TrySetResult();
        await late.Task.WaitAsync(Patience);
    }

    [Fact]
    public async Task A_program_that_ended_before_it_was_wrapped_still_announces_its_exit()
    {
        // The reaper starts before the wrapper exists; a program that ends in
        // that gap must still be announced, to handlers added at any time.
        using var child = ChildProcess.FromBackend(new AlreadyExitedBackend());
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        child.Exited += (_, _) => exited.TrySetResult();
        await exited.Task.WaitAsync(Patience);
        Assert.Equal(3, child.ExitCode);
    }

    [Fact]
    public async Task A_running_program_reports_its_resident_memory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var child = ChildProcess.Start(Shell("sleep 30"));
        try
        {
            Assert.True(child.ResidentMemoryBytes > 0, "The system's process information was not read.");
        }
        finally
        {
            child.Kill();
            await child.WaitForExitAsync().WaitAsync(Patience);
        }

        Assert.Null(child.ResidentMemoryBytes);
    }

    [Fact]
    public void A_missing_program_fails_to_start_the_way_Process_does()
    {
        var start = new ProcessStartInfo("asura-no-such-program-" + Guid.NewGuid().ToString("N"))
        {
            UseShellExecute = false,
        };

        Assert.Throws<Win32Exception>(() => ChildProcess.Start(start));
    }

    [Fact]
    public void Shell_execution_and_argument_strings_are_refused()
    {
        Assert.Throws<NotSupportedException>(() => ChildProcess.Start(
            new ProcessStartInfo("https://example.com") { UseShellExecute = true }));
        Assert.Throws<NotSupportedException>(() => ChildProcess.Start(
            new ProcessStartInfo("/bin/echo", "one two") { UseShellExecute = false }));
    }

    private sealed class AlreadyExitedBackend : IChildProcessBackend
    {
        public int Id => 1;

        public StreamWriter? StandardInput => null;

        public StreamReader? StandardOutput => null;

        public StreamReader? StandardError => null;

        public bool HasExited => true;

        public int ExitCode => 3;

        public long? ResidentMemoryBytes => null;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Kill(bool entireProcessTree)
        {
        }

        public void Dispose()
        {
        }
    }

    private static ProcessStartInfo Shell(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("sh");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ResolvePhysical(string path)
    {
        var info = new DirectoryInfo(path);
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        if (target is not null)
        {
            return target.FullName;
        }

        // /var is a link to /private/var on macOS; pwd -P reports the latter.
        return path.StartsWith("/var/", StringComparison.Ordinal) && Directory.Exists("/private" + path)
            ? "/private" + path
            : path;
    }
}
