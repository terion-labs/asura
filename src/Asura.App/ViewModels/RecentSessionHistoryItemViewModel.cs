using Asura.Application;
using Asura.Core;
using FluentIcons.Common;

namespace Asura.App.ViewModels;

public sealed record RecentSessionHistoryItemViewModel(
    RecentSessionRecord Record,
    bool CanOpen,
    DateTimeOffset ObservedAt,
    string? SourceTransport = null,
    string? SourceEndpoint = null)
{
    public SessionId SessionId => Record.SessionId;

    public DefinitionKey SourceDefinition => Record.SourceDefinition;

    public PanelKind PanelKind => Record.Kind;

    public RecentSessionOutcome Outcome => Record.Outcome;

    public string Title => Record.Title;

    public string PanelKindName => PanelKindLabel(PanelKind);

    public string OutcomeName => OutcomeLabel(Outcome);

    public string Detail => $"{PanelKindName} · {OutcomeName}";

    /// <summary>
    /// What reopening this row would actually connect to. A row identifies a
    /// session by the definition behind it, so the endpoint is resolved from the
    /// saved definition rather than stored in history — history keeps metadata,
    /// and an endpoint copied at session time would go stale the moment the
    /// connection was edited.
    /// </summary>
    public string Endpoint => SourceEndpoint ?? SourceIdentifier;

    public bool HasEndpoint => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>
    /// The transport badge — SSH, Docker, Local — falling back to the definition
    /// kind for a session whose definition has since been deleted, where there is
    /// no transport left to name.
    /// </summary>
    public string SourceKind => SourceTransport ?? SourceKindLabel(SourceDefinition.Kind);

    /// <summary>
    /// Rows are scanned by shape before they are read, so the glyph has to track
    /// the transport rather than being a terminal icon on every row.
    /// </summary>
    public Symbol SourceGlyph => SourceTransport switch
    {
        "Docker" => Symbol.Box,
        "Local" or "WSL" => Symbol.Desktop,
        null when SourceDefinition.Kind == ScreenDefinition.Kind => Symbol.Grid,
        null when SourceDefinition.Kind == WorkspaceDefinition.Kind => Symbol.Layer,
        _ => Symbol.WindowConsole,
    };

    public string SourceIdentifier => SourceDefinition.Value;

    public string SessionIdentifier => SessionId.Value;

    public string LastUsed => RelativeTime(Record.LastUsedAt, ObservedAt);

    public string Started => DisplayDates.Format(Record.StartedAt);

    public string Ended => Record.EndedAt is { } endedAt
        ? DisplayDates.Format(endedAt)
        : "In progress";

    public string Duration => Record.EndedAt is { } endedAt
        ? FormatDuration(endedAt - Record.StartedAt)
        : "In progress";

    public string ReopenStatus => CanOpen
        ? "Reopening opens the workspace or screen as it's saved now. History keeps names and times, not what was on screen."
        : "The workspace or screen that session came from no longer exists or isn't available on this computer. Its details stay here for review.";

    /// <summary>
    /// Whether the row would look identical.
    ///
    /// Record equality cannot say: every refresh stamps a new
    /// <see cref="ObservedAt"/>, so two projections of the same session never
    /// compare equal and the list rebuilds every row — which drops whatever the
    /// pointer was hovering.
    /// </summary>
    public bool PresentsSameAs(RecentSessionHistoryItemViewModel other) =>
        other is not null
        && SessionId == other.SessionId
        && CanOpen == other.CanOpen
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal)
        && string.Equals(SourceKind, other.SourceKind, StringComparison.Ordinal)
        && string.Equals(LastUsed, other.LastUsed, StringComparison.Ordinal)
        && SourceGlyph == other.SourceGlyph;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(0, (int)duration.TotalSeconds)} sec";
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return $"{(int)duration.TotalMinutes} min";
        }

        return duration < TimeSpan.FromDays(1)
            ? $"{(int)duration.TotalHours} hr {duration.Minutes} min"
            : $"{(int)duration.TotalDays} d {duration.Hours} hr";
    }

    private static string RelativeTime(DateTimeOffset timestamp, DateTimeOffset observedAt)
    {
        var age = observedAt.ToUniversalTime() - timestamp.ToUniversalTime();
        if (age < TimeSpan.Zero || age < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)age.TotalHours)} hr ago";
        }

        return age < TimeSpan.FromDays(7)
            ? $"{Math.Max(1, (int)age.TotalDays)} d ago"
            : DisplayDates.Format(timestamp);
    }

    private static string PanelKindLabel(PanelKind kind) => PanelTypes.Name(kind);

    private static string OutcomeLabel(RecentSessionOutcome outcome) => outcome switch
    {
        RecentSessionOutcome.Active => "Active",
        RecentSessionOutcome.GracefullyClosed => "Closed",
        RecentSessionOutcome.ForceTerminated => "Force terminated",
        RecentSessionOutcome.Failed => "Failed",
        RecentSessionOutcome.Cancelled => "Canceled",
        RecentSessionOutcome.Interrupted => "Interrupted",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static string SourceKindLabel(DefinitionKind kind) => kind switch
    {
        var value when value == ConnectionProfile.Kind => "Connection",
        var value when value == ScreenDefinition.Kind => "Screen",
        var value when value == WorkspaceDefinition.Kind => "Workspace",
        _ => kind.Value,
    };
}
