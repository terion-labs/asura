using System.Globalization;
using System.Text.RegularExpressions;
using Asura.Application;
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
    /// <summary>A line a search shows around a match, without a match of its own.</summary>
    public bool IsContextLine { get; init; }

    /// <summary>
    /// When the engine wrote the line, in the reader's local time and
    /// culture. The engine sends UTC to the nanosecond; a line it sends in
    /// some other shape is shown as sent.
    /// </summary>
    public string DisplayTimestamp => ParseEngineTime(Timestamp) is { } moment
        ? moment.Ticks % TimeSpan.TicksPerSecond < TimeSpan.TicksPerMillisecond
            // A whole second carries no fraction: ",000" on every line of an
            // engine that writes whole seconds is noise, not precision.
            ? DisplayDates.FormatTime(moment)
            : DisplayDates.FormatPreciseTime(moment)
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

/// <summary>
/// Docker's own size text, rewritten by the application's one size
/// formatter. The engine writes container usage in binary units ("12.5MiB")
/// and image sizes in decimal ones ("123MB"), so one panel showed both MiB and
/// MB; each size is read in the unit it was written in and written again the
/// way the Files panel writes a size.
/// </summary>
internal static partial class DockerSizeText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        return SizePattern().Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || UnitBytes(match.Groups["unit"].Value) is not { } unit)
            {
                return match.Value;
            }

            var bytes = Math.Min(Math.Round(value * unit), long.MaxValue);
            return ByteSize.Format((long)Math.Max(0, bytes));
        });
    }

    private static double? UnitBytes(string unit) => unit switch
    {
        "B" => 1,
        "kB" or "KB" => 1e3,
        "MB" => 1e6,
        "GB" => 1e9,
        "TB" => 1e12,
        "KiB" => 1024d,
        "MiB" => 1024d * 1024,
        "GiB" => 1024d * 1024 * 1024,
        "TiB" => 1024d * 1024 * 1024 * 1024,
        _ => null,
    };

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)\s?(?<unit>[KMGT]iB|[kKMGT]B|B)\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SizePattern();
}

/// <summary>
/// One row of the Info view: the engine's field, with a time written in the
/// reader's culture and a byte count written as a size, so the view carries
/// one date style and one size style instead of whatever the engine sent.
/// </summary>
public sealed record DockerInspectionRow(string Name, string Value)
{
    public static DockerInspectionRow From(DockerInspectionProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var value = property.Value;
        if (DockerLogRowViewModel.ParseEngineTime(value) is { } moment)
        {
            // The engine writes "0001-01-01T00:00:00Z" for a time it never set.
            value = moment.Year <= 1 ? "Never" : DisplayDates.Format(moment);
        }
        else if (property.Name.EndsWith("Size", StringComparison.Ordinal)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
        {
            value = ByteSize.Format(bytes);
        }

        return new(property.Name, value);
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

    /// <summary>The container's usage figures, sizes in the application's units.</summary>
    public string CpuText => Container is { } container && !string.IsNullOrWhiteSpace(container.Cpu) ? container.Cpu : "—";

    public string MemoryText => DockerSizeText.Normalize(Container?.Memory);

    public string NetworkIoText => DockerSizeText.Normalize(Container?.NetworkIo);

    public string BlockIoText => DockerSizeText.Normalize(Container?.BlockIo);

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
        DockerSizeText.Normalize(image.Size),
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
        volume.SizeBytes is { } bytes ? ByteSize.Format(bytes) : DockerSizeText.Normalize(volume.Size),
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
