using System.Text;
using System.Text.Json;

namespace Asura.Git;

public sealed partial class GitRepositoryClient
{
    public async ValueTask<GitResult<IReadOnlyList<GitLfsLock>>> ReadLfsLocksAsync(
        GitRepositoryHandle repository, string? remote, CancellationToken cancellationToken)
    {
        var selected = await ResolveLfsRemoteAsync(repository, remote, null, cancellationToken).ConfigureAwait(false);
        if (selected is GitResult<string>.Failure remoteFailure)
        {
            return new GitResult<IReadOnlyList<GitLfsLock>>.Failure(remoteFailure.Error);
        }

        var name = ((GitResult<string>.Success)selected).Value;
        // Git LFS caches server-verified ownership. Read that cache only after
        // a successful verification, so sign-in failures never look like no locks.
        var verified = await MutateAsync(repository, ["lfs", "locks", "--verify", "--remote=" + name],
            cancellationToken, NetworkTimeout).ConfigureAwait(false);
        if (verified is GitResult<GitUnit>.Failure verifyFailure)
        {
            return new GitResult<IReadOnlyList<GitLfsLock>>.Failure(verifyFailure.Error);
        }

        var cached = await ExecuteAsync(repository, ["lfs", "locks", "--verify", "--cached", "--json", "--remote=" + name],
            ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (cached is GitResult<CommandOutput>.Failure cacheFailure)
        {
            return new GitResult<IReadOnlyList<GitLfsLock>>.Failure(cacheFailure.Error);
        }

        try
        {
            if (Value(cached).Truncated)
            {
                throw new FormatException("The lock listing exceeded its output limit.");
            }

            using var json = JsonDocument.Parse(Value(cached).Text);
            List<GitLfsLock> locks = [];
            ParseLfsLockGroup(json.RootElement.GetProperty("ours"), GitLfsLockOwnership.CurrentUser, locks);
            ParseLfsLockGroup(json.RootElement.GetProperty("theirs"), GitLfsLockOwnership.OtherUser, locks);
            return new GitResult<IReadOnlyList<GitLfsLock>>.Success([.. locks.OrderBy(item => item.Path, StringComparer.Ordinal)]);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Failure<IReadOnlyList<GitLfsLock>>(GitErrorCode.InvalidResponse, "Git LFS returned an invalid lock listing.");
        }
    }

    public async ValueTask<GitResult<IReadOnlyList<GitLfsStatusFile>>> ReadLfsStatusAsync(
        GitRepositoryHandle repository, CancellationToken cancellationToken)
    {
        var read = await ExecuteAsync(repository, ["lfs", "status", "--json"], DiffTimeout,
            ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<IReadOnlyList<GitLfsStatusFile>>.Failure(failure.Error);
        }

        try
        {
            if (Value(read).Truncated)
            {
                throw new FormatException("The status listing exceeded its output limit.");
            }

            using var json = JsonDocument.Parse(Value(read).Text);
            List<GitLfsStatusFile> files = [];
            foreach (var file in json.RootElement.GetProperty("files").EnumerateObject())
            {
                files.Add(new(DecodeLfsStatusPath(file.Name), file.Value.GetProperty("status").GetString() ?? "",
                    file.Value.TryGetProperty("from", out var original) && original.GetString() is { } from
                        ? DecodeLfsStatusPath(from) : null));
            }

            return new GitResult<IReadOnlyList<GitLfsStatusFile>>.Success([.. files.OrderBy(item => item.Path, StringComparer.Ordinal)]);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return Failure<IReadOnlyList<GitLfsStatusFile>>(GitErrorCode.InvalidResponse, "Git LFS returned an invalid status listing.");
        }
    }

    private static string DecodeLfsStatusPath(string path)
    {
        // The LFS status scanner embeds Git's C-quoted path inside its JSON key.
        // Octal sequences describe UTF-8 bytes, not Unicode code points.
        if (path.Length < 2 || path[0] != '"' || path[^1] != '"')
        {
            return path;
        }

        var source = Encoding.UTF8.GetBytes(path[1..^1]);
        var decoded = new byte[source.Length];
        var written = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var value = source[index];
            if (value == '\\')
            {
                if (++index >= source.Length)
                {
                    throw new FormatException("The quoted LFS path ends with an incomplete escape.");
                }

                value = source[index];
                if (value is >= (byte)'0' and <= (byte)'7')
                {
                    var octal = value - '0';
                    for (var digits = 1; digits < 3 && index + 1 < source.Length
                        && source[index + 1] is >= (byte)'0' and <= (byte)'7'; digits++)
                    {
                        octal = (octal * 8) + source[++index] - '0';
                    }

                    value = octal <= byte.MaxValue ? (byte)octal : throw new FormatException("The quoted LFS path has an invalid byte.");
                }
                else
                {
                    value = value switch
                    {
                        (byte)'a' => 7,
                        (byte)'b' => 8,
                        (byte)'t' => 9,
                        (byte)'n' => 10,
                        (byte)'v' => 11,
                        (byte)'f' => 12,
                        (byte)'r' => 13,
                        (byte)'"' or (byte)'\\' => value,
                        _ => throw new FormatException("The quoted LFS path has an invalid escape."),
                    };
                }
            }

            decoded[written++] = value;
        }

        return Encoding.UTF8.GetString(decoded.AsSpan(0, written));
    }

