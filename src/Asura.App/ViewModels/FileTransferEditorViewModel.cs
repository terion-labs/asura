using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed class FileTransferEditorViewModel : ObservableObject
{
    private FileProviderProfileDescriptor _selectedDestinationProfile;
    private string _destination;
    private FilePanelTransferOperation _operation = FilePanelTransferOperation.Copy;
    private FilePanelConflictPolicy _conflictPolicy = FilePanelConflictPolicy.Fail;

    public FileTransferEditorViewModel(
        FilePanelEntry source,
        IReadOnlyList<FileProviderProfileDescriptor> profiles,
        string? preferredProfileId = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException("At least one destination provider is required.", nameof(profiles));
        }

        Profiles = Array.AsReadOnly(profiles.ToArray());
        _selectedDestinationProfile = Profiles
            .FirstOrDefault(profile => string.Equals(profile.Id, preferredProfileId, StringComparison.Ordinal))
            ?? Profiles[0];
        _destination = FileLocationPresentation.ChildDisplay(
            _selectedDestinationProfile,
            source.Name);
    }

    public FilePanelEntry Source { get; }

    public string SourceLocation => FileLocationPresentation.Display(Source.Location);

    public IReadOnlyList<FileProviderProfileDescriptor> Profiles { get; }

    /// <summary>The operation pop-up's items, named in words rather than enum values.</summary>
    public IReadOnlyList<FileTransferOperationOption> OperationOptions { get; } =
        [.. Enum.GetValues<FilePanelTransferOperation>()
            .Select(value => new FileTransferOperationOption(value, FileTransferText.Operation(value)))];

    /// <summary>The "When destination exists" pop-up's items.</summary>
    public IReadOnlyList<FileConflictPolicyOption> ConflictPolicyOptions { get; } =
        [.. Enum.GetValues<FilePanelConflictPolicy>()
            .Select(value => new FileConflictPolicyOption(value, FileTransferText.ConflictPolicy(value)))];

    public FileProviderProfileDescriptor SelectedDestinationProfile
    {
        get => _selectedDestinationProfile;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!Profiles.Any(profile => string.Equals(profile.Id, value.Id, StringComparison.Ordinal)))
            {
                throw new ArgumentException("The destination profile isn't available.", nameof(value));
            }

            if (SetProperty(ref _selectedDestinationProfile, value))
            {
                Destination = FileLocationPresentation.ChildDisplay(value, Source.Name);
            }
        }
    }

    public string Destination
    {
        get => _destination;
        set => SetProperty(ref _destination, value);
    }

    public FilePanelTransferOperation Operation
    {
        get => _operation;
        set
        {
            if (SetProperty(ref _operation, value))
            {
                OnPropertyChanged(nameof(SelectedOperationOption));
            }
        }
    }

    public FileTransferOperationOption SelectedOperationOption
    {
        get => OperationOptions.First(option => option.Value == Operation);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Operation = value.Value;
        }
    }

    public FilePanelConflictPolicy ConflictPolicy
    {
        get => _conflictPolicy;
        set
        {
            if (SetProperty(ref _conflictPolicy, value))
            {
                OnPropertyChanged(nameof(SelectedConflictPolicyOption));
            }
        }
    }

    public FileConflictPolicyOption SelectedConflictPolicyOption
    {
        get => ConflictPolicyOptions.First(option => option.Value == ConflictPolicy);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ConflictPolicy = value.Value;
        }
    }

    public FilePanelTransferRequest CreateRequest()
    {
        var destination = FileLocationPresentation.Parse(
            SelectedDestinationProfile,
            Destination);
        if (destination == Source.Location.WithVersion(null))
        {
            throw FailureReason.Readable(new ArgumentException(
                "The destination is the item itself. Choose a different destination."));
        }

        return new FilePanelTransferRequest(
            Source.Location,
            destination,
            Operation,
            ConflictPolicy);
    }
}

public sealed record FileTransferOperationOption(FilePanelTransferOperation Value, string Label);

public sealed record FileConflictPolicyOption(FilePanelConflictPolicy Value, string Label);

/// <summary>
/// A transfer's operation, conflict rule, and state in words. The transfer
/// dialog and the transfer list used to show the enum values themselves
/// ("KeepBoth", "Fail").
/// </summary>
internal static class FileTransferText
{
    public static string Operation(FilePanelTransferOperation operation) => operation switch
    {
        FilePanelTransferOperation.Copy => "Copy",
        FilePanelTransferOperation.Move => "Move",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    public static string ConflictPolicy(FilePanelConflictPolicy policy) => policy switch
    {
        FilePanelConflictPolicy.Fail => "Stop the Transfer",
        FilePanelConflictPolicy.Skip => "Skip the Item",
        FilePanelConflictPolicy.Replace => "Replace It",
        FilePanelConflictPolicy.KeepBoth => "Keep Both",
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
    };

    public static string State(FilePanelTransferState state) => state switch
    {
        FilePanelTransferState.Queued => "Queued",
        FilePanelTransferState.Running => "In progress",
        FilePanelTransferState.Completed => "Done",
        FilePanelTransferState.Failed => "Failed",
        FilePanelTransferState.Cancelled => "Canceled",
        FilePanelTransferState.Skipped => "Skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
