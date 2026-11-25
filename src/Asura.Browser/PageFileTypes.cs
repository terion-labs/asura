using Avalonia.Platform.Storage;

namespace Asura.Browser;

/// <summary>
/// A page's <c>accept</c> list as file picker filters. CEF hands over each
/// entry as an extension (<c>.pdf</c>), a MIME type (<c>image/*</c>) or a
/// described group (<c>Images|.png;.jpg</c>). The page's types come first and
/// All Files after them, so a filter never hides the file someone means to
/// pick; an entry the picker can't express leaves the picker unfiltered.
/// </summary>
internal static class PageFileTypes
{
    // A MIME family is what pickers can match on every platform: macOS by
    // its type identifier, Linux by MIME type, Windows by extension.
    private static readonly Dictionary<string, FilePickerFileType> Families =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/*"] = FilePickerFileTypes.ImageAll,
            ["video/*"] = new("Videos")
            {
                Patterns = ["*.mp4", "*.mov", "*.m4v", "*.webm", "*.mkv", "*.avi"],
                AppleUniformTypeIdentifiers = ["public.movie"],
                MimeTypes = ["video/*"],
            },
            ["audio/*"] = new("Audio")
            {
                Patterns = ["*.mp3", "*.m4a", "*.wav", "*.aac", "*.flac", "*.ogg"],
                AppleUniformTypeIdentifiers = ["public.audio"],
                MimeTypes = ["audio/*"],
            },
            ["text/*"] = FilePickerFileTypes.TextPlain,
            ["application/pdf"] = FilePickerFileTypes.Pdf,
        };

    public static IReadOnlyList<FilePickerFileType>? From(IReadOnlyList<string> accept)
    {
        var patterns = new List<string>();
        var types = new List<FilePickerFileType>();
        foreach (var entry in accept)
        {
            var bar = entry.LastIndexOf('|');
            foreach (var filter in entry[(bar + 1)..].Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (filter.StartsWith('.') && filter.Length > 1)
                {
                    patterns.Add("*" + filter);
                }
                else if (filter.StartsWith("*.", StringComparison.Ordinal) && filter.Length > 2)
                {
                    patterns.Add(filter);
                }
                else if (Families.TryGetValue(filter, out var family))
                {
                    types.Add(family);
                }
                else
                {
                    return null;
                }
            }
        }

        if (patterns.Count > 0)
        {
            types.Insert(0, new FilePickerFileType(string.Join(", ", patterns))
            {
                Patterns = patterns,
            });
        }

        return types.Count == 0 ? null : [.. types, FilePickerFileTypes.All];
    }
}
