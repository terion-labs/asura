using Asura.Application;

namespace Asura.App;

/// <summary>One window's part in quitting Asura.</summary>
internal interface IQuitParticipant
{
    /// <summary>Asks about unsaved edits; false means the person kept editing.</summary>
    Task<bool> ConfirmDiscardPendingEditsAsync();

    /// <summary>What is still running, without closing anything; null if it cannot tell.</summary>
    Task<IReadOnlyList<ActiveSessionSummary>?> InspectRunningSessionsAsync();

    /// <summary>Closes for a quit that is already confirmed; false if it could not.</summary>
    Task<bool> CloseForQuitAsync();
}

internal enum QuitOutcome
{
    /// <summary>The person said no somewhere; every window is as it was.</summary>
    Cancelled,

    /// <summary>A window could not close. The ones before it are closed; the rest stay open.</summary>
    Interrupted,

    /// <summary>Every window closed; the app can exit.</summary>
    ReadyToExit,
}

/// <summary>
/// The order a quit happens in: unsaved edits first, window by window; then
/// one census of what is running everywhere and, if anything is, one alert;
/// only then are windows closed. Nothing closes before every question is
/// answered, so cancelling leaves every window open.
/// </summary>
internal static class ApplicationQuit
{
    public static async Task<QuitOutcome> RunAsync(
        IReadOnlyList<IQuitParticipant> windows,
        Func<Task<IReadOnlyList<ActiveSessionSummary>?>> inspectQuickTerminal,
        Func<RunningSessionCensus, Task<bool>> confirm,
        Action approve,
        Action withdraw)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(inspectQuickTerminal);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(approve);
        ArgumentNullException.ThrowIfNull(withdraw);

        foreach (var window in windows)
        {
            if (!await window.ConfirmDiscardPendingEditsAsync())
            {
                return QuitOutcome.Cancelled;
            }
        }

        var census = RunningSessionCensus.Empty;
        foreach (var window in windows)
        {
            census = census.WithWindow(await window.InspectRunningSessionsAsync());
        }

        census = census.WithQuickTerminal(await inspectQuickTerminal());
        if (census.RequiresConfirmation && !await confirm(census))
        {
            return QuitOutcome.Cancelled;
        }

        approve();
        foreach (var window in windows)
        {
            if (!await window.CloseForQuitAsync())
            {
                // That window reported why. The ones already closed stay
                // closed; the rest stay open and Asura keeps running.
                withdraw();
                return QuitOutcome.Interrupted;
            }
        }

        return QuitOutcome.ReadyToExit;
    }
}
