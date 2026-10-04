using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private string _commitSignatureSummary = "";
    public string CommitSignatureSummary { get => _commitSignatureSummary; private set => SetProperty(ref _commitSignatureSummary, value); }
    private async Task LoadCommitSignatureAsync(GitRepositoryHandle repository, string sha, CancellationToken cancellationToken)
    {
        var signature = await _client.ReadSignatureAsync(repository, sha, cancellationToken);
        if (!_disposed && !cancellationToken.IsCancellationRequested && ReferenceEquals(repository, _repository)
            && _comparisonTarget is null && string.Equals(SelectedCommit?.Commit.Sha, sha, StringComparison.Ordinal))
        {
            CommitSignatureSummary = signature is GitResult<GitSignature>.Success success ? success.Value.Summary : "Signature verification unavailable";
        }
    }

    public async Task<IReadOnlyList<GitSigningKey>> ReadSigningKeysAsync()
    {
        if (_repository is not { } repository)
        {
            return [];
        }

        var keys = await _client.ReadSigningKeysAsync(repository, _lifetime.Token);
        return keys is GitResult<IReadOnlyList<GitSigningKey>>.Success success ? success.Value : [];
    }

    public Task ResetIdentityAsync() => RepositoryActionAsync(repository => _client.ResetIdentityAsync(repository, ActionToken));
}
