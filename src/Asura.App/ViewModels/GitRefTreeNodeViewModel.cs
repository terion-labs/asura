namespace Asura.App.ViewModels;

/// <summary>A slash-delimited ref tree keeps folder expansion through snapshot refreshes.</summary>
public sealed class GitRefTreeNodeViewModel(string name, string path, bool expanded, Action<string, bool> expansionChanged, GitRefItemViewModel? item = null) : ObservableObject
{
    private readonly List<GitRefTreeNodeViewModel> _children = [];
    private bool _isExpanded = expanded;

    public string Name { get; } = name;
    public GitRefItemViewModel? Item { get; } = item;
    public bool IsFolder => Item is null;
    public bool HasRef => Item is not null;
    public string Path { get; } = path;
    public IReadOnlyList<GitRefTreeNodeViewModel> Children => _children;
    internal void Add(GitRefTreeNodeViewModel item) => _children.Add(item);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                expansionChanged(Path, value);
            }
        }
    }
}
