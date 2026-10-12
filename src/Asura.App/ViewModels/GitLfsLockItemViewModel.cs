using System.Globalization;
using Asura.Git;

namespace Asura.App.ViewModels;

public sealed record GitLfsLockItemViewModel(GitLfsLock Item)
{
    public string Path => Item.Path;
    public string OwnerName => Item.OwnerName;
    public string Id => Item.Id;
    public DateTimeOffset? LockedAt => Item.LockedAt;
    public string LockedAtText => LockedAt is { } lockedAt ? GitDateText.Format(lockedAt, CultureInfo.CurrentCulture) : "";
    /// <summary>
    /// Who holds the lock, whose it is and since when, on one line with
    /// middle dots between, as a Finder row runs its details together.
    /// </summary>
    public string DetailLine => string.Join(" · ", new[] { OwnerName, OwnershipLabel, LockedAtText }.Where(part => part.Length > 0));

    /// <summary>The lock's id, which only the server needs: kept to the row's tooltip.</summary>
    public string IdTip => $"Lock ID {Id}";

    public string OwnershipLabel => Item.Ownership switch
    {
        GitLfsLockOwnership.CurrentUser => "Your lock",
        GitLfsLockOwnership.OtherUser => "Another account’s lock",
        _ => "Owner unknown",
    };
}
