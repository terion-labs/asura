using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views;

public sealed partial class GitOutputDialog : Window
{
    public GitOutputDialog() => InitializeComponent();

    public GitOutputDialog(string title, string output) : this()
    {
        Title = title;
        Shell.Title = title;
        Output.Text = output;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(Output.Text ?? "");
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save Git output", SuggestedFileName = "git-output.txt" });
            if (destination is null)
            {
                return;
            }

            await using var stream = await destination.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(Output.Text);
            await writer.FlushAsync();
            stream.SetLength(stream.Position);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Shell.Subtitle = "Could not save: " + exception.Message;
        }
    }
}
