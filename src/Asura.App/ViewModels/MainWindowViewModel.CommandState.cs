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
        [BuiltInCommands.GitFetch] = git => git.FetchRequestCommand,
        [BuiltInCommands.GitNewBranch] = git => git.NewBranchRequestCommand,
        [BuiltInCommands.GitMerge] = git => git.MergeRequestCommand,
        [BuiltInCommands.GitRebase] = git => git.RebaseRequestCommand,
        [BuiltInCommands.GitStash] = git => git.StashRequestCommand,
    };

    /// <summary>
    /// The window's contexts and facts for the command palette, including
    /// what only the view knows, such as what the focused field can do. The
    /// view model alone knows <see cref="ActiveCommandContexts"/> and
    /// <see cref="ActiveCommandState"/>.
    /// </summary>
    internal Func<CommandInvocation>? CommandInvocationSource { get; set; }

    private CommandInvocation CaptureCommandInvocation() =>
        CommandInvocationSource?.Invoke()
        ?? new CommandInvocation(ActiveCommandContexts, state: ActiveCommandState);

    public IReadOnlyList<KeyValuePair<string, bool>> ActiveCommandState
    {
        get
        {
            List<KeyValuePair<string, bool>> state =
            [
                new(CommandStates.CanReopenClosedTab, CanReopenClosedTab),
                new(CommandStates.HasOtherTabs, RuntimeWorkspace?.Tabs.Count > 1),
                new(CommandStates.IsSidebarVisible, ShowWorkspacesPanel),
                new(CommandStates.IsAgentPanelVisible, IsAgentPanelVisible),
                new(CommandStates.IsPanelZoomed, RuntimeWorkspace?.ActiveTab?.ZoomedPanelId is not null),
            ];
            AddGitState(state, ActivePanel as GitRuntimePanelViewModel);
            return state;
        }
    }

    /// <summary>
    /// The focused Git panel's command behind a Git menu item, or null when
    /// the active panel is not a Git panel.
    /// </summary>
    internal ICommand? ActiveGitCommand(CommandId command) =>
        GitCommand(ActivePanel as GitRuntimePanelViewModel, command);

    internal static bool IsGitCommand(CommandId command) => GitCommands.ContainsKey(command);

    /// <summary>The given Git panel's command behind a Git menu item.</summary>
    internal static ICommand? GitCommand(GitRuntimePanelViewModel? git, CommandId command) =>
        git is not null && GitCommands.TryGetValue(command, out var select)
            ? select(git)
            : null;

    /// <summary>Adds whether each Git menu item can run against <paramref name="git"/>.</summary>
    internal static void AddGitState(
        ICollection<KeyValuePair<string, bool>> state,
        GitRuntimePanelViewModel? git)
    {
        foreach (var command in GitCommands.Keys)
        {
            state.Add(new(
                CommandStates.CanExecute(command),
                GitCommand(git, command)?.CanExecute(null) == true));
        }
    }
}
