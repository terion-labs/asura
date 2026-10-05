using System.Globalization;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views;

public sealed partial class GitFileHistoryDialog : Window
{
    private GitFileInvestigationViewModel? ViewModel => DataContext as GitFileInvestigationViewModel;
    public GitFileHistoryDialog() => InitializeComponent();
    public GitFileHistoryDialog(GitFileInvestigationViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync(reset: true);
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(null);
    private void OnOpenCommit(object? sender, RoutedEventArgs e) => Close(ViewModel?.SelectedCommit?.Sha);
    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.LoadFullHistoryAsync();
        }
    }

    private async void OnLoadMore(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.LoadAsync(reset: false);
        }
    }

    private async void OnRevisionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { SelectedCommit: { } commit } viewModel)
        {
            await viewModel.SelectAsync(commit);
        }
    }

    private async void OnBlameCommit(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { DataContext: GitBlameLine line })
        {
            await viewModel.NavigateAsync(line.CommitSha);
        }
    }

    private async void OnRangeHistory(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var range = FileContent.SelectedLineRange;
        var values = await new GitWorkflowDialog("Line history", $"Trace these lines in {viewModel.Path} from the selected revision.",
            [new("start", "First line", range.Start.ToString(CultureInfo.InvariantCulture)), new("end", "Last line", range.End.ToString(CultureInfo.InvariantCulture))])
            .ShowDialog<Dictionary<string, string>?>(this);
        if (values is null)
        {
            return;
        }

        if (!int.TryParse(values["start"], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !int.TryParse(values["end"], NumberStyles.None, CultureInfo.InvariantCulture, out var end) || start < 1 || end < start)
        {
            await new GitOutputDialog("Invalid line range", "Choose positive line numbers, with the last line at or after the first.").ShowDialog(this);
            return;
        }

        await viewModel.LoadRangeAsync(start, end);
    }

    private async void OnCopyPatch(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard && ViewModel is { } viewModel)
        {
            await clipboard.SetTextAsync(viewModel.Diff);
        }
    }

    private async void OnSaveRevision(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save recorded file",
                SuggestedFileName = System.IO.Path.GetFileName(viewModel.Path),
            });
            if (destination is null || await viewModel.ReadSelectedBytesAsync() is not { } bytes)
            {
                return;
            }

            await using var stream = await destination.OpenWriteAsync();
            await stream.WriteAsync(bytes);
            stream.SetLength(stream.Position);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await new GitOutputDialog("Could not save revision", exception.Message).ShowDialog(this);
        }
    }
}
