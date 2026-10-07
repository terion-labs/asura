namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find reaches the database workspace's object filter when focus is
/// elsewhere in the window.
/// </summary>
public sealed partial class DatabaseRuntimePanelView : IFindTarget
{
    private IFindTarget Workspace => DatabaseWorkspace;

    bool IFindTarget.CanBeginFind => Workspace.CanBeginFind;

    Task IFindTarget.BeginFindAsync(string? searchText) => Workspace.BeginFindAsync(searchText);
}
