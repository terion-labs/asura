using System.Globalization;
using Asura.Application;

namespace Asura.App;

/// <summary>
/// What is still running across every window and Quick Terminal when Asura
/// is asked to quit, gathered before anything closes so one alert can speak
/// for all of them.
/// </summary>
public sealed record RunningSessionCensus(
    IReadOnlyList<ActiveSessionSummary> Sessions,
    int WindowCount,
    bool IncludesQuickTerminal,
    bool IsComplete)
{
    public static RunningSessionCensus Empty { get; } = new([], 0, false, true);

    /// <summary>
    /// Whether quitting has anything to ask about: a running session, or a
    /// window that could not say whether it has one.
    /// </summary>
    public bool RequiresConfirmation => Sessions.Count > 0 || !IsComplete;

    /// <summary>Adds one window's answer; null means it could not answer.</summary>
    public RunningSessionCensus WithWindow(IReadOnlyList<ActiveSessionSummary>? sessions) =>
        sessions is null
            ? this with { IsComplete = false }
            : sessions.Count == 0
                ? this
                : this with
                {
                    Sessions = [.. Sessions, .. sessions],
                    WindowCount = WindowCount + 1,
                };

    /// <summary>Adds Quick Terminal's answer; null means it could not answer.</summary>
    public RunningSessionCensus WithQuickTerminal(IReadOnlyList<ActiveSessionSummary>? sessions) =>
        sessions is null
            ? this with { IsComplete = false }
            : sessions.Count == 0
                ? this
                : this with
                {
                    Sessions = [.. Sessions, .. sessions],
                    IncludesQuickTerminal = true,
                };

    /// <summary>The alert's sentence: how much is running, and where.</summary>
    public string Describe()
    {
        if (Sessions.Count == 0)
        {
            return IsComplete
                ? "Nothing is running."
                : "Asura couldn't confirm that every session is idle. Quitting ends any that are still running.";
        }

        var count = Sessions.Count == 1
            ? "1 session"
            : string.Create(CultureInfo.CurrentCulture, $"{Sessions.Count} sessions");
        var windows = WindowCount == 1
            ? "1 window"
            : string.Create(CultureInfo.CurrentCulture, $"{WindowCount} windows");
        var place = (WindowCount > 0, IncludesQuickTerminal) switch
        {
            (true, true) => $" in {windows} and Quick Terminal",
            (true, false) => $" in {windows}",
            (false, true) => " in Quick Terminal",
            _ => string.Empty,
        };
        var verb = Sessions.Count == 1 ? "is" : "are";
        // With unchecked windows there may be more than the one counted.
        var pronoun = Sessions.Count == 1 && IsComplete ? "it" : "them";
        var unconfirmed = IsComplete
            ? string.Empty
            : " Others couldn't be checked.";
        return $"{count}{place} {verb} still running.{unconfirmed} Quitting ends {pronoun}.";
    }
}
