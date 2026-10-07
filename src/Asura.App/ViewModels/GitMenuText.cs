namespace Asura.App.ViewModels;

/// <summary>
/// Repository data placed in a menu item header. Avalonia reads '_' in a
/// string header as an access-key marker, so "my_repo" would show and be
/// announced as "myrepo"; doubling each underscore keeps the text literal.
/// </summary>
public static class GitMenuText
{
    public static string Literal(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("_", "__", StringComparison.Ordinal);
}
