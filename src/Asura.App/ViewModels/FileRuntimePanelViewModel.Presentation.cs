using Asura.Application;
using Asura.Core;
using Avalonia.Media.Imaging;

namespace Asura.App.ViewModels;

/// <summary>
/// What the header and the listing say about the connection and its items:
/// the status mark, the search field's wording, and each row's icon.
/// </summary>
public sealed partial class FileRuntimePanelViewModel
{
    /// <summary>
    /// The header's status mark. It used to be a green dot whatever was going
    /// on; it now follows what the listing actually managed: waiting while the
    /// first folder is read, green once one is on screen, amber when the
    /// connection answered but refused this location, red when it could not be
    /// reached at all.
    /// </summary>
    public StatusTone StatusTone => ContentState switch
    {
        FileBrowserContentState.Loading => _hasLoadedListing ? StatusTone.Success : StatusTone.Neutral,
        FileBrowserContentState.Ready
            or FileBrowserContentState.EmptyLocation
            or FileBrowserContentState.SearchNoResults => StatusTone.Success,
        FileBrowserContentState.AccessRequired
            or FileBrowserContentState.Unsupported
            or FileBrowserContentState.RecoverableError => StatusTone.Warning,
        _ => StatusTone.Danger,
    };

    /// <summary>The same state in words, for the tooltip and VoiceOver.</summary>
    public string StatusDescription => StatusTone switch
    {
        StatusTone.Success => $"Connected to {ConnectionDisplayName}",
        StatusTone.Neutral => $"Connecting to {ConnectionDisplayName}",
        StatusTone.Warning => $"{ConnectionDisplayName}: {ContentPresentation.Title}",
        _ => $"{ConnectionDisplayName} unavailable",
    };

    /// <summary>
    /// One verb per meaning. A connection that searches on its side is
    /// searched; anything else filters the folder already read.
    /// </summary>
    public string FilterPlaceholder => SearchesOnProvider ? "Search" : "Filter";

    public string FilterAccessibleName => SearchesOnProvider
        ? "Search this location"
        : "Filter this folder";

    private bool SearchesOnProvider =>
        SelectedProfile?.Capabilities.HasFlag(FilePanelCapability.Search) == true;

    /// <summary>The listing is of the machine Asura is running on.</summary>
    public bool IsLocalProvider => string.Equals(
        SelectedProfile?.Id,
        BuiltInFileProviders.HomeId.Value,
        StringComparison.Ordinal);

    /// <summary>
    /// Where an entry lives on this machine, when the listing is of this
    /// machine and the entry resolves inside it; null otherwise. Used to ask
    /// the system for the entry's icon, to hand it to Finder in a drag, and to
    /// move it to the Trash.
    /// </summary>
    public string? TryGetLocalPath(FilePanelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsLocalProvider
            || entry.Location.Address is not FilePanelAddress.Hierarchical path)
        {
            return null;
        }

        var segments = path.Path.Segments.Select(segment => segment.Value).ToArray();
        var rootPath = LocalRootPath();
        var resolvedPath = Path.GetFullPath(Path.Combine([rootPath, .. segments]));
        return IsWithinDirectory(rootPath, resolvedPath) ? resolvedPath : null;
    }

    private Bitmap? ResolveIcon(FilePanelEntry entry) =>
        FileTypeIcons.NativeIcon(entry, TryGetLocalPath(entry));

    private void NotifyStatusMarkChanged()
    {
        OnPropertyChanged(nameof(StatusTone));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(FilterPlaceholder));
        OnPropertyChanged(nameof(FilterAccessibleName));
    }
}
