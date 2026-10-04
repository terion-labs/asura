using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    public async Task InspectStashAsync(string reference, bool untracked)
    {
        if (_repository is not { } repository)
        {
            return;
        }

        var result = await _client.ReadCommitDetailAsync(repository, untracked ? reference + "^3" : reference, _lifetime.Token);
        if (result is GitResult<GitCommitDetail>.Failure failure)
        {
            PresentFailure(failure.Error, untracked ? "This stash has no new-file revision" : "Could not inspect stash");
            return;
        }

        SelectCommitBySha(((GitResult<GitCommitDetail>.Success)result).Value.Commit.Sha);
    }

    public async Task<string?> ReadStashPatchAsync(string reference)
    {
        if (_repository is not { } repository)
        {
            return null;
        }

        var result = await _client.ReadStashPatchAsync(repository, reference, _lifetime.Token);
        if (result is GitResult<GitTaskOutput>.Failure failure)
        {
            PresentFailure(failure.Error, "Could not export stash");
            return null;
        }

        return ((GitResult<GitTaskOutput>.Success)result).Value.Text;
    }

    public string? CurrentBranchName => _snapshot?.Head.BranchName;

    public Task DownloadLfsImagesAsync() => RepositoryActionAsync(repository =>
        _client.DownloadLfsObjectsAsync(repository, ComposeDiffRequest()?.CommitSha ?? "HEAD", ActionToken));
    public GitFileInvestigationViewModel? CreateFileInvestigation(string path, bool isDirectory = false) => _repository is { } repository
        ? new(_client, repository, path, isDirectory) { Revision = IsLocalChangesSection ? "HEAD" : _comparisonTarget ?? SelectedCommit?.Commit.Sha ?? "HEAD" } : null;

    public async Task<string?> ReadSelectedPatchAsync()
    {
        if (_repository is not { } repository || ComposeDiffRequest() is not { } request)
        {
            return null;
        }

        var result = await _client.ReadDiffAsync(repository, request, _lifetime.Token);
        if (result is GitResult<GitDiffDocument>.Failure failure)
        {
            PresentFailure(failure.Error, "Could not read patch");
            return null;
        }

        var diff = ((GitResult<GitDiffDocument>.Success)result).Value;
        if (diff.IsTruncated)
        {
            PresentFailure(new(GitErrorCode.Unsupported, "This patch exceeds the preview limit. Export the commit range instead.", false), "Patch is incomplete");
            return null;
        }

        return diff.RawPatch;
    }

    public Task ExternalDiffAsync() => RepositoryActionAsync(async repository =>
    {
        if (ComposeDiffRequest() is not { } request)
        {
            return new GitResult<GitUnit>.Success(GitUnit.Value);
        }

        var result = await _client.RunExternalDiffAsync(repository, request, ActionToken);
        return result is GitResult<GitTaskOutput>.Failure failure
            ? new GitResult<GitUnit>.Failure(failure.Error)
            : new GitResult<GitUnit>.Success(GitUnit.Value);
    });
}
