using Asura.Core;

namespace Asura.Git;

public enum GitHostingProvider { GitHub, GitLab }
public sealed record GitHostingAccount(GitHostingProvider Provider, Uri ApiBase, string Username, SecretRef Reference)
{
    public string DisplayName => $"{Provider} · {Username} @ {ApiBase.Host}";
}
public sealed record GitHostedRepository(string Name, string CloneUrl, string SshUrl, string WebUrl, bool IsPrivate);
public sealed record GitHostedRepositoryPage(IReadOnlyList<GitHostedRepository> Repositories, int Page, bool HasMore)
{
    public GitHostingAccount? Account { get; init; }
}
public sealed record GitHostingCloneSelection(GitHostedRepository Repository, GitHostingAccount? Account);

public partial interface IGitRepositoryClient
{
    ValueTask<GitResult<GitRepositoryHandle>> CloneHostedRepositoryWithExecutableAsync(ConnectionProfile connection,
        GitHostingAccount account, string url, string path, string executable, CancellationToken cancellationToken) =>
        CloneRepositoryWithExecutableAsync(connection, url, path, executable, cancellationToken);
    ValueTask<GitResult<IReadOnlyList<GitHostingAccount>>> ReadHostingAccountsAsync(ConnectionProfile connection,
        CancellationToken cancellationToken) => Unsupported<IReadOnlyList<GitHostingAccount>>();

    ValueTask<GitResult<GitHostedRepositoryPage>> ReadHostedRepositoriesAsync(ConnectionProfile connection,
        GitHostingProvider provider, Uri apiBase, GitHostingAccount? account, int page, bool starred,
        CancellationToken cancellationToken) => Unsupported<GitHostedRepositoryPage>();

    ValueTask<GitResult<GitUnit>> RemoveHostingAccountAsync(ConnectionProfile connection, GitHostingAccount account,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();
}
