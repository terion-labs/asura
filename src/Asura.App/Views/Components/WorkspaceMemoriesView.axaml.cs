using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views.Components;

public sealed partial class WorkspaceMemoriesView : UserControl
{
    public WorkspaceMemoriesView() { InitializeComponent(); }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        _ = sender; _ = e;
        if (DataContext is not WorkspaceMemoriesViewModel memories || TopLevel.GetTopLevel(this) is not { } top) { return; }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Export workspace memories", SuggestedFileName = "workspace-memories.json", DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is { } path) { await memories.ExportAsync(path); }
    }

    /// <summary>Enter runs the search; the field has no button of its own to find.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key is Key.Enter or Key.Return && DataContext is WorkspaceMemoriesViewModel memories)
        {
            memories.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is WorkspaceMemoriesViewModel memories) { memories.RefreshCommand.Execute(null); }
    }
}
