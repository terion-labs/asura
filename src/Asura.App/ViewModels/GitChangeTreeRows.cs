using System.Collections.ObjectModel;

namespace Asura.App.ViewModels;

/// <summary>
/// A working-set section's change tree as the rows it shows. The tree is
/// folded from the flat change list on every snapshot and reconciled in
/// place, so kept folders keep their expansion and their rows.
/// </summary>
public sealed class GitChangeTreeRows(ObservableCollection<GitChangeTreeNodeViewModel> roots)
    : TreeRows<GitChangeTreeNodeViewModel>(roots)
{
    /// <summary>Folds the flat change list into the tree, then re-lists the rows.</summary>
    public void Reconcile(IReadOnlyList<GitChangeItemViewModel> items)
    {
        GitChangeTreeNodeViewModel.Reconcile(roots, items);
        Refresh();
    }
}
