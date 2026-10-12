using System.Collections.ObjectModel;
using Avalonia.Controls;

namespace Asura.App.Controls;

/// <summary>
/// A menu whose items are built as it opens, from whatever they are then.
///
/// A plain <see cref="MenuFlyout"/> declared with no items and filled from its
/// <c>Opening</c> event opens as an empty sliver: a hairline frame drawn under
/// its button. Its item collection makes its backing list on the first add,
/// and the menu's presenter, made before that, is left watching the empty
/// placeholder and never hears about the items. The title bar's Workspaces
/// menu shipped that way and could not be opened at all.
///
/// Here the menu reads from a list that exists from the start, so the
/// presenter always sees what <see cref="ReplaceItems"/> puts in it.
/// </summary>
internal sealed class DynamicMenuFlyout : MenuFlyout
{
    private readonly ObservableCollection<Control> _items = [];

    public DynamicMenuFlyout()
    {
        ItemsSource = _items;
    }

    /// <summary>Swaps the menu's items for <paramref name="items"/>.</summary>
    public void ReplaceItems(IEnumerable<Control> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        foreach (var item in items)
        {
            _items.Add(item);
        }
    }
}
