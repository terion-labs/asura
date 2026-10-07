namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find focuses the log search while container logs are open.
/// </summary>
public sealed partial class DockerRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(LogSearchBox);

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(LogSearchBox, searchText);
}
