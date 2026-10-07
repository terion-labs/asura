using System.Globalization;
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

/// <summary>
/// The views of the selected resource, in the order the segmented control
/// shows them. Five is as many as a segmented control holds, so a container's
/// usage figures are part of Info rather than a sixth view of their own.
/// </summary>
public enum DockerPanelDetail
{
    Info,
    Logs,
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
    /// <summary>
    /// When the engine wrote the line, in the reader's local time and
    /// culture. The engine sends UTC to the nanosecond; a line it sends in
    /// some other shape is shown as sent.
    /// </summary>
    public string DisplayTimestamp => ParseEngineTime(Timestamp) is { } moment
        ? DisplayDates.FormatPreciseTime(moment)
        : Timestamp;

    /// <summary>
    /// Reads an RFC 3339 time as the engine writes it. .NET reads at most
    /// seven fractional digits, so the nanoseconds past them are dropped.
    /// </summary>
    internal static DateTimeOffset? ParseEngineTime(string text)
    {
        if (!text.Contains('T', StringComparison.Ordinal))
        {
            return null;
        }

        var point = text.IndexOf('.', StringComparison.Ordinal);
        if (point >= 0)
        {
            var end = point + 1;
            while (end < text.Length && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            if (end - point - 1 > 7)
            {
                text = string.Concat(text.AsSpan(0, point + 8), text.AsSpan(end));
            }
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var moment)
            ? moment
            : null;
    }
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
                OnPropertyChanged(nameof(ToggleLabel));
            }
        }
    }

    public Symbol ExpandIcon => IsExpanded ? Symbol.ChevronDown : Symbol.ChevronRight;

    /// <summary>What the disclosure button does next, naming the group it does it to.</summary>
    public string ToggleLabel => IsExpanded ? $"Collapse {Name}" : $"Expand {Name}";

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
        StatusTone? statusTone,
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
    /// A container's state, or nothing for the kinds that have none — an
    /// image, a volume, a network — whose tile is tinted by kind instead.
    /// </summary>
    public StatusTone? StatusTone { get; }

    public bool IsImage => Resource.Kind == DockerResourceKind.Image;

    public bool IsVolume => Resource.Kind == DockerResourceKind.Volume;

    public bool IsNetwork => Resource.Kind == DockerResourceKind.Network;

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
        ContainerTone(container),
        true,
        container.IsRunning,
        container.IsPaused,
        container);

    private static ViewModels.StatusTone ContainerTone(DockerContainerSummary container) =>
        container.IsRunning
            ? ViewModels.StatusTone.Success
            : container.IsPaused ? ViewModels.StatusTone.Warning : ViewModels.StatusTone.Neutral;

    public static DockerResourceItemViewModel From(DockerImageSummary image) => new(
        new DockerResourceReference(
            DockerResourceKind.Image,
            image.Id,
            $"{image.Repository}:{image.Tag}"),
        $"{image.Repository}:{image.Tag}",
        image.Size,
        image.Created,
        Symbol.Archive,
        null,
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
        null,
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
        null,
        false,
        false,
        false,
        null);
}
