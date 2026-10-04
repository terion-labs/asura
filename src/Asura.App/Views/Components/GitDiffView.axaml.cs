using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// The structured diff presenter, unified or side-by-side. It binds to the
/// Git panel view model it inherits as DataContext; the panel decides which
/// diff is shown and holds the comparison options.
/// </summary>
public sealed partial class GitDiffView : UserControl
{
    public GitDiffView()
    {
        InitializeComponent();
    }

    private GitRuntimePanelViewModel? Panel => DataContext as GitRuntimePanelViewModel;

    private void OnDiffNavigate(object? sender, RoutedEventArgs e)
    {
        if (Panel is not { } panel || sender is not Button { Tag: string direction })
        {
            return;
        }

        panel.NavigateDiff(direction.StartsWith("Previous", StringComparison.Ordinal), direction.EndsWith("Change", StringComparison.Ordinal));
        if (panel.DiffIsSplit && panel.DiffSearchSplitMatch is { } splitMatch)
        {
            SplitLines.ScrollIntoView(splitMatch);
        }
        else if (panel.DiffSearchMatch is { } match)
        {
            UnifiedLines.ScrollIntoView(match);
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
                Title = "Discard selected edits",
                Heading = "Discard these selected lines?",
                Detail = panel.DiffFileName ?? "Selected file",
                ConfirmLabel = "Discard selection",
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

    private void OnToggleWhitespaceClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (Panel is { } panel)
        {
            panel.DiffIgnoresWhitespace = !panel.DiffIgnoresWhitespace;
        }
    }

    private void OnToggleSplitClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (Panel is { } panel)
        {
            panel.DiffIsSplit = !panel.DiffIsSplit;
        }
    }
}
