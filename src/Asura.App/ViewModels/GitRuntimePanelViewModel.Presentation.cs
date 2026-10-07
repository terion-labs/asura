using System.ComponentModel;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// What the panel's chrome derives from repository state: the status mark in
/// the header, the section and detail switchers, and stepping the diff from
/// one changed file to the next.
/// </summary>
public sealed partial class GitRuntimePanelViewModel
{
    // Each derived value names the properties it reads; a change to any of
    // them re-announces the value, so the markup never shows a stale mark.
    private static readonly HashSet<string> StatusSources = new(StringComparer.Ordinal)
    {
        nameof(IsRepositoryOpen),
        nameof(CanMutateRepository),
        nameof(HasIssue),
        nameof(IssueTitle),
        nameof(HasOperation),
        nameof(OperationLabel),
        nameof(HasUnresolvedConflicts),
    };

    private static readonly HashSet<string> DiffFileSources = new(StringComparer.Ordinal)
    {
        nameof(Section),
        nameof(SelectedChange),
        nameof(UnstagedItems),
        nameof(StagedItems),
        nameof(SelectedCommitChange),
        nameof(CommitChanges),
    };

    /// <summary>
    /// The status-read benchmark measures Asura, not the repository, so it is
    /// offered only in builds made for developing Asura.
    /// </summary>
    public static bool IsDiagnosticsBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// The header's status mark. It used to be a green dot whatever the
    /// repository was doing; it now follows the repository: nothing open,
    /// opening or running an operation, an operation waiting on conflicts, a
    /// failure, or ready.
    /// </summary>
    public StatusTone RepositoryStatusTone => RepositoryStatus.Tone;

    /// <summary>What the status mark means, for its tooltip and for VoiceOver.</summary>
    public string RepositoryStatusLabel => RepositoryStatus.Label;

    public string RepositoryStatusAccessibleName => $"Repository status: {RepositoryStatusLabel}";

    private (StatusTone Tone, string Label) RepositoryStatus
    {
        get
        {
            if (!IsRepositoryOpen)
            {
                return _isOpeningRepository ? (StatusTone.Warning, "Opening repository")
                    : HasIssue ? (StatusTone.Danger, IssueTitle ?? "Repository unavailable")
                    : (StatusTone.Neutral, "No repository open");
            }

            if (!CanMutateRepository)
            {
                return (StatusTone.Warning, "Running a Git operation");
            }

            if (HasUnresolvedConflicts)
            {
                return (StatusTone.Warning, "Unresolved conflicts");
            }

            if (HasOperation)
            {
                return (StatusTone.Warning, OperationLabel);
            }

            return HasIssue
                ? (StatusTone.Danger, IssueTitle ?? "Git operation failed")
                : (StatusTone.Success, "Repository ready");
        }
    }

    /// <summary>The section as a segment index, for the section switchers.</summary>
    public int SectionIndex
    {
        get => (int)Section;
        set
        {
            if (Enum.IsDefined((GitPanelSection)value))
            {
                Section = (GitPanelSection)value;
            }
        }
    }

    /// <summary>The commit detail view as a segment index.</summary>
    public int DetailTabIndex
    {
        get => (int)DetailTab;
        set
        {
            if (Enum.IsDefined((GitCommitDetailTab)value))
            {
                DetailTab = (GitCommitDetailTab)value;
            }
        }
    }

    public bool HasRecentRepositories => RecentRepositories.Count > 0;

    /// <summary>
    /// Whether folders for this panel are chosen with the system's Open
    /// panel: they are when Git runs on this machine. A target over SSH is
    /// browsed with Asura's own picker, which can see the remote file system.
    /// </summary>
    public bool UsesSystemFolderPicker => _connection.Endpoint is ConnectionEndpoint.Local;

    /// <summary>Whether a saved Git Flow finish can be resumed or discarded now.</summary>
    public bool CanResolvePendingGitFlowFinish => HasPendingGitFlowFinish && CanMutateRepository;

