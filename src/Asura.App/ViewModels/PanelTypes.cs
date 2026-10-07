using Asura.Core;
using FluentIcons.Common;

namespace Asura.App.ViewModels;

/// <summary>
/// One noun and one sentence per panel type, used wherever a type is offered.
///
/// The launcher said "New terminal", "File Viewer" and "Process monitor" in
/// one grid; the Add Panel chooser said "Terminal", "Files" and "Processes";
/// the palette grouped "Create · files" beside "Create · Docker"; Settings
/// offered "File viewer". Each screen had written its own copy. The launcher,
/// the chooser, the palette and Settings read them from here instead.
/// </summary>
public static class PanelTypes
{
    public const string Terminal = "Terminal";
    public const string TerminalDescription = "Your default shell on this computer";

    public const string Browser = "Browser";
    public const string BrowserDescription = "Web pages in a panel beside your work";

    public const string Files = "Files";
    public const string FilesDescription = "Browse files here or on a saved connection";

    public const string Processes = "Processes";
    public const string ProcessesDescription = "Watch the processes running on this computer";

    public const string Statistics = "Statistics";
    public const string StatisticsDescription = "Live CPU, memory, disk, and network use";

    public const string Database = "Database";
    public const string DatabaseDescription = "Query SQLite, PostgreSQL, or MySQL";

    public const string Docker = "Docker";
    public const string DockerDescription = "Containers, images, volumes, and networks";

    public const string Kubernetes = "Kubernetes";
    public const string KubernetesDescription = "Cluster resources, manifests, and pod logs";

    public const string Git = "Git";
    public const string GitDescription = "Stage, commit, and browse history";

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

    public static string Description(PanelKind kind) => kind switch
    {
        PanelKind.Terminal => TerminalDescription,
        PanelKind.Browser => BrowserDescription,
        PanelKind.FileViewer => FilesDescription,
        PanelKind.Statistics => StatisticsDescription,
        PanelKind.ProcessMonitor => ProcessesDescription,
        PanelKind.DatabaseViewer => DatabaseDescription,
        PanelKind.Docker => DockerDescription,
        PanelKind.Kubernetes => KubernetesDescription,
        PanelKind.Git => GitDescription,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static Symbol Icon(PanelKind kind) => kind switch
    {
        PanelKind.Terminal => Symbol.WindowConsole,
        PanelKind.Browser => Symbol.Globe,
        PanelKind.FileViewer => Symbol.Folder,
        PanelKind.Statistics => Symbol.PulseSquare,
        PanelKind.ProcessMonitor => Symbol.Gauge,
        PanelKind.DatabaseViewer => Symbol.Database,
        PanelKind.Docker => Symbol.Box,
        PanelKind.Kubernetes => Symbol.Grid,
        PanelKind.Git => Symbol.BranchFork,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
