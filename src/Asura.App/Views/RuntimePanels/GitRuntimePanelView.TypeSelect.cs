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
    /// that starts with it, as in any Mac list. The shared type-select buffer
    /// gives every sidebar list the same search.
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
        // The list holds exactly the rows a person can see, top to bottom.
        if (sender is not ListBox { ItemsSource: TreeRows<GitRefTreeNodeViewModel> rows } tree
            || _refTypeSelect.Append(e.Text, DateTimeOffset.UtcNow) is not { } prefix
            || TypeSelectBuffer.FindMatch(rows, node => node.Name, prefix) is not { } match)
        {
            return;
        }

        e.Handled = true;
        TreeList.Select(tree, match);
    }
}
