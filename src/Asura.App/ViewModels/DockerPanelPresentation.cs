using Asura.Docker;
using FluentIcons.Common;

namespace Asura.App.ViewModels;

public enum DockerPanelSection
{
    Containers,
    Images,
    Volumes,
    Networks,
}

public enum DockerPanelDetail
{
    Info,
    Logs,
    Stats,
    Shell,
    Files,
    Json,
}

public sealed record DockerLogTextSegmentViewModel(
    string Text,
    bool IsMatch);

public sealed record DockerLogRowViewModel(
    string Timestamp,
    string Message,
    bool StartsContextBlock,
    IReadOnlyList<DockerLogTextSegmentViewModel> MessageSegments)
{
    public string DisplayTimestamp => Timestamp.Length > 11
        ? Timestamp[11..].TrimEnd('Z')
        : Timestamp;
}

public sealed class DockerContainerStackViewModel : ObservableObject
{
    private bool _isExpanded = true;

    public DockerContainerStackViewModel(
        string name,
        IReadOnlyList<DockerResourceItemViewModel> containers,
        int runningCount,
        bool isStandalone)
    {
        Name = name;
        Containers = containers;
        RunningCount = runningCount;
        IsStandalone = isStandalone;
    }

    public string Name { get; }

    public IReadOnlyList<DockerResourceItemViewModel> Containers { get; }

    public int RunningCount { get; }

    public bool IsStandalone { get; }

    public int Count => Containers.Count;

    public bool HasRunningContainers => RunningCount > 0;

    public bool HasLifecycleControls => !IsStandalone;

    public bool CanStart => Containers.Any(container =>
        !container.IsRunning && !container.IsPaused);

    public bool CanStop => Containers.Any(container =>
        container.IsRunning || container.IsPaused);

    public bool CanRestart => CanStop;

    public bool CanPause => Containers.Any(container =>
        container.IsRunning && !container.IsPaused);

    public bool CanResume => Containers.Any(container => container.IsPaused);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandIcon));
            }
        }
    }

    public Symbol ExpandIcon => IsExpanded ? Symbol.ChevronDown : Symbol.ChevronRight;

    public string Summary => RunningCount == 0
        ? $"{Count} stopped"
        : $"{RunningCount}/{Count} running";
}

public sealed class DockerResourceItemViewModel : ObservableObject
{
    private bool _isSelected;

    private DockerResourceItemViewModel(
        DockerResourceReference resource,
        string title,
        string subtitle,
        string tertiary,
        Symbol icon,
        StatusTone statusTone,
        bool isContainer,
        bool isRunning,
        bool isPaused,
        DockerContainerSummary? container)
    {
        Resource = resource;
        Title = title;
        Subtitle = subtitle;
        Tertiary = tertiary;
        Icon = icon;
        StatusTone = statusTone;
        IsContainer = isContainer;
        IsRunning = isRunning;
        IsPaused = isPaused;
        Container = container;
    }

    public DockerResourceReference Resource { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Tertiary { get; }

    public Symbol Icon { get; }

    /// <summary>
    /// A container's state, or neutral for the kinds that have none — an
    /// image, a volume, a network — whose glyph already says what they are.
    /// </summary>
    public StatusTone StatusTone { get; }

    public bool IsContainer { get; }

    public bool IsRunning { get; }

    public bool IsPaused { get; }

    public DockerContainerSummary? Container { get; }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    public static DockerResourceItemViewModel From(DockerContainerSummary container) => new(
        new DockerResourceReference(
            DockerResourceKind.Container,
            container.Id,
            container.Name),
        container.Name,
        container.Image,
        container.Status,
        Symbol.Box,
        container.IsRunning
            ? StatusTone.Success
            : container.IsPaused ? StatusTone.Warning : StatusTone.Neutral,
        true,
        container.IsRunning,
        container.IsPaused,
        container);

    public static DockerResourceItemViewModel From(DockerImageSummary image) => new(
        new DockerResourceReference(
            DockerResourceKind.Image,
            image.Id,
            $"{image.Repository}:{image.Tag}"),
        $"{image.Repository}:{image.Tag}",
        image.Size,
        image.Created,
        Symbol.Archive,
        StatusTone.Neutral,
        false,
        false,
        false,
        null);

    public static DockerResourceItemViewModel From(DockerVolumeSummary volume) => new(
        new DockerResourceReference(
            DockerResourceKind.Volume,
            volume.Name,
            volume.Name),
        volume.Name,
        volume.Size,
        $"{volume.Driver} · {volume.Scope}",
        Symbol.HardDrive,
        StatusTone.Neutral,
        false,
        false,
        false,
        null);

    public static DockerResourceItemViewModel From(DockerNetworkSummary network) => new(
        new DockerResourceReference(
            DockerResourceKind.Network,
            network.Id,
            network.Name),
        network.Name,
        network.Driver,
        network.Scope,
        Symbol.Globe,
        StatusTone.Neutral,
        false,
        false,
        false,
        null);
}
