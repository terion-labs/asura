using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Opens the local folder <paramref name="path"/> in a new tab of the
    /// workspace in front: a terminal there, the Files panel showing it, or
    /// the Git panel for the repository it is in.
    /// </summary>
    public async Task<bool> OpenLocalFolderAsync(
        string path,
        OpenFolderView view,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var workspace = RuntimeWorkspace;
        if (workspace is null)
        {
            SetError("Open a workspace before opening a folder.");
            return false;
        }

        if (!Directory.Exists(path))
        {
            SetError($"There is no folder at {path}.");
            return false;
        }

        if (view == OpenFolderView.Git && OpenFolderRequest.FindRepositoryRoot(path) is null)
        {
            SetError($"{path} is not in a Git repository.");
            return false;
        }

        var connection = LocalConnection() ?? BuiltInConnections.Local;
        var name = Path.GetFileName(path);
        var title = string.IsNullOrEmpty(name) ? path : name;
        return await AppendRuntimeTabAsync(
            workspace,
            runtime =>
            {
                var tab = new RuntimeTabViewModel(
                    TabInstanceId.New(),
                    title,
                    connection.Name,
                    historySource: new RuntimeHistorySource(connection.Key, connection.Name),
                    agentPolicy: runtime.AgentPolicy);
                RuntimePanelViewModel panel = view switch
                {
                    OpenFolderView.Files => CreateFilePanel(
                        runtime.Id,
                        tab.Id,
                        PanelInstanceId.New(),
                        title,
                        initialProfileId: BuiltInFileProviders.HomeId,
                        initialLocationText: path,
                        connection: connection),
                    // The panel finds the repository's root from any folder in it.
                    OpenFolderView.Git => CreateGitPanel(
                        PanelInstanceId.New(),
                        title,
                        connection,
                        repositoryPath: path,
                        workspaceId: runtime.Id),
                    _ => CreateTerminalPanel(
                        runtime.Id,
                        tab.Id,
                        connection,
                        title,
                        new PanelStartupBehavior(location: path)),
                };
                AddPanelOrDispose(tab, panel);
                return tab;
            },
            "folder tab creation",
            cancellationToken);
    }
}
