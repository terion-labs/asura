using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

public sealed partial class AgentApprovalCardView : UserControl
{
    public AgentApprovalCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves keyboard focus to the request when it appears, so it is read
    /// aloud and can be answered without hunting for it in the transcript.
    /// </summary>
    internal bool FocusRequest()
    {
        AgentApprovalRequest.BringIntoView();
        return AgentApprovalRequest.Focus(NavigationMethod.Tab);
    }

    public event EventHandler<RoutedEventArgs>? ApproveRequested;

    public event EventHandler<RoutedEventArgs>? DenyRequested;

    private void OnApproveClick(object? sender, RoutedEventArgs e) =>
        ApproveRequested?.Invoke(sender, e);

    private void OnDenyClick(object? sender, RoutedEventArgs e) =>
        DenyRequested?.Invoke(sender, e);
}
