using System.Reflection;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Application.Previews;
using Asura.Core;
using Asura.Git;

namespace Asura.App.Tests;

public sealed class GitPanelWorkflowTests
{
    [Fact]
    public async Task CommitAndPushReportsTheSavedCommitAndRetriesOnlyThePush()
    {
        var (client, recorder) = CreateClient();
        recorder.FailFirstPush = true;
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        panel.CommitSubject = "Saved commit";
        panel.CommitBody = "Body";

        await panel.CommitAndPushAsync();

        Assert.Single(recorder.Inner.Commits);
        Assert.Equal("", panel.CommitSubject);
        Assert.Equal("", panel.CommitBody);
        Assert.Equal("Commit saved; push failed", panel.IssueTitle);
        Assert.Equal(1, recorder.PushCalls);
        await panel.CommitAndPushAsync();
        Assert.Single(recorder.Inner.Commits);
        Assert.Equal(1, recorder.PushCalls);
        await panel.PushAsync();
        Assert.Equal(2, recorder.PushCalls);
        Assert.Single(recorder.Inner.Commits);
    }

    [Fact]
    public async Task FailedCommitPreservesTheComposerAndNeverPushes()
    {
        var (client, recorder) = CreateClient();
        recorder.CommitFailure = new GitError(GitErrorCode.CommandFailed, "Hook rejected the commit", false);
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        panel.CommitSubject = "Retry this";
        panel.CommitBody = "Keep this body";
        panel.Amend = true;

        await panel.CommitAndPushAsync();

        Assert.Equal("Retry this", panel.CommitSubject);
        Assert.Equal("Keep this body", panel.CommitBody);
        Assert.True(panel.Amend);
        Assert.Equal(0, recorder.PushCalls);
        Assert.Empty(recorder.Inner.Commits);
        Assert.Single(recorder.CommitAttempts);
    }

    [Fact]
    public async Task SuccessfulCommitStillPushesWhenItsRefreshFails()
    {
        var (client, recorder) = CreateClient();
        recorder.FailSnapshotsAfterCommit = true;
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        panel.CommitSubject = "Saved before refresh failed";

        await panel.CommitAndPushAsync();

        Assert.Single(recorder.Inner.Commits);
        Assert.Equal(1, recorder.PushCalls);
        Assert.Equal("", panel.CommitSubject);
    }

    [Fact]
    public async Task SwitchingRepositoriesRestoresTheirOwnComposerDrafts()
    {
        var (client, recorder) = CreateClient();
        recorder.RespectOpenPath = true;
        var preferences = new DraftPreferences();
        using var panel = CreatePanel(client, preferences);
        await panel.Initialization;
        await panel.OpenRepositoryAsync("/first");
        panel.CommitSubject = "First subject";
        panel.CommitBody = "First body";
        panel.Amend = true;
        await panel.OpenRepositoryAsync("/second");
        Assert.Equal("", panel.CommitSubject);
        Assert.Equal("", panel.CommitBody);
        Assert.False(panel.Amend);
        panel.CommitSubject = "Second subject";
        panel.CommitBody = "Second body";

        await panel.OpenRepositoryAsync("/first");

        Assert.Equal("First subject", panel.CommitSubject);
        Assert.Equal("First body", panel.CommitBody);
        Assert.True(panel.Amend);
        Assert.Equal(new GitCommitDraft("Second subject", "Second body", false),
            preferences.Drafts[BuiltInConnections.Local.Id.Value + "\n/second"]);
    }

