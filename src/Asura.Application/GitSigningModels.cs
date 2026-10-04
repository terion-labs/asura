namespace Asura.Git;

public sealed record GitSignature(string Status, string Signer, string Key, string Fingerprint)
{
    public string Summary => Signer.Length == 0 ? Status : Status + " · " + Signer + " · " + Key;
}
public sealed record GitSigningKey(string Fingerprint, string Identity);

public partial interface IGitRepositoryClient
{
    ValueTask<GitResult<GitSignature>> ReadSignatureAsync(GitRepositoryHandle repository, string revision,
        CancellationToken cancellationToken) => ValueTask.FromResult<GitResult<GitSignature>>(new GitResult<GitSignature>.Success(new("Signature status unavailable", "", "", "")));
    ValueTask<GitResult<IReadOnlyList<GitSigningKey>>> ReadSigningKeysAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => ValueTask.FromResult<GitResult<IReadOnlyList<GitSigningKey>>>(new GitResult<IReadOnlyList<GitSigningKey>>.Success([]));
    ValueTask<GitResult<GitUnit>> ResetIdentityAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken) => Unsupported<GitUnit>();
}
