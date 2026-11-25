namespace Asura.Browser;

/// <summary>
/// Whether the paths someone picked can be handed to a page. The CEF shim
/// carries a file dialog's answer as one newline-separated string and splits
/// it again without escaping, and a Unix file or folder name may contain a
/// newline: <c>/tmp/chosen\n/etc/passwd</c> would reach the page as two
/// files, the second never chosen. A choice with a line break or NUL in any
/// path is refused whole, and the dialog is cancelled.
/// </summary>
internal static class FileDialogPaths
{
    public static bool CanReachThePage(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.All(path => path.Length > 0 && path.AsSpan().IndexOfAny('\n', '\r', '\0') < 0);
    }
}
