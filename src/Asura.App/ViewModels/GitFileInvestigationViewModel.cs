using System.Collections.ObjectModel;
using Asura.Git;

namespace Asura.App.ViewModels;

public sealed class GitFileInvestigationViewModel(IGitRepositoryClient client, GitRepositoryHandle repository,
    string path, bool isDirectory = false) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _selectionCancellation;
    private GitCommitItem? _selectedCommit;
    private string _revision = "HEAD";
    private string _diff = "";
    private string _content = "";
    private string _status = "";
    private bool _hasMore;
    private bool _isBusy;
    private string _selectedPath = path;
    private GitHistoryQuery? _appliedQuery;
    private int? _rangeStart;
    private int? _rangeEnd;

    public string Path { get; } = path;
    public bool IsFile => !isDirectory;
    public ObservableCollection<GitCommitItem> Commits { get; } = [];
    public ObservableCollection<GitBlameLine> Blame { get; } = [];
    public string Revision { get => _revision; set => SetProperty(ref _revision, value); }
    public string Diff { get => _diff; private set => SetProperty(ref _diff, value); }
    public string Content { get => _content; private set => SetProperty(ref _content, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool HasMore { get => _hasMore; private set => SetProperty(ref _hasMore, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public GitCommitItem? SelectedCommit { get => _selectedCommit; set => SetProperty(ref _selectedCommit, value); }

    public async Task LoadAsync(bool reset)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var query = reset ? new GitHistoryQuery(AllRefs: false, Revision: Revision, Path: Path, FollowRenames: _rangeStart is null,
                StartLine: _rangeStart, EndLine: _rangeEnd, PathIsDirectory: isDirectory) : _appliedQuery ?? new(AllRefs: false, Revision: Revision, Path: Path, FollowRenames: true, PathIsDirectory: isDirectory);
            var result = await client.ReadHistoryAsync(repository, query, reset ? 0 : Commits.Count, 100, _lifetime.Token);
            if (result is GitResult<GitCommitPage>.Failure failure)
            {
                Status = failure.Error.Message;
                return;
            }

            var page = ((GitResult<GitCommitPage>.Success)result).Value;
            if (reset)
            {
                Commits.Clear();
                _appliedQuery = query;
            }

            foreach (var commit in page.Commits)
            {
                Commits.Add(commit);
            }

            HasMore = page.HasMore;
            Status = $"{Commits.Count} revisions{(HasMore ? " · more available" : "")}";
            SelectedCommit = reset ? Commits.FirstOrDefault() : SelectedCommit ?? Commits.FirstOrDefault();
        }
        catch (ArgumentException exception)
        {
            Status = exception.Message;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectAsync(GitCommitItem commit)
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = cancellation.Token;
        _selectionCancellation = cancellation;
        try
        {
            SelectedCommit = commit;
            var historical = isDirectory ? new GitResult<GitHistoricalFilePath>.Success(new(Path, null))
                : await client.ReadHistoricalFilePathAsync(repository, _appliedQuery?.Revision ?? Revision, Path, commit.Sha, token);
            token.ThrowIfCancellationRequested();
            var historicalPath = historical is GitResult<GitHistoricalFilePath>.Success successful ? successful.Value : new(Path, null);
            var pathAtRevision = historicalPath.Path;
            _selectedPath = pathAtRevision;

            var diff = await client.ReadDiffAsync(repository, new(GitDiffArea.Commit, pathAtRevision, historicalPath.OriginalPath, CommitSha: commit.Sha), token);
            token.ThrowIfCancellationRequested();
            Diff = diff is GitResult<GitDiffDocument>.Success d ? d.Value.RawPatch : Error(diff);
            if (isDirectory)
            {
                Content = "Directory history. Select a file in the Git tree to inspect its recorded content or blame.";
                Blame.Clear();
                return;
            }
            var blob = await client.ReadBlobAsync(repository, commit.Sha, pathAtRevision, token);
            token.ThrowIfCancellationRequested();
            Content = blob is GitResult<GitBlobSnapshot>.Success b
                ? b.Value.IsBinary ? "Binary file. Use image comparison or save this revision." : b.Value.Text
                : Error(blob);
            var blame = await client.ReadBlameAsync(repository, commit.Sha, pathAtRevision, token);
            token.ThrowIfCancellationRequested();
            Blame.Clear();
            if (blame is GitResult<GitBlameDocument>.Success attribution)
            {
                foreach (var line in attribution.Value.Lines)
                {
                    Blame.Add(line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (ArgumentException exception)
        {
            Status = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, cancellation))
            {
                _selectionCancellation = null;
            }
        }
    }

    public async Task NavigateAsync(string sha)
    {
        var detail = await client.ReadCommitDetailAsync(repository, sha, _lifetime.Token);
        if (detail is GitResult<GitCommitDetail>.Success success)
        {
            await SelectAsync(success.Value.Commit);
        }
        else
        {
            Status = Error(detail);
        }
    }

    public async Task LoadRangeAsync(int start, int end)
    {
        _rangeStart = start;
        _rangeEnd = end;
        Revision = SelectedCommit?.Sha ?? Revision;
        await LoadAsync(reset: true);
    }

    public async Task LoadFullHistoryAsync()
    {
        _rangeStart = null;
        _rangeEnd = null;
        await LoadAsync(reset: true);
    }

    public async Task<ReadOnlyMemory<byte>?> ReadSelectedBytesAsync()
    {
        if (isDirectory || SelectedCommit is not { } commit)
        {
            return null;
        }

        var images = await client.ReadImagesAsync(repository, new(GitDiffArea.Commit, _selectedPath, CommitSha: commit.Sha), _lifetime.Token);
        if (images is GitResult<GitImagePair>.Success success && !success.Value.After.IsMissing)
        {
            return success.Value.After.Bytes;
        }

        Status = Error(images);
        return null;
    }

    private static string Error<T>(GitResult<T> result) => result is GitResult<T>.Failure failure ? failure.Error.Message : "";

    public void Dispose()
    {
        _lifetime.Cancel();
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _lifetime.Dispose();
    }
}
