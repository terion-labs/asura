namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find focuses the file filter.
/// </summary>
public sealed partial class FileRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(FileFilterBox);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(FileFilterBox, searchText);
}
