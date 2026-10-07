using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private readonly TypeSelectBuffer _refTypeSelect = new();

    /// <summary>
    /// Typing a name in the branch, remote or tag list selects the first row
    /// that starts with it, as in any Mac list. A tree has no text search of
    /// its own, so the shared type-select buffer stands in for one.
    /// </summary>
    private void EnableRefTypeSelect()
    {
        foreach (var tree in new[] { GitLocalBranchTree, GitRemoteBranchTree, GitTagTree })
        {
            tree.AddHandler(TextInputEvent, OnRefTreeTextInput, RoutingStrategies.Tunnel);
        }
    }

    private void OnRefTreeTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TreeView { ItemsSource: IEnumerable<GitRefTreeNodeViewModel> roots } tree
            || _refTypeSelect.Append(e.Text, DateTimeOffset.UtcNow) is not { } prefix
            || TypeSelectBuffer.FindMatch(VisibleRefNodes(roots), node => node.Name, prefix) is not { } match)
        {
            return;
        }

        e.Handled = true;
        tree.SelectedItem = match;
        tree.TreeContainerFromItem(match)?.BringIntoView();
    }

    /// <summary>The rows a person can see, top to bottom: closed folders hide theirs.</summary>
    internal static IEnumerable<GitRefTreeNodeViewModel> VisibleRefNodes(IEnumerable<GitRefTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.IsFolder && node.IsExpanded)
            {
                foreach (var child in VisibleRefNodes(node.Children))
                {
                    yield return child;
                }
            }
        }
    }
}
