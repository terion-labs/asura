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
/// the chooser, the palette and Settings read them from here instead. The
/// nouns themselves live in <see cref="PanelTypeNames"/>, so the agent runtime
/// names a panel type the same way.
/// </summary>
public static class PanelTypes
{
    public const string Terminal = PanelTypeNames.Terminal;
    public const string TerminalDescription = "Your default shell on this computer";

    public const string Browser = PanelTypeNames.Browser;
    public const string BrowserDescription = "Web pages in a panel beside your work";

    public const string Files = PanelTypeNames.Files;
    public const string FilesDescription = "Browse files here or on a saved connection";

    public const string Processes = PanelTypeNames.Processes;
    public const string ProcessesDescription = "Watch the processes running on this computer";

    public const string Statistics = PanelTypeNames.Statistics;
    public const string StatisticsDescription = "Live CPU, memory, disk, and network use";

    public const string Database = PanelTypeNames.Database;
    public const string DatabaseDescription = "Query SQLite, PostgreSQL, or MySQL";

    public const string Docker = PanelTypeNames.Docker;
    public const string DockerDescription = "Containers, images, volumes, and networks";

    public const string Kubernetes = PanelTypeNames.Kubernetes;
    public const string KubernetesDescription = "Cluster resources, manifests, and pod logs";

    public const string Git = PanelTypeNames.Git;
    public const string GitDescription = "Stage, commit, and browse history";

    /// <summary>The noun for a panel type, as every screen names it.</summary>
    public static string Name(PanelKind kind) => kind == PanelKind.Placeholder
        ? throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        : PanelTypeNames.Name(kind);

    public static string Name(ScreenPanelKind kind) => PanelTypeNames.Name(kind);

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

    public static Symbol Icon(ScreenPanelKind kind) => kind switch
    {
        ScreenPanelKind.Terminal => Icon(PanelKind.Terminal),
        ScreenPanelKind.Browser => Icon(PanelKind.Browser),
        ScreenPanelKind.FileViewer => Icon(PanelKind.FileViewer),
        ScreenPanelKind.Statistics => Icon(PanelKind.Statistics),
        ScreenPanelKind.ProcessMonitor => Icon(PanelKind.ProcessMonitor),
        ScreenPanelKind.DatabaseViewer => Icon(PanelKind.DatabaseViewer),
        ScreenPanelKind.Docker => Icon(PanelKind.Docker),
        ScreenPanelKind.Kubernetes => Icon(PanelKind.Kubernetes),
        ScreenPanelKind.Git => Icon(PanelKind.Git),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
