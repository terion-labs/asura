using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    public async Task<GitRepositoryStatistics?> ReadStatisticsAsync()
    {
        if (_repository is not { } repository)
        {
            return null;
        }

        var result = await _client.ReadStatisticsAsync(repository, _lifetime.Token);
        if (result is GitResult<GitRepositoryStatistics>.Success success)
        {
            return success.Value;
        }

        PresentFailure(((GitResult<GitRepositoryStatistics>.Failure)result).Error, "Couldn't read statistics");
        return null;
    }
    public async Task CreateRepositoryAsync(string path, string branch, string? cloneUrl = null, GitHostingAccount? account = null)
    {
        if (_disposed || IsMutating)
        {
            return;
        }

        IsMutating = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operationCancellation = cancellation;
        try
        {
            var result = cloneUrl is null
                ? await _client.InitializeRepositoryWithExecutableAsync(_connection, path, branch, _gitPreferences.GitExecutable, ActionToken)
                : account is null
                    ? await _client.CloneRepositoryWithExecutableAsync(_connection, cloneUrl, path, _gitPreferences.GitExecutable, ActionToken)
                    : await _client.CloneHostedRepositoryWithExecutableAsync(_connection, account, cloneUrl, path, _gitPreferences.GitExecutable, ActionToken);
            if (result is GitResult<GitRepositoryHandle>.Failure failure)
            {
                if (cloneUrl is not null && failure.Error.Code == GitErrorCode.CredentialStorageFailed)
                {
                    await OpenRepositoryAsync(path);
                    PresentFailure(failure.Error, "Repository cloned; credentials could not be saved");
                    return;
                }
                PresentFailure(failure.Error, cloneUrl is null ? "Couldn't initialize repository" : "Could not clone repository");
                return;
            }
            await OpenRepositoryAsync(((GitResult<GitRepositoryHandle>.Success)result).Value.WorkingTreeRoot);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_disposed)
            {
                PresentFailure(new GitError(GitErrorCode.Cancelled, "Repository creation was canceled. Inspect the destination before retrying.", Retryable: false), "Repository creation canceled");
            }
        }
        catch (ArgumentException exception)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid repository destination");
        }
        finally
        {
            _operationCancellation = null;
            IsMutating = false;
        }
    }

    public Task NetworkAsync(GitNetworkRequest request) => RepositoryActionAsync(repository =>
        _client.NetworkAsync(repository, request, ActionToken));

    public Task ManageWorktreeAsync(GitWorktreeRequest request) => RepositoryActionAsync(repository =>
        _client.ManageWorktreeAsync(repository, request, ActionToken));

    public Task ManageSubmoduleAsync(GitSubmoduleRequest request) => RepositoryActionAsync(repository =>
        _client.ManageSubmoduleAsync(repository, request, ActionToken));

    public Task SaveStashAsync(GitStashRequest request) => RepositoryActionAsync(repository =>
        _client.SaveStashAsync(repository, request, ActionToken));

    /// <summary>
    /// Tasks that only read the repository. They stay available while another
    /// operation runs — looking up a signature or a line's history should not
    /// wait for a fetch to finish — so they skip the mutation gate and the
    /// refresh that follows a change.
    /// </summary>
    public static bool IsReadOnlyTask(GitRepositoryTask task) => task is GitRepositoryTask.Statistics
        or GitRepositoryTask.Blame or GitRepositoryTask.RangeHistory or GitRepositoryTask.Signature
        or GitRepositoryTask.ConflictForecast or GitRepositoryTask.Benchmark or GitRepositoryTask.ExportPatch
        or GitRepositoryTask.LfsStatus;

    public async Task<string?> RunRepositoryTaskAsync(GitRepositoryTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsReadOnlyTask(request.Task))
        {
            return await ReadRepositoryTaskAsync(request);
        }

        string? output = null;
        await RepositoryActionAsync(async repository =>
        {
            var result = await _client.RunRepositoryTaskAsync(repository, request, ActionToken);
            if (result is GitResult<GitTaskOutput>.Failure failure)
            {
                return new GitResult<GitUnit>.Failure(failure.Error);
            }

            output = ((GitResult<GitTaskOutput>.Success)result).Value.Text;
            return new GitResult<GitUnit>.Success(GitUnit.Value);
        });
        return output;
    }

    private async Task<string?> ReadRepositoryTaskAsync(GitRepositoryTaskRequest request)
    {
        if (_disposed || _repository is not { } repository)
        {
            return null;
        }

        var started = DateTimeOffset.UtcNow;
        try
        {
            var result = await _client.RunRepositoryTaskAsync(repository, request, _lifetime.Token);
            if (result is GitResult<GitTaskOutput>.Failure failure)
            {
                RecordActivity(started, new GitResult<GitUnit>.Failure(failure.Error));
                PresentFailure(failure.Error, "Git operation failed");
                return null;
            }

            return ((GitResult<GitTaskOutput>.Success)result).Value.Text;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (ArgumentException exception)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, exception.Message, Retryable: false), "Invalid Git request");
            return null;
        }
    }

    public async Task<GitIdentitySettings?> ReadIdentityAsync()
    {
        if (_repository is not { } repository)
        {
            return null;
        }

        var result = await _client.ReadIdentityAsync(repository, ActionToken);
        if (result is GitResult<GitIdentitySettings>.Success success)
        {
            return success.Value;
        }

        PresentFailure(((GitResult<GitIdentitySettings>.Failure)result).Error, "Couldn't read Git identity");
        return null;
    }

    public Task SaveIdentityAsync(GitIdentitySettings settings) => RepositoryActionAsync(repository =>
        _client.SaveIdentityAsync(repository, settings, ActionToken));
}
