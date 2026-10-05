using System.Data.Common;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private readonly SemaphoreSlim _draftWrites = new(1, 1);
    private bool _applyingDraft;
    private bool _draftReady;
    private int _draftEditVersion;
    private IReadOnlyList<string> _recentRepositories = [];

    public IReadOnlyList<string> RecentRepositories { get => _recentRepositories; private set => SetProperty(ref _recentRepositories, value); }
    public IReadOnlyList<string> RecentCommitMessages => [.. Commits.Select(commit => commit.Subject).Distinct(StringComparer.Ordinal).Take(20)];

    private async Task InitializePanelAsync(string initialPath)
    {
        await PresentStoredViewStyleAsync();
        await LoadRecentRepositoriesAsync();
        if (!_disposed && initialPath.Length > 0)
        {
            await OpenRepositoryAsync(initialPath);
        }
    }

    private async Task LoadRecentRepositoriesAsync()
    {
        if (_panelPreferences is not { } preferences)
        {
            return;
        }

        try
        {
            RecentRepositories = await preferences.ReadRecentRepositoriesAsync(_connection.Id.Value, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DbException)
        {
            PresentFailure(new Git.GitError(Git.GitErrorCode.CommandFailed, exception.Message, Retryable: true), "Recent repositories unavailable");
        }
    }

    private async Task LoadRepositoryDraftAsync(CancellationToken cancellationToken)
    {
        if (_repository is not { } repository)
        {
            return;
        }

        try
        {
            _draftReady = false;
            _applyingDraft = true;
            try
            {
                CommitSubject = "";
                CommitBody = "";
                Amend = false;
            }
            finally { _applyingDraft = false; }
            if (_panelPreferences is not { } preferences)
            {
                _draftReady = true;
                return;
            }
            var editVersion = _draftEditVersion;
            var draft = await preferences.ReadDraftAsync(_connection.Id.Value + "\n" + repository.WorkingTreeRoot, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || !ReferenceEquals(repository, _repository))
            {
                return;
            }
            if (editVersion == _draftEditVersion)
            {
                _applyingDraft = true;
                try
                {
                    CommitSubject = draft?.Subject ?? "";
                    CommitBody = draft?.Body ?? "";
                    Amend = draft?.Amend ?? false;
                }
                finally { _applyingDraft = false; }
                _draftReady = true;
            }
            await preferences.RecordRepositoryAsync(_connection.Id.Value, repository.WorkingTreeRoot, cancellationToken);
            var recent = await preferences.ReadRecentRepositoriesAsync(_connection.Id.Value, cancellationToken);
            if (!_disposed && !cancellationToken.IsCancellationRequested && ReferenceEquals(repository, _repository))
            {
                RecentRepositories = recent;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DbException)
        {
            PresentFailure(new Git.GitError(Git.GitErrorCode.CommandFailed, "The commit draft could not be restored. " + exception.Message, Retryable: true), "Draft storage unavailable");
        }
    }

    private async Task PersistDraftAsync()
    {
        if (!_draftReady || _applyingDraft || _disposed || _panelPreferences is not { } preferences || _repository is not { } repository)
        {
            return;
        }

        var id = _connection.Id.Value + "\n" + repository.WorkingTreeRoot;
        var draft = new GitCommitDraft(CommitSubject, CommitBody, Amend);
        try
        {
            // Draft writes already accepted from the composer must finish when
            // its panel closes. Cancelling reads and Git work must not lose them.
            await _draftWrites.WaitAsync(CancellationToken.None);
            try { await preferences.SaveDraftAsync(id, draft, CancellationToken.None); }
            finally { _draftWrites.Release(); }
        }
        catch (OperationCanceledException) when (_disposed || _lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DbException)
        {
            if (!_disposed)
            {
                PresentFailure(new Git.GitError(Git.GitErrorCode.CommandFailed, "Your draft remains in this panel but could not be saved. " + exception.Message, Retryable: true), "Draft storage unavailable");
            }
        }
    }
}
