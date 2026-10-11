using Asura.App.Controls;
using Asura.Git;

namespace Asura.App.ViewModels;

/// <summary>
/// The Git panel's words for a request the Git layer refused before running
/// it. Those refusals are argument checks ("A governed branch target is
/// invalid.", "Value cannot be null. (Parameter 'revision')"), written for
/// developers and offering no next step, so they go to diagnostics and the
/// panel says what to check instead. A refusal the panel wrote for people is
/// shown as written.
/// </summary>
internal static class GitRequestRefusal
{
    public const string OpenRepository =
        "Asura can’t open a repository at that path. Choose the repository’s folder, then try again.";

    public const string HistoryFilter =
        "Asura can’t search the history with that filter. Check the branch, author, path, and text, then try again.";

    public const string Operation =
        "Asura didn’t start that Git action because part of it no longer fits the repository. "
        + "Refresh, check the branches, commits, or files you chose, then try again.";

    public const string RepositoryDestination =
        "Asura can’t create a repository there. Choose an empty folder you can write to, and a valid branch name, then try again.";

    public const string Commit =
        "Asura can’t find that commit. Check its ID, then try again.";

    public const string RebaseRange =
        "Asura can’t plan a rebase for those commits. Choose the commits again, then try again.";

    public const string Comparison =
        "Asura can’t compare those revisions. Choose them again, then try again.";

    public const string Specialist =
        "Asura can’t load this view for the current selection. Refresh, then choose it again.";

    public const string FileHistory =
        "Asura can’t read the history of this file at that revision. Choose another revision, then try again.";

    public const string RecentRepositories =
        "Asura couldn’t read the list of recent repositories. Open a repository by choosing its folder instead.";

    public const string HostedRepositories =
        "Asura couldn’t list the repositories for this account. Check the HTTPS API address and the account, then try again.";

    /// <summary>The sentence for <paramref name="exception"/>; <paramref name="fallback"/> unless it was written for people.</summary>
    public static string Describe(Exception exception, string fallback) =>
        FailureReason.Refusal("git.request.refused", exception, fallback);

    /// <summary>A panel issue for <paramref name="exception"/>.</summary>
    public static GitError Error(Exception exception, string fallback, bool retryable = false) =>
        new(GitErrorCode.CommandFailed, Describe(exception, fallback), Retryable: retryable);
}
