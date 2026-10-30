using System.Diagnostics;
using Asura.App;
using Asura.Processes;

namespace Asura.Desktop;

/// <summary>
/// <c>asura open PATH [--as terminal|files|git]</c>: opens a folder in a new
/// tab of the running Asura, starting it when it is not running. A terminal
/// there is the default; Files shows the folder, and Git opens the
/// repository it is in.
/// </summary>
internal static class OpenFolderCommand
{
    internal const string Verb = "open";
    internal const string ViewSwitch = "--as";

    /// <summary>
    /// Marks a launch that a command line handed to the system's launcher,
    /// so the new instance opens the folder instead of handing it off again.
    /// </summary>
    internal const string LaunchedSwitch = "--open-launched";

    internal const string Usage =
        "Usage: asura open PATH [--as terminal|files|git]\n"
        + "Opens the folder PATH in a new Asura tab: a terminal there (the default),\n"
        + "the Files panel showing it, or the Git panel for the repository it is in.";

    /// <summary>What the command line asked for.</summary>
    internal sealed record Invocation(OpenFolderRequest Request, bool WasLaunched);

    /// <summary>Whether <paramref name="arguments"/> are an <c>open</c> command.</summary>
    internal static bool IsInvocation(string[] arguments) =>
        arguments.Length > 0 && string.Equals(arguments[0], Verb, StringComparison.Ordinal);

    /// <summary>
    /// Reads an <c>open</c> command. A relative path is taken from
    /// <paramref name="currentDirectory"/>. Throws <see cref="ArgumentException"/>
    /// with the message to print when the command is malformed or the folder
    /// cannot be opened as asked.
    /// </summary>
    internal static Invocation Parse(string[] arguments, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? path = null;
        OpenFolderView? view = null;
        var launched = false;
        for (var index = 1; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, LaunchedSwitch, StringComparison.Ordinal))
            {
                launched = true;
            }
            else if (string.Equals(argument, ViewSwitch, StringComparison.Ordinal))
            {
                if (index + 1 >= arguments.Length)
                {
                    throw new ArgumentException("--as needs a view: terminal, files, or git.");
                }

                view = RequireView(arguments[++index]);
            }
            else if (argument.StartsWith(ViewSwitch + "=", StringComparison.Ordinal))
            {
                view = RequireView(argument[(ViewSwitch.Length + 1)..]);
            }
            else if (argument.StartsWith('-') && argument.Length > 1)
            {
                throw new ArgumentException($"Unknown option {argument}.");
            }
            else if (path is null)
            {
                path = argument;
            }
            else
            {
                throw new ArgumentException("Open one folder at a time.");
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Name the folder to open.");
        }

        var full = Path.GetFullPath(path, currentDirectory);
        if (!Directory.Exists(full))
        {
            throw new ArgumentException($"There is no folder at {full}.");
        }

        if (view == OpenFolderView.Git && OpenFolderRequest.FindRepositoryRoot(full) is null)
        {
            throw new ArgumentException($"{full} is not in a Git repository.");
        }

        var request = OpenFolderRequest.TryCreate(full, view ?? OpenFolderView.Terminal)
            ?? throw new ArgumentException($"{full} cannot be opened.");
        return new Invocation(request, launched);
    }

    /// <summary>
    /// On a Mac, an <c>open</c> run from a shell that finds no Asura running
    /// starts <paramref name="bundle"/> through the system's launcher rather
    /// than becoming the app itself, so it does not belong to the shell and
    /// end with it. Returns false when the launcher could not start it.
    /// </summary>
    internal static async Task<bool> TryLaunchApplicationAsync(string bundle, OpenFolderRequest request)
    {

        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var argument in new[]
        {
            "-a", bundle, "--args",
            Verb, request.Path, ViewSwitch, OpenFolderRequest.ViewName(request.View ?? OpenFolderView.Terminal),
            LaunchedSwitch,
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = ChildProcess.Start(start);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static OpenFolderView RequireView(string name) =>
        OpenFolderRequest.ParseView(name)
        ?? throw new ArgumentException($"Unknown view {name}: use terminal, files, or git.");

    /// <summary>
    /// The Mac app bundle this executable runs from, if it runs from one.
    /// Outside one there is nothing to hand the folder to, and this process
    /// opens it itself.
    /// </summary>
    internal static string? ApplicationBundle()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        return bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(bundle, "Contents", "MacOS"))
            ? bundle
            : null;
    }
}
