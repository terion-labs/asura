using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

public sealed partial class AgentDesktopAccessCardView : UserControl
{
    public AgentDesktopAccessCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves keyboard focus to the card when it appears, so it is read aloud
    /// and answered without hunting for it in the transcript.
    /// </summary>
    internal bool FocusRequest()
    {
        AgentPendingDesktopAccess.BringIntoView();
        return AgentPendingDesktopAccess.Focus(NavigationMethod.Tab);
    }

    private AgentChatViewModel? AgentChat => (DataContext as IAgentWorkspaceHost)?.AgentChat;

    private void OnOpenSystemSettingsClick(object? sender, RoutedEventArgs e) =>
        _ = AgentChat?.OpenDesktopAccessSettingsAsync();

    private void OnNotNowClick(object? sender, RoutedEventArgs e) =>
        _ = AgentChat?.DismissDesktopAccessAsync(CancellationToken.None);
}
