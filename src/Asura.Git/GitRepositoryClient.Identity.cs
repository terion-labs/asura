namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitIdentitySettings>> ReadIdentityAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var values = new List<string>();
        foreach (var key in new[] { "user.name", "user.email", "commit.gpgSign", "user.signingKey" })
        {
            IReadOnlyList<string> arguments = key is "commit.gpgSign" ? ["config", "--bool", "--get", key] : ["config", "--get", key];
            var result = await ExecuteAsync(repository, arguments, ReadTimeout,
                ReadOutputLimit, cancellationToken, acceptExitOne: true).ConfigureAwait(false);
            if (result is GitResult<CommandOutput>.Failure failure)
            {
                return new GitResult<GitIdentitySettings>.Failure(failure.Error);
            }

            values.Add(Value(result).Text.TrimEnd('\r', '\n'));
        }
        return new GitResult<GitIdentitySettings>.Success(new GitIdentitySettings(values[0], values[1],
            string.Equals(values[2], "true", StringComparison.OrdinalIgnoreCase), values[3]));
    }

    public async ValueTask<GitResult<GitUnit>> SaveIdentityAsync(
        GitRepositoryHandle repository, GitIdentitySettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Email))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "Name and email are required.");
        }

        foreach (var pair in new[]
        {
            ("user.name", settings.Name), ("user.email", settings.Email),
            ("commit.gpgSign", settings.SignCommits ? "true" : "false"), ("user.signingKey", settings.SigningKey),
        })
        {
            var result = await MutateAsync(repository, ["config", "--local", pair.Item1, pair.Item2], cancellationToken).ConfigureAwait(false);
            if (result is GitResult<GitUnit>.Failure failure)
            {
                return new GitResult<GitUnit>.Failure(failure.Error with { Message = $"Could not save {pair.Item1}; earlier fields may have been saved. {failure.Error.Message}" });
            }
        }
        return new GitResult<GitUnit>.Success(GitUnit.Value);
    }
}
