namespace Asura.Application.ApplicationUpdates;

/// <summary>
/// Carries only closed diagnostic metadata across the desktop/updater boundary.
/// The inner exception is retained for debugging but must never be logged raw.
/// </summary>
public sealed class UpdateRestartPreparationException(
    UpdateRestartStep step,
    Exception? innerException = null,
    SessionCloseOutcome? closeOutcome = null) : Exception(
        "The desktop could not prepare for the update restart.", innerException)
{
    public UpdateRestartStep Step { get; } = step;

    public SessionCloseOutcome? CloseOutcome { get; } = closeOutcome;

    public string DiagnosticCode => Step switch
    {
        UpdateRestartStep.CloseSessions => CloseOutcome switch
        {
            SessionCloseOutcome.Cancelled => "update.restart.session-close.cancelled",
            SessionCloseOutcome.EngineFailed => "update.restart.session-close.engine-failed",
            SessionCloseOutcome.ConfirmationRequired => "update.restart.session-close.confirmation-required",
            _ => "update.restart.session-close.failed",
        },
        UpdateRestartStep.ClosePresentation => "update.restart.presentation-close.failed",
        UpdateRestartStep.CloseQuickTerminal => "update.restart.quick-terminal-close.failed",
        UpdateRestartStep.SaveWorkspaces => "update.restart.workspace-shutdown.failed",
        _ => "update.restart.preparation.failed",
    };
}