    [Fact]
    public async Task SupersedingAnOpenDoesNotReplaceTheUnreadRepositoryDraftWithAnEmptyComposer()
    {
        var (client, recorder) = CreateClient();
        recorder.RespectOpenPath = true;
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDraft = new TaskCompletionSource<GitCommitDraft?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preserved = new GitCommitDraft("Unread subject", "Unread body", true);
        var preferences = new DraftPreferences
        {
            ReadDraft = (id, _) =>
            {
                if (id.EndsWith("\n/first", StringComparison.Ordinal))
                {
                    readStarted.SetResult();
                    return firstDraft.Task;
                }
                return Task.FromResult<GitCommitDraft?>(new("Second subject", "Second body", false));
            },
        };
        preferences.Drafts[BuiltInConnections.Local.Id.Value + "\n/first"] = preserved;
        using var panel = CreatePanel(client, preferences);
        await panel.Initialization;
        var firstOpen = panel.OpenRepositoryAsync("/first");
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondOpen = panel.OpenRepositoryAsync("/second");
        firstDraft.SetResult(preserved);
        await Task.WhenAll(firstOpen, secondOpen);

        Assert.Equal("/second", panel.RepositoryRoot);
        Assert.Equal("Second subject", panel.CommitSubject);
        Assert.Equal("Second body", panel.CommitBody);
        Assert.False(panel.Amend);
        Assert.Equal(preserved, preferences.Drafts[BuiltInConnections.Local.Id.Value + "\n/first"]);
    }

    [Fact]
    public async Task ComposerEditsMadeDuringDraftLoadingSurviveTheStoredDraftResponse()
    {
        var (client, _) = CreateClient();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storedDraft = new TaskCompletionSource<GitCommitDraft?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new DraftPreferences
        {
            ReadDraft = (_, _) =>
            {
                readStarted.SetResult();
                return storedDraft.Task;
            },
        };
        using var panel = CreatePanel(client, preferences);
        await panel.Initialization;
        var opening = panel.OpenRepositoryAsync("/repo");
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        panel.CommitSubject = "New subject";
        panel.CommitBody = "New body";
        panel.Amend = true;

        storedDraft.SetResult(new("Older subject", "Older body", false));
        await opening;

        Assert.Equal("New subject", panel.CommitSubject);
        Assert.Equal("New body", panel.CommitBody);
        Assert.True(panel.Amend);
        Assert.Equal(new GitCommitDraft("New subject", "New body", true),
            preferences.Drafts[BuiltInConnections.Local.Id.Value + "\n/repo"]);
    }

