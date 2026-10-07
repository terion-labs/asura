namespace Asura.App;

internal enum QuickTerminalEscapeAction
{
    /// <summary>Escape is the terminal's: let it reach the running program.</summary>
    PassToTerminal,
    CancelPendingInteraction,
    Hide,
}

/// <summary>
/// Keeps Quick Terminal lifecycle decisions independent of Avalonia window callbacks so focus,
/// Escape, reuse, and reset behavior can be verified without a compositor or native hotkey loop.
/// </summary>
internal static class QuickTerminalRuntimeRules
{
    public static bool ShouldDismissForFocusLoss(
        bool isVisible,
        bool hideOnFocusLoss) =>
        isVisible && hideOnFocusLoss;

    /// <summary>
    /// Escape first cancels a prompt Asura itself is showing. Otherwise it
    /// belongs to the program in the terminal, unless the person asked for
    /// Escape to hide the panel.
    /// </summary>
    public static QuickTerminalEscapeAction ResolveEscape(
        bool isVisible,
        bool pendingInteractionCancelled,
        bool hideOnEscape) =>
        !isVisible
            ? QuickTerminalEscapeAction.PassToTerminal
            : pendingInteractionCancelled
                ? QuickTerminalEscapeAction.CancelPendingInteraction
                : hideOnEscape
                    ? QuickTerminalEscapeAction.Hide
                    : QuickTerminalEscapeAction.PassToTerminal;

    public static bool ShouldResetAfterHide(bool restoreLastSession) =>
        !restoreLastSession;

    public static bool ShouldResetForDefinitionOrPolicyChange(
        bool terminalDefinitionsChanged,
        bool previousRestoreLastSession,
        bool restoreLastSession,
        bool isVisible) =>
        terminalDefinitionsChanged
        || (previousRestoreLastSession && !restoreLastSession && !isVisible);
}
