namespace Asura.App;

/// <summary>What a folder handed to Asura from outside opens as.</summary>
public enum OpenFolderView
{
    Terminal,
    Files,
    Git,
}

/// <summary>
/// A local folder to open in a new tab, from the command line
/// (<c>asura open PATH --as VIEW</c>) or dropped on the Dock icon. With no
/// view, Asura asks which one.
/// </summary>
public sealed record OpenFolderRequest
{
    /// <summary>The longest path accepted, in characters.</summary>
    public const int MaximumPathLength = 4_096;

    private OpenFolderRequest(string path, OpenFolderView? view)
    {
        Path = path;
        View = view;
    }

    /// <summary>The folder, as an absolute path.</summary>
    public string Path { get; }

    /// <summary>What it opens as, or null to ask.</summary>
    public OpenFolderView? View { get; }

    /// <summary>
    /// A request for <paramref name="path"/>, or null when it is not a usable
    /// absolute path: relative, too long, or holding control characters.
    /// Whether the folder exists is asked when it opens.
    /// </summary>
    public static OpenFolderRequest? TryCreate(string? path, OpenFolderView? view)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > MaximumPathLength
            || path.Any(char.IsControl)
            || !System.IO.Path.IsPathFullyQualified(path))
        {
            return null;
        }

        var full = System.IO.Path.GetFullPath(path);
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(full);
        return new OpenFolderRequest(trimmed.Length == 0 ? full : trimmed, view);
    }

    /// <summary>The view a command line names — terminal, files or git — or null.</summary>
    public static OpenFolderView? ParseView(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "terminal" => OpenFolderView.Terminal,
        "files" => OpenFolderView.Files,
        "git" => OpenFolderView.Git,
        _ => null,
    };

    /// <summary>The view's command-line name.</summary>
    public static string ViewName(OpenFolderView view) => view switch
    {
        OpenFolderView.Files => "files",
        OpenFolderView.Git => "git",
        _ => "terminal",
    };

    /// <summary>
    /// The request as a second launch hands it to the running instance: the
    /// view's name, or nothing to ask, then a line break, then the path.
    /// </summary>
    public string Encode() => (View is { } view ? ViewName(view) : string.Empty) + "\n" + Path;

    /// <summary>A request read back from <see cref="Encode"/>, or null when it is malformed.</summary>
    public static OpenFolderRequest? Decode(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var separator = text.IndexOf('\n', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var name = text[..separator];
        OpenFolderView? view = null;
        if (name.Length > 0)
        {
            if (ParseView(name) is not { } parsed)
            {
                return null;
            }

            view = parsed;
        }

        return TryCreate(text[(separator + 1)..], view);
    }

    /// <summary>
    /// The root of the Git repository <paramref name="folder"/> is in, or null:
    /// the nearest folder at or above it holding a <c>.git</c> folder, or the
    /// <c>.git</c> file a worktree or submodule has.
    /// </summary>
    public static string? FindRepositoryRoot(string folder)
    {
        try
        {
            for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
            {
                var marker = System.IO.Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return directory.FullName;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return null;
    }
}
