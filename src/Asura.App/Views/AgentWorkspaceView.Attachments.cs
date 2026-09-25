using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public partial class AgentWorkspaceView
{
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
                await AgentImageImport.AddFilesAsync(agent, files, CancellationToken.None);
                return;
            }

            using var bitmap = await data.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                AgentImageImport.RequireAvailable(agent);
                using var stream = new MemoryStream();
                bitmap.Save(stream);
                agent.AddPendingImage(new AgentImageAttachment("Pasted image.png", "image/png", stream.ToArray()));
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
