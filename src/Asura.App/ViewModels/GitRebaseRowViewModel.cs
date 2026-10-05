using Asura.Git;

namespace Asura.App.ViewModels;

public sealed class GitRebaseRowViewModel(GitRebaseEntry entry) : ObservableObject
{
    private GitRebaseAction _action = entry.Action;
    private string _message = entry.Message ?? entry.Subject;

    public string Sha { get; } = entry.Sha;
    public string Subject { get; } = entry.Subject;
    public IReadOnlyList<GitRebaseAction> Actions { get; } = Enum.GetValues<GitRebaseAction>();
    public GitRebaseAction Action { get => _action; set => SetProperty(ref _action, value); }
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public GitRebaseEntry ToEntry() => new(Sha, Subject, Action, Message);
}
