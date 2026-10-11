using Asura.Git;

namespace Asura.App.ViewModels;

public sealed class GitRebaseRowViewModel(GitRebaseEntry entry) : ObservableObject
{
    private GitRebaseAction _action = entry.Action;
    private string _message = entry.Message ?? entry.Subject;
    private bool _canMoveEarlier;
    private bool _canMoveLater;

    public string Sha { get; } = entry.Sha;
    public string Subject { get; } = entry.Subject;
    public IReadOnlyList<GitRebaseAction> Actions { get; } = Enum.GetValues<GitRebaseAction>();

    public GitRebaseAction Action
    {
        get => _action;
        set
        {
            if (SetProperty(ref _action, value))
            {
                OnPropertyChanged(nameof(UsesMessage));
            }
        }
    }

    public string Message { get => _message; set => SetProperty(ref _message, value); }

    /// <summary>
    /// Whether the step takes the message typed here: only reword does, so
    /// only reword shows the message editor. Squash keeps both commits'
    /// messages together without asking (the rebase runs without an editor),
    /// and pick, edit, fixup and drop keep or discard the message, so an
    /// editor on those steps would offer an edit the rebase ignores.
    /// </summary>
    public bool UsesMessage => Action is GitRebaseAction.Reword;

    /// <summary>Whether a step comes before this one, so Move Earlier has somewhere to go.</summary>
    public bool CanMoveEarlier { get => _canMoveEarlier; private set => SetProperty(ref _canMoveEarlier, value); }

    /// <summary>Whether a step comes after this one, so Move Later has somewhere to go.</summary>
    public bool CanMoveLater { get => _canMoveLater; private set => SetProperty(ref _canMoveLater, value); }

    public string MoveEarlierLabel => $"Move “{Subject}” earlier";

    public string MoveLaterLabel => $"Move “{Subject}” later";

    /// <summary>Records where the step stands in a plan of <paramref name="count"/> steps.</summary>
    public void SetPosition(int index, int count)
    {
        CanMoveEarlier = index > 0;
        CanMoveLater = index < count - 1;
    }

    public GitRebaseEntry ToEntry() => new(Sha, Subject, Action, Message);
}