    /// <summary>
    /// Whether commands that only read — history, statistics, patches, the
    /// hosting site — may run. Unlike <see cref="CanMutateRepository"/>, an
    /// operation in progress does not withdraw them.
    /// </summary>
    public bool CanReadRepository => IsRepositoryOpen && !_isOpeningRepository;

    public bool CanSelectPreviousDiffFile => DiffFileStep(previous: true) is not null;

    public bool CanSelectNextDiffFile => DiffFileStep(previous: false) is not null;

    /// <summary>
    /// Moves the diff to the neighbouring changed file: through the unstaged
    /// list and on into the staged one in Local Changes, through the commit's
    /// files in All Commits. It stops at either end rather than wrapping, so
    /// reaching the last file is something you can tell.
    /// </summary>
    public void SelectDiffFile(bool previous)
    {
        if (DiffFileStep(previous) is not { } target)
        {
            return;
        }

        if (!IsLocalChangesSection)
        {
            SelectedCommitChange = target;
            return;
        }

        // The anchor moves first, as conflict navigation does, then the
        // selection staging acts on follows it.
        SelectedChange = target;
        if (target.IsStaged)
        {
            SelectedStagedItems = [target];
            SelectedUnstagedItems = [];
        }
        else
        {
            SelectedUnstagedItems = [target];
            SelectedStagedItems = [];
        }
    }

    private GitChangeItemViewModel? DiffFileStep(bool previous)
    {
        IReadOnlyList<GitChangeItemViewModel> files;
        GitChangeItemViewModel? current;
        if (IsLocalChangesSection)
        {
            files = [.. UnstagedItems, .. StagedItems];
            current = SelectedChange;
        }
        else
        {
            files = CommitChanges;
            current = SelectedCommitChange;
        }

        if (files.Count == 0)
        {
            return null;
        }

        var index = current is null ? -1 : IndexOfFile(files, current);
        if (index < 0)
        {
            return previous ? files[^1] : files[0];
        }

        var next = index + (previous ? -1 : 1);
        return next >= 0 && next < files.Count ? files[next] : null;
    }

    // A refresh rebuilds the rows, so the selected file is found by what it
    // is rather than by reference.
    private static int IndexOfFile(IReadOnlyList<GitChangeItemViewModel> files, GitChangeItemViewModel file)
    {
        for (var index = 0; index < files.Count; index++)
        {
            if (string.Equals(files[index].Path, file.Path, StringComparison.Ordinal)
                && files[index].IsStaged == file.IsStaged)
            {
                return index;
            }
        }

        return -1;
    }

    private void OnPresentationSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is not { } name)
        {
            return;
        }

        if (StatusSources.Contains(name))
        {
            OnPropertyChanged(nameof(RepositoryStatusTone));
            OnPropertyChanged(nameof(RepositoryStatusLabel));
            OnPropertyChanged(nameof(RepositoryStatusAccessibleName));
        }

        if (DiffFileSources.Contains(name))
        {
            OnPropertyChanged(nameof(CanSelectPreviousDiffFile));
            OnPropertyChanged(nameof(CanSelectNextDiffFile));
        }

        if (name is nameof(HasPendingGitFlowFinish) or nameof(CanMutateRepository))
        {
            OnPropertyChanged(nameof(CanResolvePendingGitFlowFinish));
        }

        if (name is nameof(IsRepositoryOpen) or nameof(CanMutateRepository))
        {
            OnPropertyChanged(nameof(CanReadRepository));
        }

        switch (name)
        {
            case nameof(Section):
                OnPropertyChanged(nameof(SectionIndex));
                break;
            case nameof(DetailTab):
                OnPropertyChanged(nameof(DetailTabIndex));
                break;
            case nameof(RecentRepositories):
                OnPropertyChanged(nameof(HasRecentRepositories));
                break;
        }
    }
}
