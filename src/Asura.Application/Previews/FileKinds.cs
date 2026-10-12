namespace Asura.Application.Previews;

/// <summary>
/// What kind of file a name says it is, in the words Finder uses: "ZIP
/// archive", "Markdown document", "Dynamic library". Used where the host has
/// no description of its own to give (every platform but macOS, and remote
/// files whose type the system has never seen), and by the binary preview,
/// which names the format rather than spelling its extension in capitals.
/// </summary>
public static class FileKinds
{
    /// <summary>A file kind and the symbol that stands for it.</summary>
    public sealed record FileKind(string Name, string Symbol);

    public const string DocumentSymbol = "DocumentOnePage";

    private static readonly Dictionary<string, FileKind> Known = Build();

    /// <summary>
    /// The kind for a file name, or null when the extension is not one this
    /// table knows. Callers word the fallback themselves.
    /// </summary>
    public static FileKind? Find(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var extension = PreviewText.Extension(fileName);
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            extension = "tgz";
        }

        return Known.GetValueOrDefault(extension);
    }

    /// <summary>
    /// The kind for a file name, always: a known kind, else "PNG file"-style
    /// naming for an unknown extension, else "Document" when there is none.
    /// </summary>
    public static string Describe(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (Find(fileName) is { } kind)
        {
            return kind.Name;
        }

        var extension = PreviewText.Extension(fileName);
        return extension.Length == 0
            ? "Document"
            : $"{extension.ToUpperInvariant()} file";
    }

    private static Dictionary<string, FileKind> Build()
    {
        var table = new Dictionary<string, FileKind>(StringComparer.Ordinal);

        void Add(string name, string symbol, params string[] extensions)
        {
            foreach (var extension in extensions)
            {
                table[extension] = new FileKind(name, symbol);
            }
        }

        // Text and data.
        Add("Plain text document", DocumentSymbol, "txt", "text", "log");
        Add("Markdown document", DocumentSymbol, "md", "markdown", "mdown", "mkd");
        Add("CSV document", "DocumentTable", "csv");
        Add("TSV document", "DocumentTable", "tsv");
        Add("JSON document", "DocumentData", "json", "jsonc", "webmanifest");
        Add("XML document", "DocumentData", "xml", "xsd", "xsl", "xslt", "plist", "resx");
        Add("YAML document", "DocumentData", "yaml", "yml");
        Add("TOML document", "DocumentData", "toml");
        Add("HTML document", "Globe", "html", "htm", "xhtml");
        Add("PDF document", DocumentSymbol, "pdf");
        Add("Rich text document", DocumentSymbol, "rtf");

        // Source code.
        Add("C# source", "CodeBlock", "cs");
        Add("C source", "CodeBlock", "c", "h");
        Add("C++ source", "CodeBlock", "cpp", "cc", "cxx", "hpp");
        Add("Swift source", "CodeBlock", "swift");
        Add("Objective-C source", "CodeBlock", "m", "mm");
        Add("Python script", "CodeBlock", "py");
        Add("JavaScript", "CodeBlock", "js", "mjs", "cjs");
        Add("TypeScript", "CodeBlock", "ts", "tsx");
        Add("Shell script", "CodeBlock", "sh", "bash", "zsh");
        Add("Go source", "CodeBlock", "go");
        Add("Rust source", "CodeBlock", "rs");
        Add("Java source", "CodeBlock", "java");
        Add("SQL file", "CodeBlock", "sql");
        Add("CSS style sheet", "CodeBlock", "css");
        Add("MSBuild project", "CodeBlock", "csproj", "props", "targets");

        // Pictures.
        Add("PNG image", "Image", "png");
        Add("JPEG image", "Image", "jpg", "jpeg");
        Add("GIF image", "Image", "gif");
        Add("WebP image", "Image", "webp");
        Add("HEIC image", "Image", "heic", "heif");
        Add("TIFF image", "Image", "tif", "tiff");
        Add("BMP image", "Image", "bmp");
        Add("SVG image", "Image", "svg");
        Add("Icon", "Image", "ico", "icns");
        Add("Photoshop document", "Image", "psd");
        Add("Illustrator document", "Image", "ai");
        Add("Sketch document", "Image", "sketch");
        Add("Figma document", "Image", "fig");
        Add("GIMP image", "Image", "xcf");

        // Archives and disk images.
        Add("ZIP archive", "FolderZip", "zip");
        Add("Tar archive", "FolderZip", "tar");
        Add("Gzip archive", "FolderZip", "gz");
        Add("Gzipped tar archive", "FolderZip", "tgz");
        Add("Bzip2 archive", "FolderZip", "bz2");
        Add("XZ archive", "FolderZip", "xz");
        Add("7-Zip archive", "FolderZip", "7z");
        Add("RAR archive", "FolderZip", "rar");
        Add("Java archive", "FolderZip", "jar");
        Add("Disk image", "Box", "dmg", "iso", "img", "vhd", "vmdk");

        // Audio and video.
        Add("MP3 audio", "MusicNote1", "mp3");
        Add("WAV audio", "MusicNote1", "wav");
        Add("FLAC audio", "MusicNote1", "flac");
        Add("AAC audio", "MusicNote1", "aac", "m4a");
        Add("Ogg audio", "MusicNote1", "ogg", "opus");
        Add("AIFF audio", "MusicNote1", "aiff");
        Add("MPEG-4 movie", "Video", "mp4", "m4v");
        Add("QuickTime movie", "Video", "mov");
        Add("Matroska video", "Video", "mkv");
        Add("AVI video", "Video", "avi");
        Add("WebM video", "Video", "webm");
        Add("Windows Media video", "Video", "wmv");

        // Fonts.
        Add("TrueType font", "TextFont", "ttf");
        Add("OpenType font", "TextFont", "otf");
        Add("Web font", "TextFont", "woff", "woff2", "eot");

        // Compiled code. A library is a library whatever system built it.
        Add("Dynamic library", DocumentSymbol, "dylib", "so", "dll");
        Add("Static library", DocumentSymbol, "a", "lib");
        Add("Object file", DocumentSymbol, "o", "obj");
        Add("Executable", DocumentSymbol, "exe");
        Add("WebAssembly module", DocumentSymbol, "wasm");
        Add("Binary data", DocumentSymbol, "bin", "dat");

        // Databases.
        Add("SQLite database", "Database", "sqlite", "sqlite3", "db");
        Add("Git pack", "Database", "pack", "idx");

        // Office documents.
        Add("Word document", "DocumentBulletList", "doc", "docx");
        Add("Excel spreadsheet", "DocumentBulletList", "xls", "xlsx");
        Add("PowerPoint presentation", "DocumentBulletList", "ppt", "pptx");
        Add("OpenDocument text", "DocumentBulletList", "odt");
        Add("OpenDocument spreadsheet", "DocumentBulletList", "ods");
        return table;
    }
}
