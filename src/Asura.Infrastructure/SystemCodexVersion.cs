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

    public SystemCodexVersion(IConnectionExecutableLocator locator)
        : this(locator, new WorkspaceIsolationCommandRunner())
    {
    }

    internal SystemCodexVersion(
        IConnectionExecutableLocator locator,
        IWorkspaceIsolationCommandRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = FindExecutable();
        if (executable is null)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Process creation must not block Avalonia's main thread. The shared
            // runner bounds and drains output and terminates its child on timeout.
            var result = await Task.Run(async () => await _runner.RunAsync(
                new WorkspaceProcessLaunch(executable, ["--version"],
                    new Dictionary<string, string>(StringComparer.Ordinal), hostWorkingDirectory: null),
                ReadOnlyMemory<byte>.Empty,
                timeout.Token).ConfigureAwait(false), timeout.Token).ConfigureAwait(false);
            return result.ExitCode == 0 ? Parse(result.StandardOutput) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private string? FindExecutable()
    {
        var executable = _locator.Find("codex");
        if (executable is not null || !OperatingSystem.IsMacOS())
        {
            return executable;
        }

        // Finder launches may not inherit the shell PATH. Prefer its Codex when
        // present, then look in installed desktop bundles without launching them.
        foreach (var applications in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
            "/Applications",
        })
        {
            foreach (var bundle in new[] { "Codex.app", "ChatGPT.app" })
            {
                executable = _locator.Find(Path.Combine(applications, bundle, "Contents", "Resources", "codex"));
                if (executable is not null)
                {
                    return executable;
                }
            }
        }

        return null;
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
