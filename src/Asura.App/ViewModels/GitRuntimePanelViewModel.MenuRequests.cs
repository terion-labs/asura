using System.Windows.Input;

namespace Asura.App.ViewModels;

/// <summary>
/// A Git command that asks something before it runs, so the panel's view
/// answers it with a dialog: Fetch…, New Branch…, Merge…, Rebase… and
/// Stash… in the Git menu.
/// </summary>
public enum GitPanelRequest
{
    Fetch,
    NewBranch,
    Merge,
    Rebase,
    Stash,
}

public sealed partial class GitRuntimePanelViewModel
{
    private readonly Dictionary<GitPanelRequest, AsyncActionCommand> _requestCommands = [];

    /// <summary>
    /// Raised when a menu command needs the view's dialog. The view handles
    /// it the same way as the matching button or row action in the panel.
    /// </summary>
    public event EventHandler<GitPanelRequest>? Requested;

    public ICommand FetchRequestCommand => RequestCommand(GitPanelRequest.Fetch);

    public ICommand NewBranchRequestCommand => RequestCommand(GitPanelRequest.NewBranch);

    public ICommand MergeRequestCommand => RequestCommand(GitPanelRequest.Merge);

    public ICommand RebaseRequestCommand => RequestCommand(GitPanelRequest.Rebase);

    public ICommand StashRequestCommand => RequestCommand(GitPanelRequest.Stash);

    /// <summary>
    /// Every request changes the repository, so each waits for a running
    /// operation as Pull and Push do. Merge and Rebase also need a branch
    /// other than the current one to choose from.
    /// </summary>
    private AsyncActionCommand RequestCommand(GitPanelRequest request)
    {
        if (!_requestCommands.TryGetValue(request, out var command))
        {
            command = new AsyncActionCommand(
                () =>
                {
                    Requested?.Invoke(this, request);
                    return Task.CompletedTask;
                },
                () => !_disposed && CanMutateRepository && (request is not (GitPanelRequest.Merge or GitPanelRequest.Rebase)
                    || LocalBranches.Any(branch => !branch.IsCurrent)));
            _requestCommands[request] = command;
        }

        return command;
    }

    private void RaiseRequestCommands()
    {
        foreach (var command in _requestCommands.Values)
        {
            command.RaiseCanExecuteChanged();
        }
    }
}