    [Fact]
    public async Task ClosingThePanelDoesNotCancelTheLatestQueuedDraftWrite()
    {
        var (client, _) = CreateClient();
        var preferences = new DraftPreferences { HoldFirstDraftWrite = true };
        using var panel = CreatePanel(client, preferences);
        await panel.Initialization;
        await panel.OpenRepositoryAsync("/repo");
        panel.CommitSubject = "First";
        await preferences.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        panel.CommitSubject = "Latest";
        panel.CommitBody = "Latest body";
        panel.Amend = true;

        panel.Dispose();
        preferences.ReleaseFirstWrite.SetResult();
        await preferences.LatestDraftWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new GitCommitDraft("Latest", "Latest body", true),
            preferences.Drafts[BuiltInConnections.Local.Id.Value + "\n/repo"]);
    }

    [Fact]
    public async Task EmptyPanelLoadsRecentRepositoriesDuringInitialization()
    {
        var (client, _) = CreateClient();
        var preferences = new DraftPreferences { Recent = ["/recent-one", "/recent-two"] };
        using var panel = CreatePanel(client, preferences);

        await panel.Initialization;

        Assert.False(panel.IsRepositoryOpen);
        Assert.Equal(["/recent-one", "/recent-two"], panel.RecentRepositories);
    }

    [Fact]
    public async Task ClaimedImageFormatUsesTheInjectedDecoderAndRetainsItsOriginalMetadata()
    {
        var (client, recorder) = CreateClient();
        recorder.Images = (_, _, _) => Task.FromResult(new GitImagePair(
            new(ReadOnlyMemory<byte>.Empty, "Before", IsMissing: true), new(new byte[] { 1, 2, 3 }, "After", IsMissing: false)));
        var calls = 0;
        var decoder = new Decoder(async (content, budget, token) =>
        {
            calls++;
            Assert.Null(content.LocalPath);
            Assert.Equal([1, 2, 3], await content.ReadAllBytesAsync(token));
            Assert.Equal(PreviewRasterBudget.MaximumPixels, budget);
            return new(new byte[] { 4, 5 }, 123, 456, "TIFF");
        });
        using var panel = CreatePanel(client, decoder: decoder);
        await panel.OpenRepositoryAsync("/repo");

        panel.SelectedChange = new(new("image.tiff", null, GitChangeKind.Modified, GitChangeArea.Unstaged));
        await panel.DiffLoading;

        Assert.Equal(1, calls);
        Assert.NotNull(panel.DiffImages);
        var after = panel.DiffImages.After;
        Assert.Equal([4, 5], after.Bytes.ToArray());
        Assert.Equal(123, after.PixelWidth);
        Assert.Equal(456, after.PixelHeight);
        Assert.Equal("TIFF", after.FormatName);
        Assert.Equal(3, after.OriginalByteLength);
        Assert.False(after.IsPreviewUnavailable);
    }

    [Fact]
    public async Task SelectingAnotherFileCancelsAnInFlightImageConversion()
    {
        var (client, recorder) = CreateClient();
        recorder.Images = (_, _, _) => Task.FromResult(new GitImagePair(
            new(ReadOnlyMemory<byte>.Empty, "Before", IsMissing: true), new(new byte[] { 1 }, "After", IsMissing: false)));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken conversionToken = default;
        var decoder = new Decoder(async (_, _, token) =>
        {
            conversionToken = token;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        using var panel = CreatePanel(client, decoder: decoder);
        await panel.OpenRepositoryAsync("/repo");
        panel.SelectedChange = new(new("image.tiff", null, GitChangeKind.Modified, GitChangeArea.Unstaged));
        var obsoleteDiff = panel.DiffLoading;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        panel.SelectedChange = panel.UnstagedItems[0];
        await Task.WhenAll(obsoleteDiff, panel.DiffLoading).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(conversionToken.IsCancellationRequested);
        Assert.Null(panel.DiffImages);
        Assert.False(panel.IsDiffLoading);
        Assert.False(panel.HasIssue);
    }

    [Fact]
    public async Task SavingUnrelatedPreferencesKeepsAnInFlightImageDiffAttachedToItsRepository()
    {
        var (client, recorder) = CreateClient();
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var images = new TaskCompletionSource<GitImagePair>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.Images = (_, _, _) =>
        {
            readStarted.SetResult();
            return images.Task;
        };
        using var panel = CreatePanel(client, new DraftPreferences());
        await panel.Initialization;
        await panel.OpenRepositoryAsync("/repo");
        panel.SelectedChange = new(new("image.png", null, GitChangeKind.Modified, GitChangeArea.Unstaged));
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await panel.SaveGitPreferencesAsync(GitPanelPreferenceState.Default with { SubjectGuide = 60 });
        images.SetResult(new(new(ReadOnlyMemory<byte>.Empty, "Before", IsMissing: true),
            new(ReadOnlyMemory<byte>.Empty, "After", IsMissing: true)));
        await panel.DiffLoading;

        Assert.NotNull(panel.DiffImages);
        Assert.Equal("Before", panel.DiffImages.Before.Label);
        Assert.Equal("After", panel.DiffImages.After.Label);
        Assert.Contains("/60 characters", panel.CommitSubjectGuide, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshingAnUnchangedDiffPreservesTheSelectedLineForPartialStaging()
    {
        var (client, recorder) = CreateClient();
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        await panel.DiffLoading;
        var selected = Assert.Single(panel.DiffLines, line => line.IsAdded);
        panel.SelectDiffLines([selected]);

        await panel.RefreshAsync();
        await panel.DiffLoading;

        Assert.Same(selected, Assert.Single(panel.DiffLines, line => line.IsAdded));
        await panel.ApplySelectedDiffAsync(GitPatchAction.Stage);
        var patch = Assert.Single(recorder.PartialPatches);
        Assert.Equal([new GitPatchLineSelection(0, 2)], patch.Lines);
    }

    [Fact]
    public async Task NewHistoryFilterCanCompleteBeforeTheSupersededReadReturns()
    {
        var (client, recorder) = CreateClient();
        var oldRead = new TaskCompletionSource<GitCommitPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken obsoleteToken = default;
        recorder.History = (query, _, token) =>
        {
            if (query.Search == "old")
            {
                obsoleteToken = token;
                return oldRead.Task;
            }
            return Task.FromResult(new GitCommitPage([Commit(new string('b', 40), query.Search ?? "Initial")], 0, false));
        };
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        panel.HistorySearch = "old";
        var superseded = panel.ApplyHistoryFilterAsync();
        Assert.False(superseded.IsCompleted);
        panel.HistorySearch = "new";

        await panel.ApplyHistoryFilterAsync();

        Assert.True(obsoleteToken.IsCancellationRequested);
        Assert.Equal("new", Assert.Single(panel.Commits).Subject);
        oldRead.SetResult(new GitCommitPage([Commit(new string('a', 40), "old")], 0, false));
        await superseded;
        Assert.Equal("new", Assert.Single(panel.Commits).Subject);
    }

    [Fact]
    public async Task PaginationUsesTheAppliedQueryUntilAnotherFilterIsApplied()
    {
        var (client, recorder) = CreateClient();
        recorder.History = (query, offset, _) => Task.FromResult(new GitCommitPage(
            [Commit(new string(offset == 0 ? 'a' : 'b', 40), query.Search ?? "Initial")], offset, offset == 0));
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        panel.HistorySearch = "Applied query";
        await panel.ApplyHistoryFilterAsync();
        panel.HistorySearch = "Not applied";

        panel.LoadMoreCommitsCommand.Execute(null);
        await WaitForAsync(() => panel.Commits.Count == 2);

        Assert.Equal("Applied query", recorder.HistoryRequests[^1].Query.Search);
        Assert.Equal(1, recorder.HistoryRequests[^1].Offset);
        Assert.All(panel.Commits, commit => Assert.Equal("Applied query", commit.Subject));
    }

    [Fact]
    public async Task SelectingAnUnloadedShaDisplaysItsRevisionWithoutKeepingUnrelatedFilters()
    {
        var (client, recorder) = CreateClient();
        var selected = Commit(new string('c', 40), "Unloaded commit");
        recorder.Detail = sha => new GitCommitDetail(selected, "Body", "Test", selected.AuthoredAt, []);
        recorder.History = (query, offset, _) => Task.FromResult(new GitCommitPage(
            query.Revision == selected.Sha ? [selected] : [Commit(new string('a', 40), "Initial")], offset, false));
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        panel.HistorySearch = "Old search";
        panel.HistoryAuthor = "Old author";
        panel.HistoryPath = "old/path.txt";

        await panel.SelectCommitByShaAsync("cccc");

        Assert.Equal(selected.Sha, panel.SelectedCommit?.Commit.Sha);
        Assert.Equal(selected.Sha, panel.HistoryRevision);
        Assert.Equal("", panel.HistorySearch);
        Assert.Equal("", panel.HistoryAuthor);
        Assert.Equal("", panel.HistoryPath);
        Assert.Equal(selected.Sha, recorder.HistoryRequests[^1].Query.Revision);
        Assert.Equal(GitPanelSection.AllCommits, panel.Section);
    }

    [Fact]
    public async Task CancellingALegacyPullCancelsItsCommandAndReleasesThePanel()
    {
        var (client, recorder) = CreateClient();
        recorder.HoldPullUntilCancelled = true;
        using var panel = CreatePanel(client);
        await panel.OpenRepositoryAsync("/repo");
        var pull = panel.PullAsync();
        await recorder.PullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        panel.CancelOperation();
        await pull.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(recorder.PullToken.IsCancellationRequested);
        Assert.False(panel.IsMutating);
        Assert.Contains("cancelled", panel.IssueMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static GitRuntimePanelViewModel CreatePanel(IGitRepositoryClient client, IGitPanelPreferences? preferences = null,
        IImagePreviewDecoder? decoder = null) =>
        new(PanelInstanceId.New(), "Git", client, BuiltInConnections.Local, panelPreferences: preferences, imagePreviewDecoder: decoder);

    private static (IGitRepositoryClient Client, WorkflowClientProxy Recorder) CreateClient()
    {
        var client = DispatchProxy.Create<IGitRepositoryClient, WorkflowClientProxy>();
        return (client, (WorkflowClientProxy)client);
    }

    private static GitCommitItem Commit(string sha, string subject) => new(sha, sha[..8], [], "Test", "test@example.invalid",
        DateTimeOffset.FromUnixTimeSeconds(1_755_500_570), subject, []);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    // Delegate established reads to the shared structural client, overriding
    // only the asynchronous boundaries whose sequencing these tests exercise.
    public class WorkflowClientProxy : DispatchProxy
    {
        internal FakeGitRepositoryClient Inner { get; } = new();
        internal List<GitCommitRequest> CommitAttempts { get; } = [];
        internal List<GitPatchRequest> PartialPatches { get; } = [];
        internal List<(GitHistoryQuery Query, int Offset)> HistoryRequests { get; } = [];
        internal Func<GitHistoryQuery, int, CancellationToken, Task<GitCommitPage>>? History { get; set; }
        internal Func<string, GitCommitDetail>? Detail { get; set; }
        internal Func<GitRepositoryHandle, GitDiffRequest, CancellationToken, Task<GitImagePair>>? Images { get; set; }
        internal GitError? CommitFailure { get; set; }
        internal bool FailFirstPush { get; set; }
        internal bool FailSnapshotsAfterCommit { get; set; }
        internal bool RespectOpenPath { get; set; }
        internal bool HoldPullUntilCancelled { get; set; }
        internal int PushCalls { get; private set; }
        internal CancellationToken PullToken { get; private set; }
        internal TaskCompletionSource PullStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            ArgumentNullException.ThrowIfNull(args);
            switch (targetMethod.Name)
            {
                case nameof(IGitRepositoryClient.OpenRepositoryAsync) when RespectOpenPath:
                    return ValueTask.FromResult<GitResult<GitRepositoryHandle>>(new GitResult<GitRepositoryHandle>.Success(
                        new((ConnectionProfile)args[0]!, (string)args[1]!)));
                case nameof(IGitRepositoryClient.OpenRepositoryWithExecutableAsync) when RespectOpenPath:
                    return ValueTask.FromResult<GitResult<GitRepositoryHandle>>(new GitResult<GitRepositoryHandle>.Success(
                        new((ConnectionProfile)args[0]!, (string)args[1]!) { Executable = (string)args[2]! }));
                case nameof(IGitRepositoryClient.CommitAsync):
                    CommitAttempts.Add((GitCommitRequest)args[1]!);
                    if (CommitFailure is { } failure)
                    {
                        return Failure<GitUnit>(failure);
                    }
                    break;
                case nameof(IGitRepositoryClient.PushAsync):
                    PushCalls++;
                    if (FailFirstPush && PushCalls == 1)
                    {
                        return Failure<GitUnit>(new GitError(GitErrorCode.CommandFailed, "Push rejected", false));
                    }
                    break;
                case nameof(IGitRepositoryClient.ReadSnapshotAsync) when FailSnapshotsAfterCommit && Inner.Commits.Count > 0:
                    return Failure<GitRepositorySnapshot>(new GitError(GitErrorCode.CommandFailed, "Refresh failed", true));
                case nameof(IGitRepositoryClient.ReadHistoryAsync) when History is not null:
                    var query = (GitHistoryQuery)args[1]!;
                    var offset = (int)args[2]!;
                    HistoryRequests.Add((query, offset));
                    return ReadHistoryAsync(query, offset, (CancellationToken)args[4]!);
                case nameof(IGitRepositoryClient.ReadCommitDetailAsync) when Detail is not null:
                    return ValueTask.FromResult<GitResult<GitCommitDetail>>(new GitResult<GitCommitDetail>.Success(Detail((string)args[1]!)));
                case nameof(IGitRepositoryClient.ReadDiffAsync):
                    return ReadDiffAsync((GitRepositoryHandle)args[0]!, (GitDiffRequest)args[1]!, (CancellationToken)args[2]!);
                case nameof(IGitRepositoryClient.ReadImagesAsync) when Images is not null:
                    return ReadImagesAsync((GitRepositoryHandle)args[0]!, (GitDiffRequest)args[1]!, (CancellationToken)args[2]!);
                case nameof(IGitRepositoryClient.ApplyPartialPatchAsync):
                    PartialPatches.Add((GitPatchRequest)args[1]!);
                    return ValueTask.FromResult<GitResult<GitUnit>>(new GitResult<GitUnit>.Success(GitUnit.Value));
                case nameof(IGitRepositoryClient.PullAsync) when HoldPullUntilCancelled:
                    return HoldPullAsync((CancellationToken)args[1]!);
            }
            return targetMethod.Invoke(Inner, args);
        }

        private async ValueTask<GitResult<GitCommitPage>> ReadHistoryAsync(GitHistoryQuery query, int offset, CancellationToken token) =>
            new GitResult<GitCommitPage>.Success(await History!(query, offset, token));

        private async ValueTask<GitResult<GitImagePair>> ReadImagesAsync(GitRepositoryHandle repository, GitDiffRequest request, CancellationToken token) =>
            new GitResult<GitImagePair>.Success(await Images!(repository, request, token));

        private async ValueTask<GitResult<GitDiffDocument>> ReadDiffAsync(GitRepositoryHandle repository, GitDiffRequest request, CancellationToken token)
        {
            var result = (GitResult<GitDiffDocument>.Success)await Inner.ReadDiffAsync(repository, request, token);
            return new GitResult<GitDiffDocument>.Success(result.Value with
            {
                RawPatch = "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1,2 +1,2 @@\n line\n-old\n+new\n",
            });
        }

        private async ValueTask<GitResult<GitUnit>> HoldPullAsync(CancellationToken token)
        {
            PullToken = token;
            PullStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new GitResult<GitUnit>.Success(GitUnit.Value);
        }

        private static ValueTask<GitResult<T>> Failure<T>(GitError error) => ValueTask.FromResult<GitResult<T>>(new GitResult<T>.Failure(error));
    }

    private sealed class DraftPreferences : IGitPanelPreferences
    {
        public Dictionary<string, GitCommitDraft> Drafts { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<string> Recent { get; init; } = [];
        public Func<string, CancellationToken, Task<GitCommitDraft?>>? ReadDraft { get; init; }
        public bool HoldFirstDraftWrite { get; init; }
        public TaskCompletionSource FirstWriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LatestDraftWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? Changed { add { } remove { } }
        public ValueTask<GitPanelPreferenceState> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(GitPanelPreferenceState.Default);
        public ValueTask ApplyAsync(GitPanelPreferenceState state, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<GitCommitDraft?> ReadDraftAsync(string repositoryId, CancellationToken cancellationToken) =>
            ReadDraft is { } read ? new(read(repositoryId, cancellationToken)) : ValueTask.FromResult(Drafts.GetValueOrDefault(repositoryId));
        public ValueTask<IReadOnlyList<string>> ReadRecentRepositoriesAsync(string connectionId, CancellationToken cancellationToken) => ValueTask.FromResult(Recent);
        public ValueTask RecordRepositoryAsync(string connectionId, string path, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask SaveDraftAsync(string repositoryId, GitCommitDraft draft, CancellationToken cancellationToken)
        {
            if (HoldFirstDraftWrite && !FirstWriteStarted.Task.IsCompleted)
            {
                FirstWriteStarted.SetResult();
                await ReleaseFirstWrite.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Drafts[repositoryId] = draft;
            if (draft is { Subject: "Latest", Body: "Latest body", Amend: true })
            {
                LatestDraftWritten.TrySetResult();
            }
        }
    }

    private sealed class Decoder(Func<FilePreviewContent, long, CancellationToken, Task<DecodedImage?>> decode) : IImagePreviewDecoder
    {
        public bool Claims(string fileName) => fileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);
        public ValueTask<DecodedImage?> DecodeAsync(FilePreviewContent content, long maximumPixels, CancellationToken cancellationToken) =>
            new(decode(content, maximumPixels, cancellationToken));
    }
}
