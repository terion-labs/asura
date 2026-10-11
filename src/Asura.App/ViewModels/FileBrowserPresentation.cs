using System.Windows.Input;
using Asura.Application;

namespace Asura.App.ViewModels;

public enum FileBrowserViewMode
{
    Details,
    List,
    Grid,
}

public enum FileEntrySortField
{
    Name,
    Kind,
    Size,
    Modified,
}

public enum FileEntrySortDirection
{
    Ascending,
    Descending,
}

public enum FileOperationIssueKind
{
    Validation,
    Configuration,
    PermissionDenied,
    AuthenticationRequired,
    Offline,
    QuotaExceeded,
    Conflict,
    Stale,
    Unsupported,
    Partial,
    Cancelled,
    Unexpected,
}

public enum FileBrowserContentState
{
    Loading,
    Ready,
    EmptyLocation,
    SearchNoResults,
    RecoverableError,
    AccessRequired,
    Unsupported,
    Unavailable,
}

/// <summary>
/// Provider-independent directory presentation. Keeping this state explicit prevents an empty
/// folder, an empty search, and an inaccessible location from collapsing into the same message.
/// </summary>
public sealed record FileBrowserContentPresentation(
    FileBrowserContentState State,
    string Title,
    string Message,
    string SuggestedAction,
    string AccessibleName,
    bool CanRetry)
{
    public bool IsError => State is FileBrowserContentState.RecoverableError
        or FileBrowserContentState.AccessRequired
        or FileBrowserContentState.Unsupported
        or FileBrowserContentState.Unavailable;

    public static FileBrowserContentPresentation Resolve(
        bool isLoading,
        bool hasLoadedListing,
        int loadedCount,
        int visibleCount,
        string filter,
        FileOperationIssue? issue,
        string? errorMessage,
        bool canCreateFolder,
        bool hasCurrentLocation)
    {
        if (isLoading || (!hasLoadedListing && issue is null))
        {
            return new FileBrowserContentPresentation(
                FileBrowserContentState.Loading,
                "Loading files",
                "Reading this location from the selected provider.",
                string.Empty,
                "Files panel loading",
                CanRetry: false);
        }

        if (issue is not null)
        {
            var state = issue.Kind switch
            {
                FileOperationIssueKind.PermissionDenied
                    or FileOperationIssueKind.AuthenticationRequired =>
                    FileBrowserContentState.AccessRequired,
                FileOperationIssueKind.Unsupported => FileBrowserContentState.Unsupported,
                _ when issue.CanRetry => FileBrowserContentState.RecoverableError,
                _ => FileBrowserContentState.Unavailable,
            };
            var accessibleName = state switch
            {
                FileBrowserContentState.AccessRequired => "Files panel access required",
                FileBrowserContentState.Unsupported => "Files panel location unsupported",
                FileBrowserContentState.RecoverableError => "Files panel recoverable error",
                _ => "Files panel unavailable",
            };
            return new FileBrowserContentPresentation(
                state,
                issue.Title,
                string.IsNullOrWhiteSpace(errorMessage) ? issue.Message : errorMessage,
                issue.SuggestedAction,
                accessibleName,
                issue.CanRetry && hasCurrentLocation);
        }

        if (loadedCount == 0)
        {
            return new FileBrowserContentPresentation(
                FileBrowserContentState.EmptyLocation,
                "This location is empty",
                "No files or folders are stored here.",
                canCreateFolder
                    ? "Create a folder here, or choose another location."
                    : "Choose another location.",
                "Files panel empty location",
                CanRetry: false);
        }

        if (visibleCount == 0 && !string.IsNullOrWhiteSpace(filter))
        {
            return new FileBrowserContentPresentation(
                FileBrowserContentState.SearchNoResults,
                "No matching items",
                $"No files or folders match “{filter.Trim()}”.",
                "Clear or change the filter to see this location's items.",
                "Files panel search has no results",
                CanRetry: false);
        }

        return new FileBrowserContentPresentation(
            FileBrowserContentState.Ready,
            string.Empty,
            string.Empty,
            string.Empty,
            "Files panel contents",
            CanRetry: false);
    }
}

