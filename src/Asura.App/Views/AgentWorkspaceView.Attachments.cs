using Asura.App.ViewModels;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public partial class AgentWorkspaceView
{
    private const string ComposerDropTargetClass = "dropTarget";
    private bool _pasteInProgress;

    private void OnRemoveAgentImageClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { Tag: AgentImageAttachment image }
            && DataContext is IAgentWorkspaceHost { AgentChat: { } agent })
        {
            agent.RemovePendingImage(image);
        }
    }

    private void OnRemoveAgentFileClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Button { Tag: AgentFileAttachment file }
            && DataContext is IAgentWorkspaceHost { AgentChat: { } agent })
        {
            agent.RemovePendingFile(file);
        }
    }

    /// <summary>
    /// Files and images dragged onto the composer attach to the next message,
    /// exactly as if they had been chosen with the attach button or pasted.
    /// The composer lights up only while it can take them.
    /// </summary>
    private void OnAgentComposerDragOver(object? sender, DragEventArgs e)
    {
        _ = sender;
        var accepts = DataContext is IAgentWorkspaceHost { AgentChat.CanAttachFiles: true }
            && CarriesAttachments(e.DataTransfer);
        AgentComposer.Classes.Set(ComposerDropTargetClass, accepts);
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnAgentComposerDragLeave(object? sender, DragEventArgs e)
    {
        _ = sender;
        // Moving between the composer's own children raises a leave for the
        // child; only leaving the composer ends the drop target.
        if (new Rect(AgentComposer.Bounds.Size).Contains(e.GetPosition(AgentComposer)))
        {
            return;
        }

        AgentComposer.Classes.Remove(ComposerDropTargetClass);
    }

    private async void OnAgentComposerDrop(object? sender, DragEventArgs e)
    {
        _ = sender;
        AgentComposer.Classes.Remove(ComposerDropTargetClass);
        if (DataContext is not IAgentWorkspaceHost { AgentChat: { } agent }
            || !CarriesAttachments(e.DataTransfer))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        // Read the payload now: the drag source may release it once the
        // drop event returns.
        var files = e.DataTransfer.TryGetFiles();
        using var bitmap = files is { Length: > 0 } ? null : e.DataTransfer.TryGetBitmap();
        try
        {
            if (files is { Length: > 0 })
            {
                await AgentAttachmentImport.AddFilesAsync(agent, files, CancellationToken.None);
                return;
            }

            if (bitmap is not null)
            {
                AgentAttachmentImport.RequireAvailable(agent);
                agent.AddPendingImage(AgentAttachmentImport.EncodePastedImage(bitmap));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            agent.AttachmentError = exception.Message;
        }
    }

    private static bool CarriesAttachments(IDataTransfer dataTransfer) =>
        dataTransfer.Contains(DataFormat.File)
        || dataTransfer.Contains(DataFormat.Bitmap);

    private async void OnAgentPromptPasting(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_pasteInProgress
            || DataContext is not IAgentWorkspaceHost { AgentChat: { } agent }
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        _pasteInProgress = true;
        try
        {
            using var data = await clipboard.TryGetDataAsync();
            if (data is null)
            {
                return;
            }
            var files = await data.TryGetFilesAsync();
            if (files is { Length: > 0 })
            {
                await AgentAttachmentImport.AddFilesAsync(agent, files, CancellationToken.None);
                return;
            }

            if (data.Contains(DataFormat.Bitmap))
            {
                AgentAttachmentImport.RequireAvailable(agent);
                using var bitmap = await data.TryGetBitmapAsync();
                if (bitmap is null) { return; }
                var image = AgentAttachmentImport.EncodePastedImage(bitmap);
                if (!ReferenceEquals((DataContext as IAgentWorkspaceHost)?.AgentChat, agent))
                {
                    return;
                }
                AgentAttachmentImport.RequireAvailable(agent);
                agent.AddPendingImage(image);
                return;
            }

            if (await data.TryGetTextAsync() is { } text
                && agent.CanEnterPrompt
                && ReferenceEquals((DataContext as IAgentWorkspaceHost)?.AgentChat, agent))
            {
                AgentChatPromptInput.SelectedText = text;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            agent.AttachmentError = exception.Message;
        }
        finally
        {
            _pasteInProgress = false;
        }
    }
}
