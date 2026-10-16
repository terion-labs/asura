using System.ComponentModel;
using System.Diagnostics;

namespace Asura.Processes;

/// <summary>
/// A program Asura started: the one way first-party code launches a process.
///
/// <see cref="Process.Start(ProcessStartInfo)"/> forks the whole app on macOS
/// and Linux, and the copy runs the C library's fork handlers before it can
/// load the program. In an app that has loaded Chromium, those handlers can
/// wait on a lock another thread held at the moment of the fork — forever.
/// Asura hit exactly that: one launch never finished, every launch after it
/// queued behind it (Docker and Git stopped answering, quitting hung), and
/// the half-made copy outlived a force quit holding the profile's locks, so
/// each restart opened an empty recovery workspace instead.
///
/// Here a Unix launch goes through <c>posix_spawn</c>, which starts the
/// program without running any of Asura's code in a copy of it, and hands the
/// program only the descriptors it is given — never a lock or a socket the
/// app happens to hold. Windows has no fork; there this wraps
/// <see cref="Process"/>.
/// </summary>
public sealed class ChildProcess : IDisposable
{
    private readonly IChildProcessBackend _backend;
    private readonly Lock _exitGate = new();
    private EventHandler? _exited;
    private bool _exitRaised;

    private ChildProcess(IChildProcessBackend backend)
    {
        _backend = backend;
        // The program may already have ended by now: a continuation on its
        // exit runs even when attached afterwards, so no exit goes unseen.
        _ = _backend.WaitForExitAsync(CancellationToken.None).ContinueWith(
            _ => RaiseExited(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RaiseExited()
    {
        EventHandler? handlers;
        lock (_exitGate)
        {
            _exitRaised = true;
            handlers = _exited;
            _exited = null;
        }

        handlers?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The program's process identifier.</summary>
    public int Id => _backend.Id;

    /// <summary>Writes the program's standard input; requires <see cref="ProcessStartInfo.RedirectStandardInput"/>.</summary>
    public StreamWriter StandardInput => _backend.StandardInput
        ?? throw new InvalidOperationException("Standard input was not redirected.");

    /// <summary>Reads the program's standard output; requires <see cref="ProcessStartInfo.RedirectStandardOutput"/>.</summary>
    public StreamReader StandardOutput => _backend.StandardOutput
        ?? throw new InvalidOperationException("Standard output was not redirected.");

    /// <summary>Reads the program's standard error; requires <see cref="ProcessStartInfo.RedirectStandardError"/>.</summary>
    public StreamReader StandardError => _backend.StandardError
        ?? throw new InvalidOperationException("Standard error was not redirected.");

    public bool HasExited => _backend.HasExited;

    /// <summary>
    /// The exit status, or 128 plus the signal number when a signal ended the
    /// program, as <see cref="Process.ExitCode"/> reports it.
    /// </summary>
    public int ExitCode => _backend.HasExited
        ? _backend.ExitCode
        : throw new InvalidOperationException("The process has not exited.");

    /// <summary>
    /// Raised once, on a thread-pool thread, when the program exits. A handler
    /// added after that still runs, so a program that ends the moment it
    /// starts is never missed.
    /// </summary>
    public event EventHandler? Exited
    {
        add
        {
            lock (_exitGate)
            {
                if (!_exitRaised)
                {
                    _exited += value;
                    return;
                }
            }

            ThreadPool.QueueUserWorkItem(_ => value?.Invoke(this, EventArgs.Empty));
        }

        remove
        {
            lock (_exitGate)
            {
                _exited -= value;
            }
        }
    }

    /// <summary>
    /// Starts the program <paramref name="startInfo"/> describes. The program
    /// is named by <see cref="ProcessStartInfo.FileName"/> and its arguments by
    /// <see cref="ProcessStartInfo.ArgumentList"/>; shell execution and a
    /// single argument string are not supported. Throws
    /// <see cref="Win32Exception"/> when the program cannot be started.
    /// </summary>
    public static ChildProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.FileName);
        if (startInfo.UseShellExecute)
        {
            throw new NotSupportedException(
                "Shell execution is not a process launch. Open documents and addresses through the platform launcher.");
        }

        if (!string.IsNullOrEmpty(startInfo.Arguments))
        {
            throw new NotSupportedException(
                "Pass arguments through ArgumentList, one element per argument.");
        }

        return new ChildProcess(OperatingSystem.IsWindows()
            ? WindowsChildProcess.Start(startInfo)
            : PosixChildProcess.Start(startInfo));
    }

    /// <summary>
    /// How much memory the program holds in RAM right now, or null once it
    /// has exited or when the system will not say.
    /// </summary>
    public long? ResidentMemoryBytes => _backend.HasExited ? null : _backend.ResidentMemoryBytes;

    /// <summary>Wraps a started backend; tests hand it one whose program has already ended.</summary>
    internal static ChildProcess FromBackend(IChildProcessBackend backend) => new(backend);

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _backend.WaitForExitAsync(cancellationToken);

    /// <summary>
    /// Reads standard error to its end and throws it away, so a program that
    /// writes diagnostics nobody reads never stalls on a full pipe.
    /// </summary>
    public void DiscardStandardError() => _ = DiscardAsync(StandardError);

    /// <summary>Ends the program immediately. Does nothing once it has exited.</summary>
    public void Kill() => _backend.Kill(entireProcessTree: false);

    /// <summary>
    /// Ends the program immediately, and with <paramref name="entireProcessTree"/>
    /// every program it started. Does nothing once it has exited.
    /// </summary>
    public void Kill(bool entireProcessTree) => _backend.Kill(entireProcessTree);

    /// <summary>
    /// Releases the program's redirected streams. The program keeps running.
    /// </summary>
    public void Dispose() => _backend.Dispose();

    private static async Task DiscardAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try
        {
            while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The program, or this side, closed the pipe; nothing was wanted from it.
        }
    }
}

internal interface IChildProcessBackend : IDisposable
{
    int Id { get; }

    StreamWriter? StandardInput { get; }

    StreamReader? StandardOutput { get; }

    StreamReader? StandardError { get; }

    bool HasExited { get; }

    int ExitCode { get; }

    long? ResidentMemoryBytes { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill(bool entireProcessTree);
}

/// <summary>Windows starts programs without forking, so the platform type is safe there.</summary>
internal sealed class WindowsChildProcess : IChildProcessBackend
{
    private readonly Process _process;

    private WindowsChildProcess(Process process)
    {
        _process = process;
    }

    public int Id => _process.Id;

    public StreamWriter? StandardInput => _process.StartInfo.RedirectStandardInput ? _process.StandardInput : null;

    public StreamReader? StandardOutput => _process.StartInfo.RedirectStandardOutput ? _process.StandardOutput : null;

    public StreamReader? StandardError => _process.StartInfo.RedirectStandardError ? _process.StandardError : null;

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public long? ResidentMemoryBytes
    {
        get
        {
            try
            {
                _process.Refresh();
                return _process.WorkingSet64;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public static WindowsChildProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new Win32Exception("The process could not be started.");
            }

            return new WindowsChildProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Kill(bool entireProcessTree)
    {
        try
        {
            _process.Kill(entireProcessTree);
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
        }
    }

    public void Dispose() => _process.Dispose();
}
