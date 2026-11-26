using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Controls;

/// <summary>
/// Makes a list of <see cref="TreeRows{TNode}"/> fold the way a tree does:
/// Right opens a folder, Left closes it or moves to the folder a row sits
/// in, and a double click on a folder opens or closes it. A flattened tree
/// is a plain list to the control drawing it, so it does this by hand.
/// </summary>
public static class TreeList
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("IsEnabled", typeof(TreeList));

    static TreeList() =>
        IsEnabledProperty.Changed.AddClassHandler<ListBox>(OnIsEnabledChanged);

    public static bool GetIsEnabled(ListBox element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(ListBox element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    /// <summary>Selects a row and moves focus to it: Up and Down go on from the row with focus.</summary>
    public static void Select(ListBox list, object row)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.SelectedItem = row;
        list.ScrollIntoView(row);
        list.ContainerFromItem(row)?.Focus(NavigationMethod.Directional);
    }

    private static void OnIsEnabledChanged(ListBox list, AvaloniaPropertyChangedEventArgs e)
    {
        // Tunnelling, because the list itself takes Left and Right on the way up.
        list.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        list.DoubleTapped -= OnDoubleTapped;
        if (e.NewValue is true)
        {
            list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            list.DoubleTapped += OnDoubleTapped;
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: TreeRowNode node } list
            || e.KeyModifiers != KeyModifiers.None
            || list.ItemsSource is not ITreeRows rows)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Right when node is { CanExpand: true, IsExpanded: false }:
                node.IsExpanded = true;
                e.Handled = true;
                break;
            case Key.Left when node is { CanExpand: true, IsExpanded: true }:
                node.IsExpanded = false;
                e.Handled = true;
                break;
            case Key.Left when rows.ParentOf(node) is { } parent:
                Select(list, parent);
                e.Handled = true;
                break;
        }
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = e;
        if (sender is ListBox { SelectedItem: TreeRowNode { CanExpand: true } folder })
        {
            folder.IsExpanded = !folder.IsExpanded;
        }
    }
}
