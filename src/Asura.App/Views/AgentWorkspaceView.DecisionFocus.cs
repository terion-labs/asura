using System.ComponentModel;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// A run that stops for a decision waits on whoever is looking at the agent.
/// When a new approval, question, or permission request appears while
/// keyboard focus is in the agent panel, or nowhere at all, focus moves to it
/// so it is read aloud and can be answered from the keyboard. Focus that is
/// somewhere else, a terminal mid-command for instance, is left alone; the
/// agent's status announcement still says that a decision is waiting.
/// </summary>
public partial class AgentWorkspaceView
{
    private AgentChatViewModel? _agentDecisionSource;
    private object? _focusedAgentDecision;

    private void SetAgentDecisionSource(AgentChatViewModel? agentChat)
    {
        if (ReferenceEquals(_agentDecisionSource, agentChat))
        {
            return;
        }

        _agentDecisionSource?.PropertyChanged -= OnAgentDecisionSourcePropertyChanged;
        _agentDecisionSource = agentChat;
        _agentDecisionSource?.PropertyChanged += OnAgentDecisionSourcePropertyChanged;

        // A request already waiting when the panel is bound was not just
        // raised; arriving at the panel should not yank focus into it.
        _focusedAgentDecision = PendingAgentDecision(agentChat);
    }

    private void OnAgentDecisionSourcePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
                nameof(AgentChatViewModel.PendingApproval)
                or nameof(AgentChatViewModel.PendingQuestion)
                or nameof(AgentChatViewModel.PendingCapabilityRequest)))
        {
            return;
        }

        var decision = PendingAgentDecision(sender as AgentChatViewModel);
        if (Equals(decision, _focusedAgentDecision))
        {
            return;
        }

        _focusedAgentDecision = decision;
        if (decision is not null)
        {
            // After layout, so the card's visibility binding has applied and
            // the card can take focus.
            Dispatcher.UIThread.Post(
                () => FocusAgentDecision(decision),
                DispatcherPriority.Loaded);
        }
    }

    private void FocusAgentDecision(object decision)
    {
        if (_agentDecisionSource is not { } agentChat
            || !Equals(PendingAgentDecision(agentChat), decision)
            || !KeyboardFocusIsAvailable())
        {
            return;
        }

        _ = agentChat.HasPendingApproval
            ? AgentApprovalCard.FocusRequest()
            : agentChat.HasPendingQuestion
                ? AgentQuestionCard.FocusRequest()
                : AgentCapabilityRequestCard.FocusRequest();
    }

    private bool KeyboardFocusIsAvailable()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is not Visual visual
            || ReferenceEquals(visual, this)
            || this.IsVisualAncestorOf(visual);
    }

    /// <summary>
    /// The request the run is waiting on, in the order the panel shows them.
    /// Identity is the request's own id, so the same request republished by a
    /// later refresh is not treated as new.
    /// </summary>
    private static object? PendingAgentDecision(AgentChatViewModel? agentChat) =>
        (object?)agentChat?.PendingApproval?.Id
        ?? (object?)agentChat?.PendingQuestion?.Id
        ?? agentChat?.PendingCapabilityRequest?.Id;
}