public sealed class AsyncActionCommand(
    Func<Task> execute,
    Func<bool> canExecute) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        _ = parameter;
        return !_isExecuting && canExecute();
    }

    public async void Execute(object? parameter)
    {
        _ = parameter;
        if (!CanExecute(null))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A provider-independent problem that the File Viewer can render without interpreting
/// protocol-specific error strings.
/// </summary>
public sealed record FileOperationIssue(
    FileOperationIssueKind Kind,
    string Title,
    string Message,
    string SuggestedAction,
    bool CanRetry)
{
    public static FileOperationIssue FromProvider(FilePanelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            FilePanelErrorCode.AccessDenied => Create(
                FileOperationIssueKind.PermissionDenied,
                "Permission denied",
                error,
                "Check the item's permissions, or choose another location."),
            FilePanelErrorCode.AuthenticationRequired => Create(
                    FileOperationIssueKind.AuthenticationRequired,
                    "Authentication failed",
                    error,
                    "Check how this connection signs in, then try again.",
                    canRetry: true),
            FilePanelErrorCode.CertificateRejected
                or FilePanelErrorCode.HostKeyRejected
                or FilePanelErrorCode.HostKeyUnknown
                or FilePanelErrorCode.HostKeyChanged
                or FilePanelErrorCode.HostKeyStoreInvalid => Create(
                    FileOperationIssueKind.AuthenticationRequired,
                    "Trust required",
                    error,
                    "Review the connection's certificate or host key, then try again.",
                    canRetry: true),
            FilePanelErrorCode.Offline => Create(
                FileOperationIssueKind.Offline,
                "Connection offline",
                error,
                "Check the connection, then try again.",
                canRetry: true),
            FilePanelErrorCode.QuotaExceeded => Create(
                FileOperationIssueKind.QuotaExceeded,
                "Storage limit reached",
                error,
                "Free up space, or choose another destination."),
            FilePanelErrorCode.AlreadyExists => Create(
                FileOperationIssueKind.Conflict,
                "Destination already exists",
                error,
                "Choose Skip, Replace, or Keep Both, then try again.",
                canRetry: true),
            FilePanelErrorCode.SharingViolation => Create(
                FileOperationIssueKind.Conflict,
                "File is in use",
                error,
                "Quit the app or stop the process that's using the item, then try again.",
                canRetry: true),
            FilePanelErrorCode.DirectoryNotEmpty => Create(
                FileOperationIssueKind.Conflict,
                "Folder isn't empty",
                error,
                "Remove what's in the folder first, or choose a recursive delete that removes its contents too.",
                canRetry: true),
            FilePanelErrorCode.Conflict => Create(
                FileOperationIssueKind.Conflict,
                "File conflict",
                error,
                "Refresh, then choose whether to replace, rename, or skip.",
                canRetry: true),
            FilePanelErrorCode.PreconditionFailed => Create(
                FileOperationIssueKind.Stale,
                "Item changed",
                error,
                "Refresh, then try again.",
                canRetry: true),
            FilePanelErrorCode.UnsupportedCapability
                or FilePanelErrorCode.LinkNotAllowed
                or FilePanelErrorCode.RangeNotSatisfiable => Create(
                    FileOperationIssueKind.Unsupported,
                    "Not supported here",
                    error,
                    // The message already says what the location doesn't support.
                    "Try another action or location."),
            FilePanelErrorCode.PartialTransfer
                or FilePanelErrorCode.UnexpectedEndOfStream => Create(
                    FileOperationIssueKind.Partial,
                    "Not finished",
                    error,
                    "Try again, then check the destination before you continue.",
                    canRetry: true),
            FilePanelErrorCode.NotFound => Create(
                FileOperationIssueKind.Stale,
                "Item not found",
                error,
                "It may have been moved or deleted. Refresh to see what's in the folder now.",
                canRetry: true),
            FilePanelErrorCode.InvalidName => Create(
                FileOperationIssueKind.Validation,
                "Name not allowed",
                error,
                "Choose a shorter name without slashes or characters this location doesn't accept."),
            FilePanelErrorCode.NotDirectory
                or FilePanelErrorCode.IsDirectory
                or FilePanelErrorCode.InvalidLocation => Create(
                    FileOperationIssueKind.Validation,
                    "Location not available",
                    error,
                    "Check the path, or go up to the enclosing folder and choose the item there."),
            FilePanelErrorCode.OutsideRoot
                or FilePanelErrorCode.RootMutationNotAllowed => Create(
                    FileOperationIssueKind.Configuration,
                    "Outside this connection",
                    error,
                    "Choose a location inside the connection's root folder, or edit the connection's root."),
            FilePanelErrorCode.LimitExceeded => Create(
                FileOperationIssueKind.QuotaExceeded,
                "Too large",
                error,
                "Choose fewer or smaller items, then try again."),
            FilePanelErrorCode.UnknownProfile => Create(
                FileOperationIssueKind.Configuration,
                "File location not found",
                error,
                "Choose another file location, or set one up in Settings."),
            FilePanelErrorCode.Cancelled => Create(
                FileOperationIssueKind.Cancelled,
                "Canceled",
                error,
                "Try again when you're ready.",
                canRetry: true),
            _ => Create(
                FileOperationIssueKind.Unexpected,
                "File operation failed",
                error,
                error.Retryable
                    ? "Try again. If it keeps failing, check the connection in Settings."
                    : "Check the connection in Settings, then try again."),
        };
    }

    public static FileOperationIssue Validation(string message) => new(
        FileOperationIssueKind.Validation,
        "Check the file details",
        message,
        "Correct the highlighted value, then try again.",
        CanRetry: true);

    public static FileOperationIssue Configuration(string message) => new(
        FileOperationIssueKind.Configuration,
        "File location unavailable",
        message,
        "Choose a file location, or set one up in Settings.",
        CanRetry: false);

    public static FileOperationIssue Unexpected(string message) => new(
        FileOperationIssueKind.Unexpected,
        "File operation failed",
        message,
        "Try again. If it keeps failing, check the connection in Settings.",
        CanRetry: true);

    private static FileOperationIssue Create(
        FileOperationIssueKind kind,
        string title,
        FilePanelError error,
        string suggestedAction,
        bool? canRetry = null) => new(
            kind,
            title,
            error.Message,
            suggestedAction,
            canRetry ?? error.Retryable);
}

