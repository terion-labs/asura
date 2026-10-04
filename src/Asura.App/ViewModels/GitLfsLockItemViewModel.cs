using Asura.Git;

namespace Asura.App.ViewModels;

public sealed record GitLfsLockItemViewModel(GitLfsLock Item)
{
    public string Path => Item.Path;
    public string OwnerName => Item.OwnerName;
    public string Id => Item.Id;
    public DateTimeOffset? LockedAt => Item.LockedAt;
    public string OwnershipLabel => Item.Ownership switch
    {
        GitLfsLockOwnership.CurrentUser => "Your lock",
        GitLfsLockOwnership.OtherUser => "Another account's lock",
        _ => "Ownership could not be verified",
    };
}
