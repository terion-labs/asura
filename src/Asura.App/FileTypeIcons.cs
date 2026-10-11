using Asura.Application;
using Asura.Application.Previews;
using Avalonia.Media.Imaging;
using FluentIcons.Common;

namespace Asura.App;

/// <summary>
/// The picture a file listing shows beside a name.
///
/// Every file used to get the same <c>&lt;/&gt;</c> glyph, so a CSV, a dylib,
/// a Markdown note and a zip looked alike. On macOS a listing now shows the
/// icon Finder shows — from the file itself for a local item, so an app bundle
/// or a special folder looks like itself, and from its type for a remote one.
/// Elsewhere, or where the system has no answer, a glyph chosen by kind stands
/// in.
/// </summary>
internal static class FileTypeIcons
{
    /// <summary>
    /// The edge of the bitmap asked for, in pixels: a 32-point grid tile on a
    /// Retina display. Smaller places scale it down.
    /// </summary>
    internal const int PixelSize = 64;

    /// <summary>
    /// The system's icon for an entry, or null where there is none to be had.
    /// </summary>
    /// <param name="entry">The listed item.</param>
    /// <param name="localPath">
    /// Where it lives on this machine, when the listing is of this machine.
    /// </param>
    public static Bitmap? NativeIcon(FilePanelEntry entry, string? localPath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return OperatingSystem.IsMacOS()
            ? Views.MacOsFileTypeIcons.For(entry, localPath)
            : null;
    }

    /// <summary>
    /// The kind a listing names a file by, in Finder's words: the system's own
    /// description of the content type on macOS, so it matches Finder's Kind
    /// column, and a table of common kinds elsewhere or for a type the system
    /// has never seen.
    /// </summary>
    public static string KindDescription(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var native = OperatingSystem.IsMacOS()
            ? Views.MacOsFileTypeIcons.KindDescription(PreviewText.Extension(fileName))
            : null;
        return native ?? FileKinds.Describe(fileName);
    }

    /// <summary>The glyph standing in for a system icon.</summary>
    public static Symbol FallbackSymbol(FilePanelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind == FilePanelEntryKind.Directory)
        {
            return Symbol.Folder;
        }

        return PreviewText.Extension(entry.Name) switch
        {
            "png" or "jpg" or "jpeg" or "gif" or "webp" or "heic" or "bmp" or "tif" or "tiff"
                or "svg" or "ico" or "icns" or "psd" => Symbol.Image,
            "mp4" or "mov" or "mkv" or "avi" or "webm" or "m4v" => Symbol.Video,
            "mp3" or "wav" or "flac" or "aac" or "ogg" or "m4a" or "aiff" => Symbol.MusicNote2,
            "zip" or "tar" or "gz" or "tgz" or "bz2" or "xz" or "7z" or "rar" or "zst" => Symbol.FolderZip,
            "pdf" => Symbol.DocumentPdf,
            "csv" or "tsv" or "xls" or "xlsx" or "numbers" or "ods" => Symbol.DocumentTable,
            "db" or "sqlite" or "sqlite3" => Symbol.Database,
            "md" or "markdown" or "txt" or "rtf" or "log" => Symbol.DocumentText,
            "cs" or "fs" or "js" or "ts" or "tsx" or "jsx" or "py" or "rb" or "go" or "rs" or "swift"
                or "c" or "h" or "cpp" or "hpp" or "m" or "mm" or "java" or "kt" or "php" or "sh"
                or "zsh" or "ps1" or "sql" or "json" or "yaml" or "yml" or "toml" or "xml"
                or "html" or "css" or "axaml" or "xaml" or "csproj" => Symbol.Code,
            "dylib" or "so" or "dll" or "exe" or "app" or "bin" or "wasm" => Symbol.AppGeneric,
            "dmg" or "iso" or "img" => Symbol.Box,
            _ => Symbol.Document,
        };
    }
}
