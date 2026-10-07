namespace Asura.App.Views.Components;

/// <summary>
/// Edit ▸ Find focuses the database object filter.
/// </summary>
public sealed partial class DatabaseWorkspaceView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(ObjectFilterBox);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(ObjectFilterBox, searchText);
}
