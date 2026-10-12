using System.ComponentModel;
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
public sealed partial class GitDiffView : UserControl, IFindTarget, IPanelCommandTarget
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
        _ = e;
        if (sender is MenuItem { Tag: CommandId command, DataContext: GitDiffLineViewModel line })
        {
            await ApplyHunksAsync(command, [line]);
        }
    }

    /// <summary>Dims Stage Hunk in a staged file's diff and Unstage Hunk in an unstaged one's.</summary>
    private void OnHunkMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu
            || (menu.DataContext ?? menu.PlacementTarget?.DataContext) is not GitDiffLineViewModel line)
        {
            e.Cancel = true;
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Tag is CommandId command && CanApplyHunks(command, [line]);
        }
    }

    /// <summary>
    /// The menu bar's Stage Hunk and Unstage Hunk act on the hunks that hold
    /// the lines selected in the diff, as the context menu acts on the hunk of
    /// the line it opened on.
    /// </summary>
    bool IPanelCommandTarget.CanExecute(CommandId command) => CanApplyHunks(command, SelectedLines());

    Task IPanelCommandTarget.ExecuteAsync(CommandId command) => ApplyHunksAsync(command, SelectedLines());

    private GitDiffLineViewModel[] SelectedLines() =>
        UnifiedLines.IsEffectivelyVisible && UnifiedLines.SelectedItems is { } items
            ? [.. items.OfType<GitDiffLineViewModel>()]
            : [];

    private bool CanApplyHunks(CommandId command, IReadOnlyCollection<GitDiffLineViewModel> lines) =>
        HunkAction(command) is { } action
        && Panel is { CanApplyPartialDiff: true, SelectedChange: { } change } panel
        && change.IsStaged == (action == GitPatchAction.Unstage)
        && lines.Count > 0
        && lines.All(panel.DiffLines.Contains);

    private async Task ApplyHunksAsync(CommandId command, IReadOnlyCollection<GitDiffLineViewModel> lines)
    {
        if (!CanApplyHunks(command, lines) || HunkAction(command) is not { } action || Panel is not { } panel)
        {
            return;
        }

        panel.SelectDiffLines([.. lines.Select(line => line.HunkIndex).Distinct().Select(hunk => GitDiffLineViewModel.Hunk("", hunk))]);
        await panel.ApplySelectedDiffAsync(action);
    }

    private static GitPatchAction? HunkAction(CommandId command) =>
        command == BuiltInCommands.GitHunkStage ? GitPatchAction.Stage
        : command == BuiltInCommands.GitHunkUnstage ? GitPatchAction.Unstage
        : null;
}
