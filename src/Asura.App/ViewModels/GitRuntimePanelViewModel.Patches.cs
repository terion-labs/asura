using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private GitDiffDocument? _diffDocument;
    private GitDiffRequest? _presentedDiffRequest;
    private IReadOnlyList<GitDiffLineViewModel> _selectedDiffLines = [];

    public bool CanApplyPartialDiff => CanMutateRepository && IsLocalChangesSection
        && !DiffIgnoresWhitespace && !DiffWholeFile && !DiffIsSplit && !DiffIsBinary && !DiffIsTruncated
        && !IsDiffLoading && SelectedChange is { IsConflicted: false } && _diffDocument is not null;

    public void SelectDiffLines(IReadOnlyList<GitDiffLineViewModel> lines) => _selectedDiffLines = lines;

    public async Task ApplySelectedDiffAsync(GitPatchAction action)
    {
        if (!CanApplyPartialDiff || _diffDocument is not { } document || _lastDiffRequest is not { } diff)
        {
            return;
        }

        var headers = _selectedDiffLines.Where(line => line.IsHunkHeader).Select(line => line.HunkIndex).ToHashSet();
        var selection = _selectedDiffLines.Where(line => line.IsAdded || line.IsRemoved)
            .Select(line => new GitPatchLineSelection(line.HunkIndex, line.LineIndex)).ToHashSet();
        foreach (var index in headers)
        {
            for (var line = 0; line < document.Hunks[index].Lines.Count; line++)
            {
                if (document.Hunks[index].Lines[line].Kind != GitDiffLineKind.Context)
                {
                    selection.Add(new GitPatchLineSelection(index, line));
                }
            }
        }
        if (selection.Count == 0)
        {
            PresentFailure(new GitError(GitErrorCode.CommandFailed, "Select changed lines or a hunk header first.", Retryable: false), "Choose lines");
            return;
        }
        await MutateAsync(repository => _client.ApplyPartialPatchAsync(repository,
            new GitPatchRequest(diff, document.RawPatch, [.. selection], action), ActionToken), workingSetOnly: true);
    }
}
