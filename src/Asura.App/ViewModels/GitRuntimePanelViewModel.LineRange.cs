using System.Globalization;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    /// <summary>A line number typed into a Line History form: a whole number of 1 or more.</summary>
    public static int? ParseLineNumber(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var line) && line >= 1 ? line : null;

    /// <summary>
    /// What is wrong with a Line History range, as the field it is about and a
    /// sentence that says how to fix it; null when Git can trace it. The form
    /// checks this before it closes, so a bad range stays beside its field
    /// instead of failing afterward in an output window. <paramref name="path"/>
    /// is null when the form doesn't ask for one.
    /// </summary>
    public static (string Field, string Message)? ValidateLineRange(string start, string end, string? path = null)
    {
        if (ParseLineNumber(start) is not { } first)
        {
            return ("start", "Enter a line number of 1 or more.");
        }

        if (ParseLineNumber(end) is not { } last)
        {
            return ("end", "Enter a line number of 1 or more.");
        }

        if (last < first)
        {
            return ("end", "Enter a last line at or after the first line.");
        }

        if (path is null)
        {
            return null;
        }

        var trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            return ("path", "Enter the path of a file in the repository.");
        }

        // Git reads the range against a path inside the repository: an
        // absolute path or one that climbs out of it names no tracked file.
        return trimmed.StartsWith('/') || trimmed.StartsWith('\\') || Path.IsPathRooted(trimmed)
            || trimmed.Split('/', '\\').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal))
            ? ("path", "Enter the file’s path inside the repository, such as src/app.cs.")
            : null;
    }

    /// <summary>The <c>-L</c> argument Git takes for lines <paramref name="start"/> to <paramref name="end"/> of <paramref name="path"/>.</summary>
    public static string LineRangeArgument(int start, int end, string path) =>
        string.Create(CultureInfo.InvariantCulture, $"{start},{end}:{path.Trim()}");
}
