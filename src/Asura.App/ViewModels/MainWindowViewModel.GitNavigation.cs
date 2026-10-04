using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<bool> OpenGitContextAsync(GitRuntimePanelViewModel source, bool terminal, string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var workspace = RuntimeWorkspace;
        if (workspace is null || !source.IsRepositoryOpen || workspace.Tabs.All(tab => !tab.Panels.Contains(source)))
        {
            SetError("Open a Git repository before opening its files or terminal.");
            return false;
        }

        var directory = filePath is { } selectedPath && selectedPath.LastIndexOf('/') is var separator && separator >= 0
            ? selectedPath[..separator] : "";
        var path = source.RepositoryRoot.TrimEnd('/') + (directory.Length == 0 ? "" : "/" + directory);
        FileRuntimePanelViewModel? filePanel = null;
        var opened = await AppendRuntimeTabAsync(workspace, runtime =>
        {
            var tab = new RuntimeTabViewModel(TabInstanceId.New(), terminal ? "Repository terminal" : "Repository files",
                source.ConnectionDisplayName, historySource: new RuntimeHistorySource(source.Connection.Key, source.Connection.Name));
            var panel = terminal
                ? (RuntimePanelViewModel)CreateTerminalPanel(runtime.Id, tab.Id, source.Connection, "Repository terminal", new PanelStartupBehavior(location: source.RepositoryRoot))
                : CreateFilePanel(runtime.Id, tab.Id, PanelInstanceId.New(), "Repository files",
                    initialProfileId: source.Connection.Endpoint is ConnectionEndpoint.Ssh
                        ? ConnectionFileProviderProfiles.Id(source.ConnectionId) : BuiltInFileProviders.HomeId,
                    initialLocationText: path, connection: source.Connection);
            filePanel = panel as FileRuntimePanelViewModel;
            AddPanelOrDispose(tab, panel);
            return tab;
        }, terminal ? "repository terminal creation" : "repository files creation", cancellationToken);
        if (opened && !terminal && filePath is not null && filePanel is not null)
        {
            await filePanel.StartInitialization().WaitAsync(cancellationToken);
            filePanel.Filter = System.IO.Path.GetFileName(filePath);
            await filePanel.SearchCompletion.WaitAsync(cancellationToken);
            var entry = filePanel.Entries.FirstOrDefault(candidate => string.Equals(candidate.Entry.Name, System.IO.Path.GetFileName(filePath), StringComparison.Ordinal));
            if (entry is not null)
            {
                await filePanel.OpenEntryAsync(entry, cancellationToken);
            }
            else
            {
                SetError("The selected file is not present in the current working tree.");
            }
        }
        return opened;
    }
}
