using System.Collections.ObjectModel;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Views;

public sealed partial class GitRebaseDialog : Window
{
    private readonly ObservableCollection<GitRebaseRowViewModel> _rows = [];
    private readonly string _baseRevision = "";
    private GitRebaseRowViewModel? _dragRow;
    private GitRebaseRowViewModel? _dropRow;

    public GitRebaseDialog() => InitializeComponent();

    public GitRebaseDialog(string baseRevision, IReadOnlyList<GitRebaseEntry> entries) : this()
    {
        _baseRevision = baseRevision;
        BaseLabel.Text = "Rebase onto " + baseRevision;
        foreach (var entry in entries)
        {
            _rows.Add(new GitRebaseRowViewModel(entry));
        }

        Rows.ItemsSource = _rows;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: GitRebaseRowViewModel row } control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _dragRow = row;
            _dropRow = null;
            e.Pointer.Capture(Rows);
            e.Handled = true;
        }
    }

    private void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_dragRow is not null && Rows.InputHitTest(e.GetPosition(Rows)) is Control hit)
        {
            _dropRow = hit.DataContext as GitRebaseRowViewModel
                ?? hit.GetVisualAncestors().OfType<Control>().Select(control => control.DataContext).OfType<GitRebaseRowViewModel>().FirstOrDefault();
            if (_dropRow is not null)
            {
                Validation.Text = $"Move “{_dragRow.Subject}” to “{_dropRow.Subject}”. Release to update the plan.";
            }
        }
    }

    private void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragRow is { } dragged && _dropRow is { } target && !ReferenceEquals(dragged, target))
        {
            _rows.Move(_rows.IndexOf(dragged), _rows.IndexOf(target));
            Rows.SelectedItem = dragged;
        }

        _dragRow = null;
        _dropRow = null;
        Validation.Text = "";
        e.Pointer.Capture(null);
    }

    private void OnDragLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragRow = null;
        _dropRow = null;
    }

    private void OnMove(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GitRebaseRowViewModel row, Tag: string direction })
        {
            return;
        }

        var index = _rows.IndexOf(row);
        var target = index + (string.Equals(direction, "Up", StringComparison.Ordinal) ? -1 : 1);
        if (target >= 0 && target < _rows.Count)
        {
            _rows.Move(index, target);
        }
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var retained = false;
        foreach (var row in _rows)
        {
            if ((row.Action is GitRebaseAction.Squash or GitRebaseAction.Fixup && !retained)
                || (row.Action == GitRebaseAction.Reword && string.IsNullOrWhiteSpace(row.Message)))
            {
                Validation.Text = "Squash/fixup need a preceding commit. Reword needs a message.";
                return;
            }
            retained |= row.Action != GitRebaseAction.Drop;
        }
        Close(new GitInteractiveRebaseRequest(_baseRevision, [.. _rows.Select(row => row.ToEntry())],
            AutoStash.IsChecked == true, UpdateRefs.IsChecked == true));
    }
}
