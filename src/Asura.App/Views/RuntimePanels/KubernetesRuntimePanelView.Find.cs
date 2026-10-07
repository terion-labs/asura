namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find focuses the resource filter.
/// </summary>
public sealed partial class KubernetesRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(ResourceFilterBox);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(ResourceFilterBox, searchText);
}
