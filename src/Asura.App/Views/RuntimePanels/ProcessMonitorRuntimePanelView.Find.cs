namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find focuses the process filter.
/// </summary>
public sealed partial class ProcessMonitorRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(ProcessFilterBox);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(ProcessFilterBox, searchText);
}
