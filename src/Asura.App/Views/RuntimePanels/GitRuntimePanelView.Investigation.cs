using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private async void OnOpenGitContext(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && OwnerWindow?.DataContext is MainWindowViewModel main
            && sender is MenuItem { Tag: string action })
        {
            await main.OpenGitContextAsync(viewModel, string.Equals(action, "Terminal", StringComparison.Ordinal),
                string.Equals(action, "File", StringComparison.Ordinal)
                    ? viewModel.IsLocalChangesSection ? viewModel.SelectedChange?.Path : viewModel.SelectedCommitChange?.Path ?? viewModel.SelectedTreeNode?.Path
                    : null);
        }
    }

    private async void OnIgnoreSelectedFile(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem menu)
        {
            return;
        }
        var path = menu.DataContext switch
        {
            GitChangeItemViewModel item => item.Path,
            GitChangeTreeNodeViewModel node => node.RelativePath,
            _ => viewModel.SelectedChange?.Path,
        };
        if (path is null)
        {
            return;
        }
        var pattern = string.Equals(menu.Tag as string, "Extension", StringComparison.Ordinal) && System.IO.Path.GetExtension(path) is { Length: > 0 } extension
            ? "*" + extension : "/" + path;
        var values = await ShowGitFormAsync("Ignore working files", "Append this pattern to .gitignore. Already tracked files remain tracked.",
            new GitWorkflowField("pattern", "Ignore pattern", pattern));
        if (values is not null)
        {
            await viewModel.RunRepositoryTaskAsync(new(GitRepositoryTask.IgnorePattern, values["pattern"]));
        }
    }
    private async void OnInspectStash(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is MenuItem { DataContext: GitStashItem stash } menu)
        {
            await viewModel.InspectStashAsync(stash.Reference, string.Equals(menu.Tag as string, "Untracked", StringComparison.Ordinal));
        }
    }

    private async void OnExportStashPatch(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && OwnerWindow is { } window && sender is MenuItem { DataContext: GitStashItem stash }
            && await viewModel.ReadStashPatchAsync(stash.Reference) is { } patch)
        {
            await new GitOutputDialog("Stash patch · " + stash.Reference, patch).ShowDialog(window);
        }
    }

    private async void OnFileHistory(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var path = (sender as Control)?.DataContext switch
        {
            GitChangeItemViewModel item => item.Path,
            GitTreeNodeViewModel node => node.Path,
            GitChangeTreeNodeViewModel node => node.RelativePath,
            _ => viewModel.IsLocalChangesSection ? viewModel.SelectedChange?.Path
                : viewModel.DetailTab == GitCommitDetailTab.FileTree ? viewModel.SelectedTreeNode?.Path : viewModel.SelectedCommitChange?.Path,
        };
        if (path is null)
        {
            var fields = await ShowGitFormAsync("File history", "Follow one tracked file through renames.", new GitWorkflowField("path", "File path"));
            path = fields?.GetValueOrDefault("path");
        }

        if (path is null || viewModel.CreateFileInvestigation(path, sender is Control { DataContext: GitTreeNodeViewModel { IsDirectory: true } or GitChangeTreeNodeViewModel { IsDirectory: true } }) is not { } investigation)
        {
            return;
        }

        var sha = await new GitFileHistoryDialog(investigation).ShowDialog<string?>(window);
        if (sha is not null)
        {
            viewModel.SelectCommitBySha(sha);
        }
    }

    private async void OnSelectedExternalDiff(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ExternalDiffAsync();
        }
    }

    private async void OnCopySelectedPatch(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && OwnerWindow?.Clipboard is { } clipboard
            && await viewModel.ReadSelectedPatchAsync() is { } patch)
        {
            await clipboard.SetTextAsync(patch);
        }
    }

    private async void OnSaveSelectedPatch(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window
            || await viewModel.ReadSelectedPatchAsync() is not { } patch)
        {
            return;
        }

        try
        {
            var destination = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save patch",
                SuggestedFileName = "changes.patch",
            });
            if (destination is null)
            {
                return;
            }

            await using var stream = await destination.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(patch);
            await writer.FlushAsync();
            stream.SetLength(stream.Position);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await new GitOutputDialog("Could not save patch", exception.Message).ShowDialog(window);
        }
    }
}
