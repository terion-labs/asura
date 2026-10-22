using Avalonia.Media.Fonts;

namespace Asura.DesignQa;

/// <summary>
/// The interface typeface of website artwork: SF Pro Text, read from font
/// files on disk.
/// </summary>
/// <remarks>
/// The shipped app asks macOS for its system UI font, and macOS answers with
/// SF Pro. The headless platform has no such host to ask: Skia's own default
/// on macOS is Helvetica, which made every capture look like a different
/// product. Apple's license allows SF Pro on Apple platforms but not in a
/// repository, so the files are found rather than bundled: a directory named
/// by <c>ASURA_WEBSITE_FONT_DIR</c>, the repository root (where
/// <c>SF-Pro-Text-*.otf</c> is ignored by Git), then the user and system font
/// folders Font Book installs into. Whatever weights are present are loaded;
/// the collection's nearest-weight matching covers the rest.
/// </remarks>
internal sealed class WebsiteUiFontCollection : FontCollectionBase
{
    public const string FamilyName = "SF Pro Text";

    public const string FamilyReference = "fonts:AsuraWebsiteUi#SF Pro Text";

    public static readonly Uri CollectionKey =
        new("fonts:AsuraWebsiteUi", UriKind.Absolute);

    private const string FilePattern = "SF-Pro-Text-*.otf";

    /// <summary>
    /// Loads the faces at once. The font manager is already up when
    /// <c>ConfigureFonts</c> runs, which is the only place this is created.
    /// </summary>
    public WebsiteUiFontCollection()
    {
        var files = FindFontFiles();
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            if (!TryAddGlyphTypeface(stream, out _))
            {
                throw new InvalidOperationException(
                    $"Website export could not load the interface font file {file}.");
            }
        }

        Console.WriteLine(
            $"FONT {FamilyName}: {files.Length} faces from {Path.GetDirectoryName(files[0])}");
    }

    public override Uri Key => CollectionKey;

    private static string[] FindFontFiles()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("ASURA_WEBSITE_FONT_DIR") is { Length: > 0 } configured)
        {
            candidates.Add(configured);
        }

        if (FindRepositoryRoot() is { } repositoryRoot)
        {
            candidates.Add(repositoryRoot);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(home, "Library", "Fonts"));
        candidates.Add("/Library/Fonts");

        foreach (var directory in candidates.Where(Directory.Exists))
        {
            var files = Directory.GetFiles(directory, FilePattern);
            if (files.Length > 0)
            {
                Array.Sort(files, StringComparer.Ordinal);
                return files;
            }
        }

        throw new InvalidOperationException(
            $"Website export needs {FamilyName} ({FilePattern}). Download Apple's SF Pro "
            + "(developer.apple.com/fonts), then install it with Font Book, copy the files "
            + "to the repository root, or point ASURA_WEBSITE_FONT_DIR at them. Searched: "
            + string.Join(", ", candidates));
    }

    /// <summary>The nearest ancestor of the working directory that holds <c>global.json</c>.</summary>
    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
