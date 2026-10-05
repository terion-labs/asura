using Asura.Git;

namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    private string _refFilter = "";
    private readonly Dictionary<string, bool> _refFolderExpansion = new(StringComparer.Ordinal);
    private IReadOnlyList<GitRefTreeNodeViewModel> _localBranchTree = [];
    private IReadOnlyList<GitRefTreeNodeViewModel> _remoteBranchTree = [];
    private IReadOnlyList<GitRefTreeNodeViewModel> _tagTree = [];

    public IReadOnlyList<GitRefTreeNodeViewModel> LocalBranchTree { get => _localBranchTree; private set => SetProperty(ref _localBranchTree, value); }
    public IReadOnlyList<GitRefTreeNodeViewModel> RemoteBranchTree { get => _remoteBranchTree; private set => SetProperty(ref _remoteBranchTree, value); }
    public IReadOnlyList<GitRefTreeNodeViewModel> TagTree { get => _tagTree; private set => SetProperty(ref _tagTree, value); }

    public string? GetReviewedRemoteSha(string remote, string destination)
    {
        if (_snapshot is not { } snapshot || !snapshot.Remotes.Any(item => string.Equals(item.Name, remote, StringComparison.Ordinal)))
        {
            return null;
        }
        var branch = destination.StartsWith("refs/heads/", StringComparison.Ordinal) ? destination[11..] : destination;
        return snapshot.Refs.FirstOrDefault(item => item.Kind == GitRefKind.RemoteBranch
            && string.Equals(item.ShortName, remote + "/" + branch, StringComparison.Ordinal))?.TargetSha ?? "";
    }

    public string RefFilter
    {
        get => _refFilter;
        set
        {
            if (SetProperty(ref _refFilter, value) && _snapshot is { } snapshot)
            {
                PresentRefs(snapshot);
            }
        }
    }

    private void PresentRefs(GitRepositorySnapshot snapshot)
    {
        var defaultRemote = snapshot.Remotes.FirstOrDefault()?.Name;
        var refs = snapshot.Refs.Where(item => item.ShortName.Contains(RefFilter, StringComparison.OrdinalIgnoreCase));
        LocalBranches = [.. refs.Where(item => item.Kind == GitRefKind.LocalBranch)
            .Select(item => new GitRefItemViewModel(item, snapshot.Head.BranchName, defaultRemote))];
        RemoteBranches = [.. refs.Where(item => item.Kind == GitRefKind.RemoteBranch)
            .Select(item => new GitRefItemViewModel(item, snapshot.Head.BranchName, defaultRemote))];
        Tags = [.. refs.Where(item => item.Kind == GitRefKind.Tag)
            .Select(item => new GitRefItemViewModel(item, snapshot.Head.BranchName, defaultRemote))];
        LocalBranchTree = BuildRefFolders(LocalBranches, "branches");
        RemoteBranchTree = BuildRefFolders(RemoteBranches, "remotes");
        TagTree = BuildRefFolders(Tags, "tags");
    }

    private IReadOnlyList<GitRefTreeNodeViewModel> BuildRefFolders(IReadOnlyList<GitRefItemViewModel> refs, string section)
    {
        List<GitRefTreeNodeViewModel> roots = [];
        var folders = new Dictionary<string, GitRefTreeNodeViewModel>(StringComparer.Ordinal);
        foreach (var item in refs.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            GitRefTreeNodeViewModel? parent = null;
            var segments = item.Name.Split('/');
            var path = "";
            for (var index = 0; index < segments.Length - 1; index++)
            {
                path = path.Length == 0 ? segments[index] : path + "/" + segments[index];
                if (!folders.TryGetValue(path, out var folder))
                {
                    var key = RepositoryRoot + "\n" + section + "\n" + path;
                    folder = new GitRefTreeNodeViewModel(segments[index], key,
                        RefFilter.Length > 0 || _refFolderExpansion.GetValueOrDefault(key, true),
                        (folderPath, expanded) => _refFolderExpansion[folderPath] = expanded);
                    folders.Add(path, folder);
                    if (parent is null)
                    {
                        roots.Add(folder);
                    }
                    else
                    {
                        parent.Add(folder);
                    }
                }
                parent = folder;
            }
            var leaf = new GitRefTreeNodeViewModel(segments[^1], item.Name, false, static (_, _) => { }, item);
            if (parent is null)
            {
                roots.Add(leaf);
            }
            else
            {
                parent.Add(leaf);
            }
        }
        return roots;
    }
}
