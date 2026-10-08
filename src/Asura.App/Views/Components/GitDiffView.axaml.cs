using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Core;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// The structured diff presenter, unified or side-by-side. It binds to the
/// Git panel view model it inherits as DataContext; the panel decides which
/// diff is shown and holds the comparison options.
/// </summary>
public sealed partial class GitDiffView : UserControl, IFindTarget
{
    public GitDiffView()
    {
        InitializeComponent();
        // Tunnelled, so the diff's own keys reach it before the line list
        // turns Option-arrow into a selection change.
        AddHandler(KeyDownEvent, OnDiffKeyDown, RoutingStrategies.Tunnel);
        DiffFindField.AddHandler(KeyDownEvent, OnFindFieldKeyDown, RoutingStrategies.Tunnel);
    }

    private GitRuntimePanelViewModel? Panel => DataContext as GitRuntimePanelViewModel;

    bool IFindTarget.CanBeginFind => SearchFieldFind.CanFocus(DiffFindField);

    bool IFindTarget.CanFindNext => Panel is { DiffSearch.Length: > 0 };

    Task IFindTarget.BeginFindAsync(string? searchText) =>
        SearchFieldFind.FocusAsync(DiffFindField, searchText);

    Task IFindTarget.FindNextAsync(FindDirection direction)
    {
        Navigate(direction == FindDirection.Previous ? "Previous" : "Next");
        return Task.CompletedTask;
    }

    private void OnDiffNavigate(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { Tag: string direction })
        {
            Navigate(direction);
        }
    }

    /// <summary>
    /// Steps the diff: "Previous"/"Next" through find matches,
    /// "PreviousChange"/"NextChange" through hunks, and
    /// "PreviousFile"/"NextFile" through the changed files.
    /// </summary>
    private void Navigate(string direction)
    {
        if (Panel is not { } panel)
        {
            return;
        }

        var previous = direction.StartsWith("Previous", StringComparison.Ordinal);
        if (direction.EndsWith("File", StringComparison.Ordinal))
        {
            panel.SelectDiffFile(previous);
            return;
        }

        panel.NavigateDiff(previous, direction.EndsWith("Change", StringComparison.Ordinal));
        if (panel.DiffIsSplit && panel.DiffSearchSplitMatch is { } splitMatch)
        {
            SplitLines.ScrollIntoView(splitMatch);
        }
        else if (panel.DiffSearchMatch is { } match)
        {
            UnifiedLines.ScrollIntoView(match);
        }
    }

    private void OnDiffKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        // Option-arrow moves by paragraph in a text field; the field keeps it.
        if (e.Handled || e.Source is TextBox)
        {
            return;
        }

        var stroke = ApplicationKeyStrokeMapper.Map(e.Key, e.KeyModifiers);
        var direction = ShortcutText.AreEquivalent(stroke, GitDiffShortcuts.PreviousChange) ? "PreviousChange"
            : ShortcutText.AreEquivalent(stroke, GitDiffShortcuts.NextChange) ? "NextChange"
            : ShortcutText.AreEquivalent(stroke, GitDiffShortcuts.PreviousFile) ? "PreviousFile"
            : ShortcutText.AreEquivalent(stroke, GitDiffShortcuts.NextFile) ? "NextFile"
            : null;
        if (direction is not null)
        {
            Navigate(direction);
            e.Handled = true;
        }
    }

    private void OnFindFieldKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key is not (Key.Enter or Key.Return) || Panel is not { DiffSearch.Length: > 0 })
        {
            return;
        }

        var stroke = ApplicationKeyStrokeMapper.Map(e.Key, e.KeyModifiers);
        if (ShortcutText.AreEquivalent(stroke, GitDiffShortcuts.PreviousMatch))
        {
            Navigate("Previous");
            e.Handled = true;
        }
        else if (ShortcutText.AreEquivalent(stroke, GitDiffShortcuts.NextMatch))
        {
            Navigate("Next");
            e.Handled = true;
        }
    }

    private void OnDiffSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItems: { } items } && Panel is { } panel)
        {
            panel.SelectDiffLines([.. items.OfType<GitDiffLineViewModel>()]);
        }
    }

    private async void OnApplyPartial(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || Panel is not { } panel
            || !Enum.TryParse<GitPatchAction>(tag, out var action))
        {
            return;
        }

        if (action == GitPatchAction.Discard && TopLevel.GetTopLevel(this) is Window window
            && !await new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Heading = "Discard Selected Lines?",
                Detail = $"The selected edits in “{panel.DiffFileName ?? "this file"}” return to their last staged or committed content.",
                Notice = "You can’t undo this action.",
                ConfirmLabel = "Discard Selection",
                Intent = ConfirmationIntent.Destructive,
            }).ShowDialog<bool>(window))
        {
            return;
        }

        await panel.ApplySelectedDiffAsync(action);
    }

    private async void OnHunkAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag, DataContext: GitDiffLineViewModel line } || Panel is not { } panel
            || !Enum.TryParse<GitPatchAction>(tag, out var action) || !panel.DiffLines.Contains(line))
        {
            return;
        }

        panel.SelectDiffLines([GitDiffLineViewModel.Hunk("", line.HunkIndex)]);
        await panel.ApplySelectedDiffAsync(action);
    }
}
