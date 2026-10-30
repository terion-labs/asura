using System.Diagnostics;
using System.Text;
using Asura.Processes;

namespace Asura.Desktop;

/// <summary>
/// Puts an <c>asura</c> command on the shell's path, so
/// <c>asura open PATH [--as terminal|files|git]</c> works from any terminal,
/// as an editor's "Install command" does. The command is a short script that
/// runs this Asura's executable; it is written where the system's shells
/// look by default, asking for an administrator's password through the
/// system when that folder is not writable.
/// </summary>
internal static class CommandLineToolInstaller
{
    internal const string Destination = "/usr/local/bin/asura";

    /// <summary>Whether this Asura can install the command: a Mac app run from its bundle.</summary>
    internal static bool IsAvailable =>
        OperatingSystem.IsMacOS()
        && Environment.ProcessPath is { } executable
        && executable.Contains(".app/Contents/MacOS/", StringComparison.Ordinal);

    /// <summary>The script, running <paramref name="executable"/> with the arguments it is given.</summary>
    internal static string Script(string executable) =>
        "#!/bin/sh\n"
        + "# Installed by Asura. Run `asura open PATH [--as terminal|files|git]`.\n"
        + "exec " + ShellQuote(executable) + " \"$@\"\n";

    /// <summary>
    /// Installs the command. Returns null when it is in place, or the reason
    /// it could not be — including the administrator prompt being cancelled.
    /// </summary>
    internal static async Task<string?> InstallAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable || Environment.ProcessPath is not { } executable)
        {
            return "The command line tool can be installed only from the Asura app on a Mac.";
        }

        var script = Script(executable);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            await WriteScriptAsync(Destination, script, cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(Destination, ExecutableMode);
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // /usr/local/bin belongs to an administrator on a fresh Mac.
        }
        catch (IOException)
        {
        }

        var staged = Path.Combine(Path.GetTempPath(), "asura-command-" + Guid.NewGuid().ToString("N"));
        try
        {
            await WriteScriptAsync(staged, script, cancellationToken).ConfigureAwait(false);
            var command = "mkdir -p " + ShellQuote(Path.GetDirectoryName(Destination)!)
                + " && cp " + ShellQuote(staged) + " " + ShellQuote(Destination)
                + " && chmod 755 " + ShellQuote(Destination);
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("do shell script " + AppleScriptQuote(command) + " with administrator privileges");
            using var process = ChildProcess.Start(start);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? null
                : "The command line tool was not installed. An administrator's permission is needed to write to /usr/local/bin.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "The command line tool could not be installed: " + exception.Message;
        }
        finally
        {
            try
            {
                File.Delete(staged);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="script"/> as plain UTF-8: a byte-order mark
    /// before <c>#!</c> would keep the system from reading the interpreter.
    /// </summary>
    internal static Task WriteScriptAsync(string path, string script, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, script, ScriptEncoding, cancellationToken);

    private static readonly UTF8Encoding ScriptEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>A word the shell reads back exactly: single-quoted, with each quote spliced in.</summary>
    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string AppleScriptQuote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
