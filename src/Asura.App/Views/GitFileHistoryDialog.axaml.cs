using System.Globalization;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views;

public sealed partial class GitFileHistoryDialog : DialogWindow
{
    private GitFileInvestigationViewModel? ViewModel => DataContext as GitFileInvestigationViewModel;
    public GitFileHistoryDialog() => InitializeComponent();
    public GitFileHistoryDialog(GitFileInvestigationViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync(reset: true);
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>
    /// The panel shows the chosen commit. The window stays open: it is a tool
    /// consulted alongside the panel, not a question that ends with an answer.
    /// </summary>
    public event EventHandler<string>? CommitRequested;

    private void OnOpenCommit(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedCommit?.Sha is { } sha)
        {
            CommitRequested?.Invoke(this, sha);
        }
    }
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
        var values = await new GitWorkflowDialog(new GitWorkflowForm(
            "Line History",
            $"Trace these lines in {viewModel.Path} from the selected revision.",
            new GitWorkflowAction("Show History"),
            [new("start", "First line", range.Start.ToString(CultureInfo.InvariantCulture)), new("end", "Last line", range.End.ToString(CultureInfo.InvariantCulture))])
        {
            // Checked in the form, beside the number that is wrong.
            Validate = current => GitRuntimePanelViewModel.ValidateLineRange(current["start"], current["end"]) is { } error
                ? new GitWorkflowFieldError(error.Field, error.Message)
                : null,
        }).ShowDialog<Dictionary<string, string>?>(this);
        if (values is null
            || GitRuntimePanelViewModel.ParseLineNumber(values["start"]) is not { } start
            || GitRuntimePanelViewModel.ParseLineNumber(values["end"]) is not { } end)
        {
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
                Title = "Save Revision",
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
            await Confirmations.Problem("Couldn’t Save the Revision", SaveFailure.Describe(exception)).ShowDialog(this);
        }
    }
}