/// <summary>
/// Selected-entry metadata. <see cref="IsStatBacked"/> distinguishes a fresh provider stat
/// from the bounded metadata returned as part of a directory listing.
/// </summary>
public sealed class FileEntryMetadataViewModel
{
    public FileEntryMetadataViewModel(
        FilePanelEntry entry,
        bool isStatBacked,
        bool providerKeepsVersions = false)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        IsStatBacked = isStatBacked;
        _providerKeepsVersions = providerKeepsVersions;
    }

    private readonly bool _providerKeepsVersions;

    public FilePanelEntry Entry { get; }

    public bool IsStatBacked { get; }

    public string Name => Entry.Name;

    /// <summary>
    /// The kind in Finder's words: "Folder", "Alias", "Markdown document",
    /// "ZIP archive". On macOS the system's own description of the content
    /// type is used, so the words match Finder's Kind column exactly;
    /// elsewhere, and for a type the system has never seen, a table of common
    /// kinds answers.
    /// </summary>
    public string Kind => Entry.Kind switch
    {
        FilePanelEntryKind.Directory => "Folder",
        FilePanelEntryKind.Link => "Alias",
        _ => FileTypeIcons.KindDescription(Entry.Name),
    };

    public string Size => Entry.Kind == FilePanelEntryKind.Directory
        ? "Folder"
        : FileEntryViewModel.FormatSize(Entry.Size);

    public string Modified => DisplayDates.Format(Entry.LastModifiedAt);

    public string Location => FileLocationPresentation.Display(Entry.Location);

    /// <summary>
    /// The provider's version tag (an ETag, a revision), when the provider
    /// keeps versions people can tell apart. It is a hash to most people, so
    /// the panel keeps it behind a disclosure. Every other provider's tag is
    /// only a change stamp the panel uses to notice edits — a local file has
    /// one, Finder shows none — so the details say nothing about it.
    /// </summary>
    public string? Version => !_providerKeepsVersions
        || string.IsNullOrWhiteSpace(Entry.Location.Version)
        ? null
        : Entry.Location.Version;

    public bool HasVersion => Version is not null;
}
