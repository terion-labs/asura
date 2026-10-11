namespace Asura.Core;

/// <summary>
/// The one noun each panel type goes by, wherever a person reads it.
///
/// The app's launcher, chooser, palette and Settings already named panel types
/// from one table, but the agent wrote its own: a run's target said "Panel"
/// for a Database, Docker, Kubernetes or Git panel, and its context summary
/// said "2 terminals" beside "1 Files panel". Every layer that names a panel
/// type, the agent runtime included, reads the noun from here.
/// </summary>
public static class PanelTypeNames
{
    public const string Terminal = "Terminal";
    public const string Browser = "Browser";
    public const string Files = "Files";
    public const string Processes = "Processes";
    public const string Statistics = "Statistics";
    public const string Database = "Database";
    public const string Docker = "Docker";
    public const string Kubernetes = "Kubernetes";
    public const string Git = "Git";

    /// <summary>
    /// What a panel is called when its type is not one a person can choose:
    /// the placeholder a slot shows before it has a panel.
    /// </summary>
    public const string Panel = "Panel";

    /// <summary>The noun for a panel type, as every screen names it.</summary>
    public static string Name(PanelKind kind) => kind switch
    {
        PanelKind.Terminal => Terminal,
        PanelKind.Browser => Browser,
        PanelKind.FileViewer => Files,
        PanelKind.Statistics => Statistics,
        PanelKind.ProcessMonitor => Processes,
        PanelKind.DatabaseViewer => Database,
        PanelKind.Docker => Docker,
        PanelKind.Kubernetes => Kubernetes,
        PanelKind.Git => Git,
        PanelKind.Placeholder => Panel,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string Name(ScreenPanelKind kind) => kind switch
    {
        ScreenPanelKind.Terminal => Terminal,
        ScreenPanelKind.Browser => Browser,
        ScreenPanelKind.FileViewer => Files,
        ScreenPanelKind.Statistics => Statistics,
        ScreenPanelKind.ProcessMonitor => Processes,
        ScreenPanelKind.DatabaseViewer => Database,
        ScreenPanelKind.Docker => Docker,
        ScreenPanelKind.Kubernetes => Kubernetes,
        ScreenPanelKind.Git => Git,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// A count of panels of one type with the type's own noun: "1 Terminal
    /// panel", "3 Files panels". The noun keeps its case, so the phrase reads
    /// the same for every type.
    /// </summary>
    public static string Count(PanelKind kind, long count)
    {
        if (kind == PanelKind.Placeholder)
        {
            return CountText.Of(count, "panel", "panels");
        }

        var noun = Name(kind);
        return CountText.Of(count, $"{noun} panel", $"{noun} panels");
    }

    /// <summary>
    /// A count of panels: by type when they are all one type, otherwise
    /// "4 panels".
    /// </summary>
    public static string Count(IReadOnlyCollection<PanelKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var distinct = kinds.Distinct().Take(2).ToArray();
        return distinct.Length == 1
            ? Count(distinct[0], kinds.Count)
            : CountText.Of(kinds.Count, "panel", "panels");
    }
}
