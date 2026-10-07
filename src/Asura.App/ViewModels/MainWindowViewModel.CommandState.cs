using System.Windows.Input;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// The <see cref="CommandStates"/> facts this view model knows. The window
/// adds what only the view can see, such as what the focused field can do.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly Dictionary<CommandId, Func<GitRuntimePanelViewModel, ICommand>> GitCommands = new()
    {
        [BuiltInCommands.GitRefresh] = git => git.RefreshCommand,
        [BuiltInCommands.GitStageAll] = git => git.StageAllCommand,
        [BuiltInCommands.GitUnstageAll] = git => git.UnstageAllCommand,
        [BuiltInCommands.GitCommit] = git => git.CommitCommand,
        [BuiltInCommands.GitPull] = git => git.PullCommand,
        [BuiltInCommands.GitPush] = git => git.PushCommand,
        [BuiltInCommands.GitSync] = git => git.SyncCommand,
    };

    public IReadOnlyList<KeyValuePair<string, bool>> ActiveCommandState
    {
        get
        {
            List<KeyValuePair<string, bool>> state =
            [
                new(CommandStates.CanReopenClosedTab, CanReopenClosedTab),
                new(CommandStates.IsSidebarVisible, ShowWorkspacesPanel),
                new(CommandStates.IsAgentPanelVisible, IsAgentPanelVisible),
                new(CommandStates.IsPanelZoomed, RuntimeWorkspace?.ActiveTab?.ZoomedPanelId is not null),
            ];
            foreach (var command in GitCommands.Keys)
            {
                state.Add(new(
                    CommandStates.CanExecute(command),
                    ActiveGitCommand(command)?.CanExecute(null) == true));
            }

            return state;
        }
    }

    /// <summary>
    /// The focused Git panel's command behind a Git menu item, or null when
    /// the active panel is not a Git panel.
    /// </summary>
    internal ICommand? ActiveGitCommand(CommandId command) =>
        ActivePanel is GitRuntimePanelViewModel git
            && GitCommands.TryGetValue(command, out var select)
            ? select(git)
            : null;
}
