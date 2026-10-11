using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Git;

namespace Asura.App.Tests;

/// <summary>
/// A Git request the Git layer refuses before running it reads as what the
/// panel couldn't do and what to check, never as the layer's argument text.
/// </summary>
public sealed class GitRequestRefusalTests
{
    [Fact]
    public void An_argument_check_becomes_the_panels_sentence()
    {
        var refusal = new ArgumentException("A governed branch target is invalid.", "target");

        var error = GitRequestRefusal.Error(refusal, GitRequestRefusal.Operation);

        Assert.Equal(GitErrorCode.CommandFailed, error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(GitRequestRefusal.Operation, error.Message);
        Assert.DoesNotContain("governed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_written_for_people_is_kept()
    {
        var refusal = FailureReason.Readable(new ArgumentException("Choose commits to rebase."));

        Assert.Equal(
            "Choose commits to rebase.",
            GitRequestRefusal.Describe(refusal, GitRequestRefusal.RebaseRange));
    }

    [Fact]
    public void Every_fallback_says_what_happened_and_what_to_do_next()
    {
        string[] fallbacks =
        [
            GitRequestRefusal.OpenRepository,
            GitRequestRefusal.HistoryFilter,
            GitRequestRefusal.Operation,
            GitRequestRefusal.RepositoryDestination,
            GitRequestRefusal.Commit,
            GitRequestRefusal.RebaseRange,
            GitRequestRefusal.Comparison,
            GitRequestRefusal.Specialist,
            GitRequestRefusal.FileHistory,
            GitRequestRefusal.RecentRepositories,
            GitRequestRefusal.HostedRepositories,
        ];

        Assert.All(fallbacks, fallback =>
        {
            Assert.StartsWith("Asura", fallback, StringComparison.Ordinal);
            Assert.EndsWith(".", fallback, StringComparison.Ordinal);
            Assert.Contains(". ", fallback, StringComparison.Ordinal);
        });
    }
}
