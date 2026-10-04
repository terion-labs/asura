namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<GitSignature>> ReadSignatureAsync(GitRepositoryHandle repository,
        string revision, CancellationToken cancellationToken)
    {
        ValidateRevision(revision);
        var result = await ExecuteAsync(repository, ["show", "--no-patch", "--format=%G?%n%GS%n%GK%n%GF", revision],
            DiffTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (result is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitSignature>.Failure(failure.Error);
        }

        var fields = Value(result).Text.Split('\n');
        var status = fields[0] switch
        {
            "G" => "Signature valid",
            "U" => "Signature valid; key trust unknown",
            "B" => "Signature invalid",
            "X" => "Signature expired",
            "Y" => "Signing key expired",
            "R" => "Signing key revoked",
            "E" => "Signature could not be verified",
            "N" => "Not signed",
            _ => "Signature status unavailable",
        };
        return new GitResult<GitSignature>.Success(new(status, fields.ElementAtOrDefault(1) ?? "",
            fields.ElementAtOrDefault(2) ?? "", fields.ElementAtOrDefault(3) ?? ""));
    }

    public async ValueTask<GitResult<IReadOnlyList<GitSigningKey>>> ReadSigningKeysAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken)
    {
        var result = await RunCustomCommandAsync(repository, new("gpg", ["--list-secret-keys", "--with-colons"]), cancellationToken).ConfigureAwait(false);
        if (result is GitResult<GitTaskOutput>.Failure failure)
        {
            return new GitResult<IReadOnlyList<GitSigningKey>>.Failure(failure.Error);
        }

        var keys = new List<GitSigningKey>();
        var fingerprint = "";
        var primary = false;
        foreach (var line in ((GitResult<GitTaskOutput>.Success)result).Value.Text.Split('\n'))
        {
            var fields = line.Split(':');
            if (fields.Length <= 9)
            {
                continue;
            }

            if (string.Equals(fields[0], "sec", StringComparison.Ordinal))
            {
                primary = true;
                fingerprint = "";
            }
            else if (string.Equals(fields[0], "ssb", StringComparison.Ordinal))
            {
                primary = false;
            }
            else if (primary && string.Equals(fields[0], "fpr", StringComparison.Ordinal))
            {
                fingerprint = fields[9];
            }
            else if (primary && fingerprint.Length > 0 && string.Equals(fields[0], "uid", StringComparison.Ordinal))
            {
                keys.Add(new(fingerprint, fields[9]));
                primary = false;
            }
        }

        return new GitResult<IReadOnlyList<GitSigningKey>>.Success(keys);
    }

    public async ValueTask<GitResult<GitUnit>> ResetIdentityAsync(GitRepositoryHandle repository,
        CancellationToken cancellationToken)
    {
        var configured = await ExecuteAsync(repository, ["config", "--local", "--get-regexp", "^(user\\.(name|email|signingkey)|commit\\.gpgsign)$"],
            ReadTimeout, ReadOutputLimit, cancellationToken, acceptExitOne: true).ConfigureAwait(false);
        if (configured is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<GitUnit>.Failure(failure.Error);
        }

        foreach (var key in Value(configured).Text.Split('\n').Where(line => line.Length > 0).Select(line => line.Split(' ', 2)[0]).Distinct(StringComparer.Ordinal))
        {
            var result = await MutateAsync(repository, ["config", "--local", "--unset-all", key], cancellationToken).ConfigureAwait(false);
            if (result is GitResult<GitUnit>.Failure)
            {
                return result;
            }
        }

        return new GitResult<GitUnit>.Success(GitUnit.Value);
    }
}
