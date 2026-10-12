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
            bool closed;
            try
            {
                closed = await window.CloseForQuitAsync();
            }
            catch
            {
                // A close that fails outright stops the quit too, and Asura
                // keeps running without the windows already closed.
                withdraw();
                throw;
            }

            if (!closed)
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

/// <summary>What happens to a window's saved frame and workspace as it closes.</summary>
internal enum ClosedWindowFate
{
    /// <summary>
    /// Closed while Asura goes on running: not reopened, its frame is where
    /// the next new window starts, and its recovery slot is emptied.
    /// </summary>
    Forgotten,

    /// <summary>The last window, where that ends the app: saved to reopen next time.</summary>
    Reopened,

    /// <summary>
    /// Closed by a confirmed quit, which saved every window first: kept for
    /// the next launch unless a later window stops the quit.
    /// </summary>
    KeptForQuit,

    /// <summary>Closed by an exit already under way, which keeps every window.</summary>
    KeptForExit,
}

internal static class ClosedWindowPolicy
{
    /// <summary>
    /// What a closing window leaves behind. A quit that is still asking its
    /// questions has saved nothing yet and may be cancelled, so a window
    /// closed meanwhile is forgotten like any other.
    /// </summary>
    public static ClosedWindowFate FateOf(
        bool quitConfirmed,
        bool exitStarted,
        bool isLastWindow,
        bool keepsRunningWithoutWindows)
    {
        if (quitConfirmed)
        {
            return ClosedWindowFate.KeptForQuit;
        }

        if (exitStarted)
        {
            return ClosedWindowFate.KeptForExit;
        }

        return isLastWindow && !keepsRunningWithoutWindows
            ? ClosedWindowFate.Reopened
            : ClosedWindowFate.Forgotten;
    }
}

/// <summary>
/// The windows a confirmed quit has closed so far. Every window is saved
/// before any closes, so a quit that finishes reopens them all. A quit that
/// a later window interrupts leaves Asura running without the ones already
/// closed, so they are forgotten as if each had been closed on its own: they
/// leave the saved windows, the last one's frame is where the next new window
/// starts, and their recovery slots are emptied.
/// </summary>
internal sealed class QuitClosedWindows
{
    private readonly List<(WindowPlacement? Placement, Action ReleaseSlot)> _closed = [];
    private WindowPlacementSet? _saved;

    /// <summary>The quit was confirmed with every window saved as <paramref name="saved"/>.</summary>
    public void Approve(WindowPlacementSet saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _saved = saved;
        _closed.Clear();
    }

    /// <summary>
    /// A window closed for the confirmed quit. Its recovery slot is kept for
    /// the next launch unless the quit is interrupted.
    /// </summary>
    public void Closed(WindowPlacement? placement, Action releaseSlot)
    {
        ArgumentNullException.ThrowIfNull(releaseSlot);
        if (_saved is not null)
        {
            _closed.Add((placement, releaseSlot));
        }
    }

    /// <summary>
    /// A window could not close. Empties the recovery slots of the windows
    /// that did and returns the windows to save now, or null with no quit
    /// confirmed.
    /// </summary>
    public WindowPlacementSet? Withdraw()
    {
        if (_saved is not { } saved)
        {
            return null;
        }

        var closed = _closed.ToArray();
        Clear();
        foreach (var (_, releaseSlot) in closed)
        {
            releaseSlot();
        }

        var placements = closed
            .Select(window => window.Placement)
            .OfType<WindowPlacement>()
            .ToArray();
        var closedSlots = placements.Select(placement => placement.Slot).ToHashSet();
        return new WindowPlacementSet(
            [.. saved.Windows.Where(window => !closedSlots.Contains(window.Slot))],
            placements.Length > 0
                ? placements[^1] with { State = WindowPlacementState.Normal }
                : saved.LastClosed);
    }

    /// <summary>The quit is over, finished or not.</summary>
    public void Clear()
    {
        _saved = null;
        _closed.Clear();
    }
}
