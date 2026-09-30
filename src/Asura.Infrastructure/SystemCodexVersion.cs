using Asura.Application;

namespace Asura.Infrastructure;

/// <summary>
/// Reads only the installed client's release version. Codex authentication,
/// configuration, and chat sessions remain outside this integration.
/// </summary>
public sealed class SystemCodexVersion
{
    private readonly IConnectionExecutableLocator _locator;
    private readonly IWorkspaceIsolationCommandRunner _runner;
    private readonly IReadOnlyList<string> _applicationDirectories;

    public SystemCodexVersion(IConnectionExecutableLocator locator)
        : this(locator, new WorkspaceIsolationCommandRunner())
    {
    }

    internal SystemCodexVersion(
        IConnectionExecutableLocator locator,
        IWorkspaceIsolationCommandRunner runner,
        IReadOnlyList<string>? applicationDirectories = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _applicationDirectories = applicationDirectories ?? (OperatingSystem.IsMacOS()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"), "/Applications"]
            : []);
    }

    public async ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Lookup and process creation must not block Avalonia's main thread.
            // All candidates share one deadline; the runner drains bounded output
            // and terminates its child on cancellation.
            return await Task.Run(async () =>
            {
                foreach (var executable in FindExecutables())
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    WorkspaceIsolationCommandResult result;
                    try
                    {
                        result = await _runner.RunAsync(
                            new WorkspaceProcessLaunch(executable, ["--version"],
                                new Dictionary<string, string>(StringComparer.Ordinal), hostWorkingDirectory: null),
                            ReadOnlyMemory<byte>.Empty,
                            timeout.Token).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    if (result.ExitCode == 0 && Parse(result.StandardOutput) is { } version)
                    {
                        return version;
                    }
                }

                return null;
            }, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private IEnumerable<string> FindExecutables()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var executable = _locator.Find("codex");
        if (executable is not null)
        {
            _ = seen.Add(executable);
            yield return executable;
        }

        // Finder launches may not inherit the shell PATH. Prefer its Codex when
        // runnable, then check the flat and nested desktop CLI layouts. A stale
        // shim must not hide a working bundle. The GUI itself is never launched.
        foreach (var applications in _applicationDirectories)
        {
            foreach (var bundle in new[] { "Codex.app", "ChatGPT.app" })
            {
                var resources = Path.Combine(applications, bundle, "Contents", "Resources");
                foreach (var relativePath in new[]
                {
                    "codex",
                    Path.Combine("codex-cli", "CodexCLI.app", "Contents", "MacOS", "codex"),
                })
                {
                    executable = _locator.Find(Path.Combine(resources, relativePath));
                    if (executable is not null && seen.Add(executable))
                    {
                        yield return executable;
                    }
                }
            }
        }
    }

    internal static string? Parse(string output)
    {
        const string prefix = "codex-cli ";
        var line = output.Trim();
        if (line.Length > 128 || !line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var versionText = line[prefix.Length..];
        if (versionText.Any(char.IsWhiteSpace))
        {
            return null;
        }

        // Discovery uses the release triplet, as Codex itself does for prereleases.
        var suffix = versionText.IndexOfAny(['-', '+']);
        var release = suffix < 0 ? versionText : versionText[..suffix];
        return Version.TryParse(release, out var version)
            && version.Build >= 0 && version.Revision < 0
                ? version.ToString(3)
                : null;
    }
}
