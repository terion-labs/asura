namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find focuses the branch and tag filter.
/// </summary>
public sealed partial class GitRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(GitRefFilter);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(GitRefFilter, searchText);
}
