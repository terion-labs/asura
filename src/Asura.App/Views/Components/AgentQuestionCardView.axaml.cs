using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

public sealed partial class AgentQuestionCardView : UserControl
{
    public AgentQuestionCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves keyboard focus to the request when it appears, so it is read
    /// aloud and can be answered without hunting for it in the transcript.
    /// </summary>
    internal bool FocusRequest()
    {
        AgentPendingQuestion.BringIntoView();
        return AgentPendingQuestion.Focus(NavigationMethod.Tab);
    }

    public event EventHandler<RoutedEventArgs>? DeclineRequested;

    public event EventHandler<KeyEventArgs>? ResponseKeyDownRequested;

    public event EventHandler<RoutedEventArgs>? SubmitRequested;

    private void OnAgentQuestionResponseKeyDown(object? sender, KeyEventArgs e) =>
        ResponseKeyDownRequested?.Invoke(sender, e);

    private void OnDeclineAgentQuestionClick(object? sender, RoutedEventArgs e) =>
        DeclineRequested?.Invoke(sender, e);

    private void OnSubmitAgentQuestionClick(object? sender, RoutedEventArgs e) =>
        SubmitRequested?.Invoke(sender, e);
}
