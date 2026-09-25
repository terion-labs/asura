using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Asura.App.Views;

public sealed partial class MainWindow
{
    private static readonly FilePickerFileType AgentHistoryFileType = new(
        "Asura agent history")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    public void ToggleAgentPanel()
    {
        if (ViewModel.IsWorkspaceVisible && !ViewModel.HasOverlay)
        {
            ViewModel.ToggleAgentPanel();
        }
    }

    private async void OnSendAgentChatClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is null)
        {
            return;
        }

        try
        {
            await ViewModel.SendAgentPromptAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnQueueAgentSteeringClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            if (agentChat.CanOfferFollowUpQueue)
            {
                await agentChat.QueueSteeringAsync(_lifetime.Token);
            }
            else
            {
                await ViewModel.SendAgentPromptAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnAttachAgentImageClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { CanAttachImages: true } agentChat)
        {
            return;
        }

        await AgentImageImport.PickAsync(StorageProvider, agentChat, _lifetime.Token);
    }

    private void OnClearAgentImagesClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.AgentChat?.ClearPendingImages();
    }

    private async void OnAgentQuestionResponseKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        _ = sender;
        if (e.Key != Key.Enter
            || e.KeyModifiers != AvaloniaKeyModifiers.None
            || ViewModel.AgentChat is not { CanSubmitQuestionResponse: true } agentChat)
        {
            return;
        }

        e.Handled = true;
        try
        {
            await agentChat.SubmitQuestionResponseAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnSubmitAgentQuestionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.SubmitQuestionResponseAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnDeclineAgentQuestionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.DeclineQuestionAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnEnableAgentCapabilityAskClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.EnableCapabilityAskAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnKeepAgentCapabilityOffClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.KeepCapabilityOffAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnCancelAgentChatClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.StopAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnCancelAgentActionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.CancelActiveActionAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnClearAgentChatClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.ClearAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void OnClearAgentSavedScreenTargetClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.ClearAgentSavedScreenTarget();
    }

    private async void OnCreateAgentSavedScreenTargetClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            await ViewModel.CreateAgentSavedScreenTargetAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnAuthorizeAgentSavedScreenTargetClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentSavedScreenLiveTarget is not { IsAuthorized: false } target
            || !await Confirmations.AgentSavedScreenTarget(target).ShowDialog<bool>(this))
        {
            return;
        }

        if (!ViewModel.TryAuthorizeAgentSavedScreenTarget(out var error))
        {
            ViewModel.AgentChat?.ReportTargetUnavailable(error);
        }
    }

    private async void OnStartNewAgentConversationClick(object? sender, RoutedEventArgs e) =>
        await RunAgentConversationActionAsync(
            sender,
            e,
            (agent, token) => agent.StartNewConversationAsync(token));

    private async void OnOpenAgentConversationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AgentRunId runId })
        {
            await RunAgentConversationActionAsync(
                sender,
                e,
                (agent, token) => agent.OpenConversationAsync(runId, token));
        }
    }

    private async void OnDeleteAgentConversationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AgentRunId runId }
            && ViewModel.AgentChat is { } agent
            && agent.Conversations.FirstOrDefault(item => item.RunId == runId) is { } item
            && await Confirmations.AgentConversationDelete(item.Title).ShowDialog<bool>(this))
        {
            await RunAgentConversationActionAsync(
                sender,
                e,
                (agent, token) => agent.DeleteConversationAsync(runId, token),
                hideHistoryFlyout: false);
        }
    }

    private async void OnApplyAgentHistoryRetentionClick(
        object? sender,
        RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { CanApplyHistoryRetention: true } agent
            || !await Confirmations
                .AgentHistoryRetentionChange(agent.SelectedHistoryRetentionOption)
                .ShowDialog<bool>(this))
        {
            return;
        }

        await agent.ApplyHistoryRetentionAsync(_lifetime.Token);
    }

    private async void OnExportAgentHistoryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { CanExportHistory: true } agent)
        {
            return;
        }

        var selected = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export metadata-only agent history",
                SuggestedFileName = AgentRunHistoryExportController.SuggestedFileName,
                DefaultExtension = "json",
                FileTypeChoices = [AgentHistoryFileType],
                ShowOverwritePrompt = true,
            });
        if (selected?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        _ = await AgentRunHistoryExportController.ExportAsync(
            agent,
            path,
            _lifetime.Token);
    }

    private async void OnCopyAgentMessageClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Button { Tag: AgentChatMessageViewModel message })
        {
            return;
        }

        await ClipboardWriter.WriteTextAsync(message.Content);
    }

    private async void OnForkAgentConversationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AgentConversationForkPoint forkPoint })
        {
            await RunAgentConversationActionAsync(
                sender,
                e,
                (agent, token) => agent.ForkConversationAsync(forkPoint, token),
                hideHistoryFlyout: false);
        }
    }

    private async void OnSelectAgentModelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AiProviderModelDescriptor model })
        {
            await RunAgentConversationActionAsync(
                sender,
                e,
                (agent, token) => agent.SelectModelAsync(model, token),
                hideHistoryFlyout: false);
            (sender as Control)?.FindAncestorOfType<AgentWorkspaceView>()?
                .FindControl<Button>("AgentModelPickerButton")?.Flyout?.Hide();
        }
    }

    private async void OnRefreshAgentModelsClick(object? sender, RoutedEventArgs e) =>
        await RunAgentConversationActionAsync(
            sender,
            e,
            (agent, token) => agent.RefreshModelsAsync(token),
            hideHistoryFlyout: false);

    private async void OnToggleAgentModelFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AgentModelPickerItemViewModel item })
        {
            await RunAgentConversationActionAsync(
                sender,
                e,
                (agent, token) => agent.ToggleFavoriteModelAsync(item, token),
                hideHistoryFlyout: false);
        }
    }

    private async void OnMoveAgentQueuedFollowUpRequested(
        object? sender,
        AgentQueuedFollowUpMoveRequestedEventArgs e)
    {
        _ = sender;
        if (ViewModel.AgentChat is not { } agent)
        {
            return;
        }

        try
        {
            await agent.MoveQueuedFollowUpAsync(e.Item, e.DestinationIndex);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task RunAgentConversationActionAsync(
        object? sender,
        RoutedEventArgs e,
        Func<AgentChatViewModel, CancellationToken, Task> action,
        bool hideHistoryFlyout = true)
    {
        _ = e;
        if (ViewModel.AgentChat is not { } agent)
        {
            return;
        }

        try
        {
            await action(agent, _lifetime.Token);
            if (hideHistoryFlyout)
            {
                (sender as Control)?.FindAncestorOfType<AgentWorkspaceView>()?
                    .FindControl<Button>("AgentConversationHistoryButton")?.Flyout?.Hide();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnEnableAgentYoloClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.SelectFullAccessAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnDisableAgentYoloClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.SelectAskApprovalAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnApproveAgentActionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.DecideAsync(approved: true, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnDenyAgentActionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.DecideAsync(approved: false, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnRefreshAgentAuditClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.RefreshAuditAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnLoadOlderAgentAuditClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.AgentChat is not { } agentChat)
        {
            return;
        }

        try
        {
            await agentChat.LoadOlderAuditAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void OnToggleAgentClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ToggleAgentPanel();
    }

    private async void OnToggleAgentPinClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            await ViewModel.ToggleAgentPanelPinAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }
}
