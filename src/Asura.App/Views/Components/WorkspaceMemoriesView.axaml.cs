using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is WorkspaceMemoriesViewModel memories) { memories.RefreshCommand.Execute(null); }
    }
}
