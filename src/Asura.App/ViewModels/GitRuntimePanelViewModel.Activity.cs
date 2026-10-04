using System.Collections.ObjectModel;
using Asura.Git;
using Avalonia.Threading;

namespace Asura.App.ViewModels;

public sealed record GitActivityEntry(DateTimeOffset StartedAt, string Outcome, string Detail);

public sealed partial class GitRuntimePanelViewModel
{
    private CancellationTokenSource? _operationCancellation;
    private DispatcherTimer? _repositoryTimer;
    private bool _automaticRefreshRunning;
    private DateTimeOffset _lastBackgroundFetch;
    private bool _backgroundFetchEnabled;

    public ObservableCollection<GitActivityEntry> Activity { get; } = [];
    public bool BackgroundFetchEnabled { get => _backgroundFetchEnabled; set { if (SetProperty(ref _backgroundFetchEnabled, value)) { SaveViewStyle(); } } }
    private CancellationToken ActionToken => _operationCancellation?.Token ?? _lifetime.Token;

    public void CancelOperation() => _operationCancellation?.Cancel();

    private void StartRepositoryTimer()
    {
        _repositoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _repositoryTimer.Tick += OnRepositoryTimer;
        _repositoryTimer.Start();
    }

    private async void OnRepositoryTimer(object? sender, EventArgs e)
    {
        if (_disposed || _automaticRefreshRunning || !CanMutateRepository)
        {
            return;
        }

        _automaticRefreshRunning = true;
        try
        {
            var refs = _snapshot?.Refs;
            var head = _snapshot?.Head;
            await RefreshRepositoryAsync(preserveIssue: true);
            if (refs is not null && _snapshot is { } snapshot && (!refs.SequenceEqual(snapshot.Refs) || !Equals(head, snapshot.Head)))
            {
                await LoadCommitsAsync(reset: true);
            }

            if (BackgroundFetchEnabled && DateTimeOffset.UtcNow - _lastBackgroundFetch >= TimeSpan.FromMinutes(5) && Remotes.Count > 0)
            {
                _lastBackgroundFetch = DateTimeOffset.UtcNow;
                var issueTitle = IssueTitle;
                var issueMessage = IssueMessage;
                await NetworkAsync(new(GitNetworkAction.Fetch, Remotes[0].Name, Prune: true));
                if (issueMessage is not null && IssueMessage is null)
                {
                    IssueTitle = issueTitle;
                    IssueMessage = issueMessage;
                }
            }
        }
        catch (OperationCanceledException) when (_disposed || _lifetime.IsCancellationRequested) { }
        finally { _automaticRefreshRunning = false; }
    }

    private void RecordActivity(DateTimeOffset started, GitResult<GitUnit> result)
    {
        Activity.Insert(0, result is GitResult<GitUnit>.Failure failure
            ? new(started, failure.Error.Code.ToString(), failure.Error.Message)
            : new(started, "Succeeded", "Repository state refreshed."));
        while (Activity.Count > 100)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }
}
