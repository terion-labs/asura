using Avalonia.Controls;

namespace Asura.App;

internal enum FindDirection
{
    Next,
    Previous,
}

/// <summary>
/// A panel that answers Edit ▸ Find: it opens its find bar, or focuses its
/// search or filter field. Panels implement only what they have; a filter
/// field has no matches to step through, so it keeps the default Find Next.
/// </summary>
internal interface IFindTarget
{
    /// <summary>Whether the panel's find bar or search field can be shown now.</summary>
    bool CanBeginFind { get; }

    bool CanFindNext => false;

    /// <summary>
    /// Shows the panel's find bar or focuses its search field. A search text,
    /// from Use Selection for Find, replaces the current query.
    /// </summary>
    Task BeginFindAsync(string? searchText);

    Task FindNextAsync(FindDirection direction) => Task.CompletedTask;
}

/// <summary>
/// The usual find target: one search or filter field.
/// </summary>
internal static class SearchFieldFind
{
    public static bool CanFocus(TextBox? field) =>
        field is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true };

    public static Task FocusAsync(TextBox? field, string? searchText)
    {
        if (field is null || !CanFocus(field))
        {
            return Task.CompletedTask;
        }

        if (searchText is not null)
        {
            field.Text = searchText;
        }

        field.Focus();
        field.SelectAll();
        return Task.CompletedTask;
    }
}
