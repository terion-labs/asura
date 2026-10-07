namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find focuses the key pattern.
/// </summary>
public sealed partial class RedisRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(KeyPatternBox);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(KeyPatternBox, searchText);
}
