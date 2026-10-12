using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

public sealed partial class AgentCapabilityRequestCardView : UserControl
{
    public AgentCapabilityRequestCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves keyboard focus to the request when it appears, so it is read
    /// aloud and can be answered without hunting for it in the transcript.
    /// </summary>
    internal bool FocusRequest()
    {
        AgentPendingCapabilityRequest.BringIntoView();
        return AgentPendingCapabilityRequest.Focus(NavigationMethod.Tab);
    }

    public event EventHandler<RoutedEventArgs>? EnableAskRequested;

    public event EventHandler<RoutedEventArgs>? KeepOffRequested;

    private void OnEnableAgentCapabilityAskClick(object? sender, RoutedEventArgs e) =>
        EnableAskRequested?.Invoke(sender, e);

    private void OnKeepAgentCapabilityOffClick(object? sender, RoutedEventArgs e) =>
        KeepOffRequested?.Invoke(sender, e);
}
