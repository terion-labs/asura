using Asura.Git;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private GitImagePair? _diffImages;
    private bool _diffWrap;
    private bool _diffWholeFile;
    private string _diffSearch = "";
    private GitDiffLineViewModel? _diffSearchMatch;
    private bool _diffShowsInvisibles;
    private bool _diffHighlightsWords = true;

    public bool DiffShowsInvisibles { get => _diffShowsInvisibles; set { if (SetProperty(ref _diffShowsInvisibles, value)) { SaveViewStyle(); } } }
    public bool DiffHighlightsWords { get => _diffHighlightsWords; set { if (SetProperty(ref _diffHighlightsWords, value)) { SaveViewStyle(); } } }

    private static void PairDiffWords(IReadOnlyList<GitDiffLineViewModel> lines)
    {
        List<GitDiffLineViewModel> removed = [];
        List<GitDiffLineViewModel> added = [];
        foreach (var line in lines)
        {
            if (line.IsRemoved)
            {
                if (added.Count > 0)
                {
                    Pair(removed, added);
                }
                removed.Add(line);
            }
            else if (line.IsAdded)
            {
                added.Add(line);
            }
            else
            {
                Pair(removed, added);
            }
        }
        Pair(removed, added);

        static void Pair(List<GitDiffLineViewModel> removals, List<GitDiffLineViewModel> additions)
        {
            for (var index = 0; index < Math.Min(removals.Count, additions.Count); index++)
            {
                removals[index].PeerText = additions[index].Text;
                additions[index].PeerText = removals[index].Text;
            }
            removals.Clear();
            additions.Clear();
        }
    }

    public GitImagePair? DiffImages
    {
        get => _diffImages;
        private set
        {
            if (!SetProperty(ref _diffImages, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasDiffImages));
            OnPropertyChanged(nameof(ShowsBinaryDiffPlaceholder));
            OnPropertyChanged(nameof(ShowsUnifiedDiff));
            OnPropertyChanged(nameof(ShowsSplitDiff));
        }
    }
    public bool HasDiffImages => DiffImages is not null;
    public bool ShowsUnifiedDiff => !HasDiffImages && !DiffIsSplit;
    public bool ShowsSplitDiff => !HasDiffImages && DiffIsSplit;
    public bool ShowsBinaryDiffPlaceholder => DiffIsBinary && !HasDiffImages;
    public bool DiffWrap { get => _diffWrap; set { if (SetProperty(ref _diffWrap, value)) { OnPropertyChanged(nameof(DiffTextWrapping)); OnPropertyChanged(nameof(DiffHorizontalScrollBarVisibility)); SaveViewStyle(); } } }
    public TextWrapping DiffTextWrapping => DiffWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public ScrollBarVisibility DiffHorizontalScrollBarVisibility => DiffWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    public bool DiffWholeFile { get => _diffWholeFile; set { if (SetProperty(ref _diffWholeFile, value)) { OnPropertyChanged(nameof(CanApplyPartialDiff)); RefreshDiffForSelection(force: true); } } }
    public string DiffSearch { get => _diffSearch; set => SetProperty(ref _diffSearch, value); }
    public GitDiffLineViewModel? DiffSearchMatch
    {
        get => _diffSearchMatch;
        set
        {
            if (SetProperty(ref _diffSearchMatch, value))
            {
                OnPropertyChanged(nameof(DiffSearchSplitMatch));
            }
        }
    }

    public GitDiffSplitRowViewModel? DiffSearchSplitMatch => DiffSearchMatch is { } match
        ? DiffSplitRows.FirstOrDefault(row => match.IsHunkHeader
            ? row.IsHunkHeader && string.Equals(row.HeaderText, match.Text, StringComparison.Ordinal)
            : (match.OldNumberText.Length > 0 && string.Equals(row.LeftNumberText, match.OldNumberText, StringComparison.Ordinal))
                || (match.NewNumberText.Length > 0 && string.Equals(row.RightNumberText, match.NewNumberText, StringComparison.Ordinal))) : null;

    public void NavigateDiff(bool previous, bool changesOnly = false)
    {
        var current = DiffSearchMatch is null ? -1 : DiffLines.ToList().IndexOf(DiffSearchMatch);
        for (var offset = 1; offset <= DiffLines.Count; offset++)
        {
            var index = (current + (previous ? -offset : offset) + DiffLines.Count * 2) % DiffLines.Count;
            var line = DiffLines[index];
            if (changesOnly ? line.IsHunkHeader : line.Text.Contains(DiffSearch, StringComparison.OrdinalIgnoreCase))
            {
                DiffSearchMatch = line;
                return;
            }
        }
    }

    private async Task LoadDiffImagesAsync(GitRepositoryHandle repository, GitDiffRequest request, CancellationToken cancellationToken)
    {
        DiffImages = null;
        var extension = Path.GetExtension(request.Path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".ico")
            && _imagePreviewDecoder?.Claims(request.Path) != true)
        {
            return;
        }

        var result = await _client.ReadImagesAsync(repository, request, cancellationToken);
        if (!cancellationToken.IsCancellationRequested && result is GitResult<GitImagePair>.Success success)
        {
            var before = await DecodeGitImageAsync(success.Value.Before, request.Path, cancellationToken);
            var after = await DecodeGitImageAsync(success.Value.After, request.Path, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(repository, _repository))
            {
                DiffImages = new(before, after);
            }
        }
    }
}