    public async ValueTask<GitResult<GitUnit>> ManageLfsLockAsync(
        GitRepositoryHandle repository, GitLfsLockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorktreePath(request.Path);
        var remote = await ResolveLfsRemoteAsync(repository, request.Remote, null, cancellationToken).ConfigureAwait(false);
        if (remote is GitResult<string>.Failure remoteFailure)
        {
            return new GitResult<GitUnit>.Failure(remoteFailure.Error);
        }

        var name = ((GitResult<string>.Success)remote).Value;
        if (request.Action == GitLfsLockAction.Lock)
        {
            if (request.Force)
            {
                return Failure<GitUnit>(GitErrorCode.CommandFailed, "Force applies only to unlocking a reviewed lock.");
            }

            return await MutateAsync(repository, ["lfs", "lock", "--remote=" + name, "--", request.Path],
                cancellationToken, NetworkTimeout).ConfigureAwait(false);
        }

        if (request.Action != GitLfsLockAction.Unlock)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (string.IsNullOrEmpty(request.ExpectedLockId) || request.ExpectedLockId.Contains('\0', StringComparison.Ordinal))
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "Select and review the current lock before unlocking it.");
        }

        var read = await ReadLfsLocksAsync(repository, name, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<IReadOnlyList<GitLfsLock>>.Failure lockFailure)
        {
            return new GitResult<GitUnit>.Failure(lockFailure.Error);
        }

        var selected = ((GitResult<IReadOnlyList<GitLfsLock>>.Success)read).Value.FirstOrDefault(item =>
            string.Equals(item.Path, request.Path, StringComparison.Ordinal)
            && string.Equals(item.Id, request.ExpectedLockId, StringComparison.Ordinal));
        if (selected is null)
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, "The reviewed lock changed. Refresh the locks before unlocking it.");
        }

        if (!request.Force && selected.Ownership != GitLfsLockOwnership.CurrentUser)
        {
            return Failure<GitUnit>(GitErrorCode.CommandFailed, $"The lock belongs to {selected.OwnerName}. Review a force unlock to remove it.");
        }

        IReadOnlyList<string> forceOption = request.Force ? ["--force"] : [];
        return await MutateAsync(repository, ["lfs", "unlock", "--remote=" + name,
            .. forceOption, "--id=" + request.ExpectedLockId],
            cancellationToken, NetworkTimeout).ConfigureAwait(false);
    }

    private static void ParseLfsLockGroup(JsonElement group, GitLfsLockOwnership ownership, List<GitLfsLock> locks)
    {
        if (group.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        foreach (var item in group.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            var path = item.GetProperty("path").GetString();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(path))
            {
                throw new FormatException("A lock lacks its identity or path.");
            }

            var owner = item.TryGetProperty("owner", out var user) && user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("name", out var ownerName) ? ownerName.GetString() : null;
            DateTimeOffset? locked = item.TryGetProperty("locked_at", out var time) && time.TryGetDateTimeOffset(out var value) ? value : null;
            locks.Add(new(id, path, owner ?? "Unknown owner", ownership, locked));
        }
    }

    private async ValueTask<GitResult<string>> ResolveLfsRemoteAsync(
        GitRepositoryHandle repository, string? requested, string? revision, CancellationToken cancellationToken)
    {
        if (requested is not null)
        {
            ValidateRemoteOperand(requested, nameof(requested));
        }

        var read = await ExecuteAsync(repository, ["remote"], ReadTimeout, ReadOutputLimit, cancellationToken).ConfigureAwait(false);
        if (read is GitResult<CommandOutput>.Failure failure)
        {
            return new GitResult<string>.Failure(failure.Error);
        }

        var names = Value(read).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requested is not null)
        {
            return names.Contains(requested, StringComparer.Ordinal) ? new GitResult<string>.Success(requested)
                : Failure<string>(GitErrorCode.CommandFailed, "The selected LFS remote no longer exists.");
        }

        var revisionRemote = names.OrderByDescending(name => name.Length).FirstOrDefault(name => revision is not null
            && (revision.StartsWith("refs/remotes/" + name + "/", StringComparison.Ordinal)
                || revision.StartsWith(name + "/", StringComparison.Ordinal)));
        if (revisionRemote is not null)
        {
            return new GitResult<string>.Success(revisionRemote);
        }

        var configured = await ExecuteAsync(repository, ["config", "--get", "remote.lfsdefault"], ReadTimeout,
            ReadOutputLimit, cancellationToken, acceptExitOne: true).ConfigureAwait(false);
        if (configured is GitResult<CommandOutput>.Failure configuredFailure)
        {
            return new GitResult<string>.Failure(configuredFailure.Error);
        }

        var preferred = Value(configured).Text.TrimEnd('\r', '\n');
        if (preferred.Length == 0)
        {
            var branch = await ExecuteAsync(repository, ["symbolic-ref", "--quiet", "--short", "HEAD"], ReadTimeout,
                ReadOutputLimit, cancellationToken, acceptExitOne: true).ConfigureAwait(false);
            if (branch is GitResult<CommandOutput>.Failure branchFailure)
            {
                return new GitResult<string>.Failure(branchFailure.Error);
            }

            var branchName = Value(branch).Text.TrimEnd('\r', '\n');
            if (branchName.Length > 0)
            {
                var upstream = await ExecuteAsync(repository, ["config", "--get", "branch." + branchName + ".remote"], ReadTimeout,
                    ReadOutputLimit, cancellationToken, acceptExitOne: true).ConfigureAwait(false);
                if (upstream is GitResult<CommandOutput>.Failure upstreamFailure)
                {
                    return new GitResult<string>.Failure(upstreamFailure.Error);
                }

                preferred = Value(upstream).Text.TrimEnd('\r', '\n');
            }
        }

        if (names.Contains(preferred, StringComparer.Ordinal))
        {
            return new GitResult<string>.Success(preferred);
        }

        return names.Length == 1 ? new GitResult<string>.Success(names[0])
            : Failure<string>(GitErrorCode.CommandFailed, names.Length == 0
                ? "No Git remote is configured for LFS. Add a remote before downloading objects or managing locks."
                : "The LFS remote is ambiguous. Select a remote or configure remote.lfsdefault.");
    }
}
