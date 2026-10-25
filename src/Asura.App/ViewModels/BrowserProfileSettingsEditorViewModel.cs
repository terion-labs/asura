using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Asura.App.Controls;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed record BrowserProfileSharingOption(
    BrowserProfileSharing Sharing,
    string DisplayName,
    string Description);

public sealed record BrowserAuthenticationRouteOption(string Identity, string DisplayName);

public sealed record BrowserProfileItemViewModel(
    BrowserProfileId Id,
    long Revision,
    string Name,
    BrowserProfilePersistence Persistence,
    bool IsEnabled,
    bool IsBuiltIn)
{
    public string PersistenceLabel => Persistence switch
    {
        BrowserProfilePersistence.DurableMetadata =>
            "Encrypted session, restored between runs",
        BrowserProfilePersistence.PrivateSession =>
            "Private session, discarded when the panel closes",
        _ => "Unsupported policy",
    };

    public string StateLabel => IsEnabled ? "Enabled" : "Disabled";
}

/// <summary>
/// Manages named logical profiles. Profile definitions live in encrypted
/// application storage. Durable profiles restore Chromium cookies, local
/// storage, IndexedDB, cache, and navigation state from encrypted storage.
/// Private profiles discard that state when their panel closes.
/// </summary>
public sealed class BrowserProfileSettingsEditorViewModel : ObservableObject
{
    private readonly IBrowserProfilePreferences _preferences;
    private readonly IBrowserProfileDataControl? _dataControl;
    private readonly IDefinitionCatalog? _catalog;
    private readonly ISecretVault? _secretVault;
    private BrowserProfileItemViewModel? _selectedProfile;
    private string _newProfileName = string.Empty;
    private string? _creationError;
    private AuthenticationFormState _loadedAuthenticationForm;
    private string _authenticationHost = string.Empty;
    private string _authenticationPort = string.Empty;
    private string _authenticationRealm = string.Empty;
    private string _authenticationOriginScheme = "https";
    private BrowserAuthenticationRouteOption? _authenticationRoute;
    private BrowserAuthenticationScheme _authenticationScheme =
        BrowserAuthenticationScheme.Basic;
    private string _authenticationUsername = string.Empty;
    private string _authenticationPassword = string.Empty;
    private bool _hasAuthentication;
    private bool _isSavingAuthentication;
    private string _stateText =
        "Durable browser session data is encrypted between app runs.";
    private string? _operationStatus;
    private CancellationTokenSource? _clearCancellation;

    public BrowserProfileSettingsEditorViewModel(
        IBrowserProfilePreferences preferences,
        IBrowserProfileDataControl? dataControl = null,
        IDefinitionCatalog? catalog = null,
        ISecretVault? secretVault = null)
    {
        _preferences = preferences
            ?? throw new ArgumentNullException(nameof(preferences));
        _dataControl = dataControl;
        _catalog = catalog;
        _secretVault = secretVault;
        SharingOptions =
        [
            new(
                BrowserProfileSharing.Shared,
                "Shared Across Workspaces",
                "Legacy default panels share one encrypted browser session."),
            new(
                BrowserProfileSharing.PerWorkspace,
                "Separate by Workspace",
                "Legacy default panels keep a separate encrypted browser session for each workspace."),
        ];
        CreateDurableCommand = new AsyncActionCommand(
            () => CreateProfileAsync(BrowserProfilePersistence.DurableMetadata),
            () => _catalog is not null);
        CreatePrivateCommand = new AsyncActionCommand(
            () => CreateProfileAsync(BrowserProfilePersistence.PrivateSession),
            () => _catalog is not null);
        ToggleEnabledCommand = new AsyncActionCommand(
            ToggleEnabledAsync,
            () => _catalog is not null
                && SelectedProfile is { IsBuiltIn: false });
        DeleteCommand = new AsyncActionCommand(
            DeleteAsync,
            () => _catalog is not null
                && SelectedProfile is { IsBuiltIn: false });
        SaveAuthenticationCommand = new AsyncActionCommand(
            SaveAuthenticationAsync,
            CanEditAuthentication);
        DetachAuthenticationCommand = new AsyncActionCommand(
            DetachAuthenticationAsync,
            () => CanEditAuthentication() && HasAuthentication);
        ClearCookiesCommand = new AsyncActionCommand(
            () => ClearAsync(BrowserProfileDataCategory.Cookies),
            CanClearSelectedLiveProfile);
        ClearAuthenticationCommand = new AsyncActionCommand(
            () => ClearAsync(BrowserProfileDataCategory.HttpAuthentication),
            CanClearSelectedLiveProfile);
        ResetEphemeralContentCommand = new AsyncActionCommand(
            () => ClearAsync(BrowserProfileDataCategory.AllEphemeralWebContent),
            CanClearSelectedLiveProfile);
        CancelClearCommand = new AsyncActionCommand(
            () =>
            {
                CancelClear();
                return Task.CompletedTask;
            },
            () => IsClearing);
        ApplyCatalog(_catalog?.Snapshot ?? DefinitionCatalogSnapshot.Empty);
    }

    public ObservableCollection<BrowserProfileItemViewModel> Profiles { get; } = [];

    public IReadOnlyList<BrowserProfileSharingOption> SharingOptions { get; }

    public BrowserProfileSharingOption SelectedSharing
    {
        get => SharingOptions.Single(option =>
            option.Sharing == _preferences.Current.Sharing);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Sharing == _preferences.Current.Sharing)
            {
                return;
            }

            _ = _preferences.ApplyAsync(
                new BrowserProfileSettings(
                    value.Sharing,
                    _preferences.Current.DefaultProfileId),
                CancellationToken.None).AsTask();
            OnPropertyChanged();
        }
    }

    public BrowserProfileItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedDefaultProfile));
            LoadAuthenticationForm();
            RaiseCommandState();
            RefreshState();
        }
    }

    public BrowserProfileItemViewModel? SelectedDefaultProfile
    {
        get
        {
            var profileId = _preferences.Current.DefaultProfileId
                ?? BuiltInBrowserProfiles.Default.Id;
            return Profiles.SingleOrDefault(item => item.Id == profileId);
        }
        set
        {
            if (value is null || !value.IsEnabled)
            {
                return;
            }

            var selectedId = value.Id == BuiltInBrowserProfiles.Default.Id
                ? (BrowserProfileId?)null
                : value.Id;
            if (_preferences.Current.DefaultProfileId == selectedId)
            {
                return;
            }

            _ = _preferences.ApplyAsync(
                new BrowserProfileSettings(
                    _preferences.Current.Sharing,
                    selectedId),
                CancellationToken.None).AsTask();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The name the + popover offers for the next profile. Empty until the
    /// person types one, and emptied again once a profile is made, so the
    /// popover never offers the name of a profile that already exists.
    /// </summary>
    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (SetProperty(ref _newProfileName, value ?? string.Empty))
            {
                CreationError = null;
            }
        }
    }

    /// <summary>
    /// Why the last attempt to make a profile was refused, shown in the
    /// popover that asked for its name; null when there is nothing to say.
    /// </summary>
    public string? CreationError
    {
        get => _creationError;
        private set
        {
            if (SetProperty(ref _creationError, value))
            {
                OnPropertyChanged(nameof(HasCreationError));
            }
        }
    }

    public bool HasCreationError => CreationError is not null;

    /// <summary>
    /// A profile was made from <see cref="NewProfileName"/>; the popover that
    /// asked for the name closes on it, and only on it.
    /// </summary>
    public event EventHandler? ProfileCreated;

    public IReadOnlyList<BrowserAuthenticationScheme> AuthenticationSchemes { get; } =
        Enum.GetValues<BrowserAuthenticationScheme>();

    public IReadOnlyList<string> AuthenticationOriginSchemes { get; } = ["https", "http"];

    public ObservableCollection<BrowserAuthenticationRouteOption> AuthenticationRoutes { get; } = [];

    public string AuthenticationOriginScheme
    {
        get => _authenticationOriginScheme;
        set
        {
            if (SetProperty(ref _authenticationOriginScheme, value))
            {
                OnPropertyChanged(nameof(AuthenticationDefaultPort));
            }
        }
    }

    /// <summary>
    /// The port an empty Port field means: the protocol's own, shown as the
    /// field's placeholder.
    /// </summary>
    public string AuthenticationDefaultPort =>
        string.Equals(_authenticationOriginScheme, "http", StringComparison.OrdinalIgnoreCase) ? "80" : "443";

    public BrowserAuthenticationRouteOption? AuthenticationRoute
    {
        get => _authenticationRoute;
        set => SetProperty(ref _authenticationRoute, value);
    }

    public string AuthenticationHost
    {
        get => _authenticationHost;
        set => SetProperty(ref _authenticationHost, value ?? string.Empty);
    }

    public string AuthenticationPort
    {
        get => _authenticationPort;
        set => SetProperty(ref _authenticationPort, value ?? string.Empty);
    }

    public string AuthenticationRealm
    {
        get => _authenticationRealm;
        set => SetProperty(ref _authenticationRealm, value ?? string.Empty);
    }

    public BrowserAuthenticationScheme AuthenticationScheme
    {
        get => _authenticationScheme;
        set => SetProperty(ref _authenticationScheme, value);
    }

    public string AuthenticationUsername
    {
        get => _authenticationUsername;
        set => SetProperty(ref _authenticationUsername, value ?? string.Empty);
    }

    public string AuthenticationPassword
    {
        get => _authenticationPassword;
        set => SetProperty(ref _authenticationPassword, value ?? string.Empty);
    }

    /// <summary>
    /// Whether the HTTP authentication form holds edits Save Authentication
    /// has not stored. The form is a draft: Settings asks before leaving it.
    /// </summary>
    public bool HasUnsavedAuthentication =>
        SelectedProfile is { IsBuiltIn: false }
        && (!string.IsNullOrEmpty(AuthenticationPassword)
            || AuthenticationForm != _loadedAuthenticationForm);

    /// <summary>Puts the stored authentication back into the form.</summary>
    public void DiscardAuthenticationDraft() => LoadAuthenticationForm();

    private AuthenticationFormState AuthenticationForm => new(
        AuthenticationOriginScheme,
        AuthenticationRoute?.Identity,
        AuthenticationHost,
        AuthenticationPort,
        AuthenticationRealm,
        AuthenticationScheme,
        AuthenticationUsername);

    public bool HasAuthentication
    {
        get => _hasAuthentication;
        private set
        {
            if (SetProperty(ref _hasAuthentication, value))
            {
                (DetachAuthenticationCommand as AsyncActionCommand)?
                    .RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSavingAuthentication
    {
        get => _isSavingAuthentication;
        private set
        {
            if (!SetProperty(ref _isSavingAuthentication, value))
            {
                return;
            }

            (SaveAuthenticationCommand as AsyncActionCommand)?
                .RaiseCanExecuteChanged();
            (DetachAuthenticationCommand as AsyncActionCommand)?
                .RaiseCanExecuteChanged();
        }
    }

    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    public string? OperationStatus
    {
        get => _operationStatus;
        private set
        {
            if (SetProperty(ref _operationStatus, value))
            {
                OnPropertyChanged(nameof(HasOperationStatus));
            }
        }
    }

    public bool HasOperationStatus => OperationStatus is not null;

    public bool IsClearing => _clearCancellation is not null;

    public ICommand CreateDurableCommand { get; }

    public ICommand CreatePrivateCommand { get; }

    public ICommand ToggleEnabledCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand SaveAuthenticationCommand { get; }

    public ICommand DetachAuthenticationCommand { get; }

    public ICommand ClearCookiesCommand { get; }

    public ICommand ClearAuthenticationCommand { get; }

    public ICommand ResetEphemeralContentCommand { get; }

    public ICommand CancelClearCommand { get; }

    public void ApplyCatalog(DefinitionCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var stored in snapshot.BrowserProfiles
                     .OrderBy(item => item.Value.Name, StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add(new BrowserProfileItemViewModel(
                stored.Value.Id,
                stored.Revision,
                stored.Value.Name,
                stored.Value.Persistence,
                stored.Value.IsEnabled,
                stored.Value.Id == BuiltInBrowserProfiles.Default.Id));
        }

        SelectedProfile = Profiles.SingleOrDefault(item => item.Id == selectedId)
            ?? SelectedDefaultProfile
            ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedDefaultProfile));
    }

    private bool _usageRequested;

    public void RefreshUsage()
    {
        _usageRequested = true;
        RefreshState();
    }

    /// <summary>
    /// Makes a profile from <see cref="NewProfileName"/>. A refused name or a
    /// failed save is reported through <see cref="CreationError"/>, where the
    /// popover that asked for the name can show it, and leaves the name as
    /// typed; a profile that is made selects itself and empties the name.
    /// </summary>
    /// <returns>Whether the profile was made.</returns>
    public async Task<bool> CreateProfileAsync(BrowserProfilePersistence persistence)
    {
        if (_catalog is null)
        {
            return false;
        }

        CreationError = null;

        BrowserProfileDefinition definition;
        try
        {
            definition = new BrowserProfileDefinition(
                BrowserProfileId.New(),
                BrowserProfileDefinition.CurrentSchemaVersion,
                NewProfileName,
                persistence,
                persistence == BrowserProfilePersistence.PrivateSession
                    ? BrowserProfilePrivacyPolicy.PrivateSession
                    : BrowserProfilePrivacyPolicy.Strict);
        }
        catch (ArgumentException exception)
        {
            CreationError = FailureReason.Refusal(
                "browser-profile.create.refused",
                exception,
                $"Enter a profile name of up to {BrowserProfileDefinition.MaximumNameLength} characters without line breaks, then create it again.");
            return false;
        }

        var result = await _catalog.SaveBrowserProfileAsync(
            definition,
            expectedRevision: null,
            CancellationToken.None);
        if (!result.IsSuccess)
        {
            CreationError = result.Error!.Message;
            return false;
        }

        OperationStatus = persistence == BrowserProfilePersistence.PrivateSession
            ? "Private browser profile created. Its session is discarded when its panel closes."
            : "Browser profile created. Its session will be restored from encrypted storage.";
        ApplyCatalog(_catalog.Snapshot);
        SelectedProfile = Profiles.Single(item => item.Id == definition.Id);
        NewProfileName = string.Empty;
        ProfileCreated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task ToggleEnabledAsync()
    {
        if (_catalog is null || SelectedProfile is not { IsBuiltIn: false } selected)
        {
            return;
        }

        var stored = _catalog.Snapshot.BrowserProfiles
            .SingleOrDefault(item => item.Value.Id == selected.Id);
        if (stored is null)
        {
            OperationStatus = "That browser profile no longer exists.";
            return;
        }

        var profile = stored.Value;
        var result = await _catalog.SaveBrowserProfileAsync(
            new BrowserProfileDefinition(
                profile.Id,
                profile.SchemaVersion,
                profile.Name,
                profile.Persistence,
                profile.Privacy,
                profile.Authentication,
                !profile.IsEnabled),
            stored.Revision,
            CancellationToken.None);
        OperationStatus = result.IsSuccess
            ? profile.IsEnabled
                ? "Browser profile disabled. Open panels keep their pinned revision."
                : "Browser profile enabled for new panels."
            : result.Error!.Message;
        if (result.IsSuccess)
        {
            ApplyCatalog(_catalog.Snapshot);
        }
    }

    private async Task DeleteAsync()
    {
        if (_catalog is null || SelectedProfile is not { IsBuiltIn: false } selected)
        {
            return;
        }

        var stored = _catalog.Snapshot.BrowserProfiles
            .SingleOrDefault(item => item.Value.Id == selected.Id);
        var authentication = stored?.Value.Authentication;
        var result = await _catalog.DeleteAsync(
            new DefinitionKey(BrowserProfileDefinition.Kind, selected.Id.Value),
            selected.Revision,
            CancellationToken.None);
        OperationStatus = result.IsSuccess
            ? "Browser profile metadata deleted. Open panels keep their pinned revision until they close."
            : result.Error!.Message;
        if (result.IsSuccess)
        {
            if (authentication is not null && _secretVault is not null)
            {
                var removed = await DeleteAuthenticationSecretAsync(
                    selected.Id,
                    authentication.PasswordSecret);
                if (!removed)
                {
                    OperationStatus = $"The browser profile was deleted, but its old password couldn't be removed from {PlatformTerms.SecretStore}. Delete it in Settings › Security & Secrets.";
                }
            }

            ApplyCatalog(_catalog.Snapshot);
        }
    }

    private async Task SaveAuthenticationAsync()
    {
        if (!TryGetEditableStoredProfile(out var stored) || _secretVault is null)
        {
            return;
        }

        if (!TryParseAuthenticationPort(out var port))
        {
            OperationStatus = "HTTP authentication port must be empty or a number from 1 to 65535.";
            return;
        }

        var current = stored.Value.Authentication;
        var password = AuthenticationPassword;
        var replacesPassword = !string.IsNullOrEmpty(password);
        if (current is null && !replacesPassword)
        {
            OperationStatus = "Enter a password before attaching HTTP authentication.";
            return;
        }

        var passwordByteCount = Encoding.UTF8.GetByteCount(password);
        if (replacesPassword
            && passwordByteCount > BrowserHttpAuthentication.MaximumPasswordByteLength)
        {
            OperationStatus = $"HTTP authentication passwords can't be longer than {BrowserHttpAuthentication.MaximumPasswordByteLength} UTF-8 bytes.";
            return;
        }

        var reference = replacesPassword
            ? SecretRef.New()
            : current!.PasswordSecret;
        BrowserHttpAuthentication authentication;
        if (AuthenticationRoute is null)
        {
            OperationStatus = "Select the network route authorized to use this password.";
            return;
        }
        try
        {
            authentication = new BrowserHttpAuthentication(
                AuthenticationHost,
                port,
                AuthenticationRealm,
                AuthenticationScheme,
                AuthenticationUsername,
                reference,
                AuthenticationOriginScheme,
                AuthenticationRoute.Identity);
        }
        catch (ArgumentException exception)
        {
            OperationStatus = FailureReason.Refusal(
                "browser-profile.authentication.refused",
                exception,
                "Asura can’t save the sign-in with these values. Check the host, port, realm, and user name, then save again.");
            return;
        }

        IsSavingAuthentication = true;
        try
        {
            if (replacesPassword
                && !await CreateAuthenticationSecretAsync(
                    stored.Value,
                    reference,
                    password))
            {
                return;
            }

            var profile = stored.Value;
            var saved = await _catalog!.SaveBrowserProfileAsync(
                new BrowserProfileDefinition(
                    profile.Id,
                    profile.SchemaVersion,
                    profile.Name,
                    profile.Persistence,
                    profile.Privacy,
                    authentication,
                    profile.IsEnabled),
                stored.Revision,
                CancellationToken.None);
            if (!saved.IsSuccess)
            {
                if (replacesPassword)
                {
                    _ = await DeleteAuthenticationSecretAsync(profile.Id, reference);
                }

                OperationStatus = saved.Error!.Message;
                return;
            }

            AuthenticationPassword = string.Empty;
            OperationStatus = current is null
                ? $"HTTP authentication added. The password is stored only in {PlatformTerms.SecretStore}."
                : "HTTP authentication updated. Open browser panels keep their pinned profile revision.";
            if (replacesPassword
                && current is not null
                && !await DeleteAuthenticationSecretAsync(
                    profile.Id,
                    current.PasswordSecret))
            {
                OperationStatus = $"HTTP authentication was updated, but the previous password couldn't be removed from {PlatformTerms.SecretStore}. Delete it in Settings › Security & Secrets.";
            }

            ApplyCatalog(_catalog.Snapshot);
        }
        finally
        {
            IsSavingAuthentication = false;
        }
    }

    private async Task DetachAuthenticationAsync()
    {
        if (!TryGetEditableStoredProfile(out var stored)
            || stored.Value.Authentication is not { } authentication)
        {
            return;
        }

        IsSavingAuthentication = true;
        try
        {
            var profile = stored.Value;
            var saved = await _catalog!.SaveBrowserProfileAsync(
                new BrowserProfileDefinition(
                    profile.Id,
                    profile.SchemaVersion,
                    profile.Name,
                    profile.Persistence,
                    profile.Privacy,
                    authentication: null,
                    profile.IsEnabled),
                stored.Revision,
                CancellationToken.None);
            if (!saved.IsSuccess)
            {
                OperationStatus = saved.Error!.Message;
                return;
            }

            AuthenticationPassword = string.Empty;
            OperationStatus = "HTTP authentication detached from this profile.";
            if (_secretVault is not null
                && !await DeleteAuthenticationSecretAsync(
                    profile.Id,
                    authentication.PasswordSecret))
            {
                OperationStatus = $"HTTP authentication was removed, but its password couldn't be removed from {PlatformTerms.SecretStore}. Delete it in Settings › Security & Secrets.";
            }

            ApplyCatalog(_catalog.Snapshot);
        }
        finally
        {
            IsSavingAuthentication = false;
        }
    }

    private bool CanEditAuthentication() =>
        _catalog is not null
        && _secretVault is not null
        && !IsSavingAuthentication
        && SelectedProfile is { IsBuiltIn: false };

    private bool TryGetEditableStoredProfile(
        out StoredDefinition<BrowserProfileDefinition> stored)
    {
        if (_catalog is not null
            && SelectedProfile is { IsBuiltIn: false } selected
            && _catalog.Snapshot.BrowserProfiles.SingleOrDefault(item =>
                item.Value.Id == selected.Id) is { } found)
        {
            stored = found;
            return true;
        }

        stored = null!;
        OperationStatus = "That browser profile no longer exists.";
        return false;
    }

    private bool TryParseAuthenticationPort(out int? port)
    {
        port = null;
        if (string.IsNullOrWhiteSpace(AuthenticationPort))
        {
            return true;
        }

        if (!int.TryParse(
                AuthenticationPort,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed is <= 0 or > 65_535)
        {
            return false;
        }

        port = parsed;
        return true;
    }

    private async Task<bool> CreateAuthenticationSecretAsync(
        BrowserProfileDefinition profile,
        SecretRef reference,
        string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        using var material = SecretMaterial.TakeOwnership(bytes);
        var result = await _secretVault!.CreateAsync(
            new CreateSecretRequest(
                reference,
                $"{profile.Name} HTTP authentication",
                SecretKind.Password,
                AuthenticationScope(profile.Id),
                AuthenticationManagementPurpose(profile.Id)),
            material,
            CancellationToken.None);
        if (result is SecretVaultResult<SecretMetadata>.Failure failure)
        {
            OperationStatus = failure.Error.Message;
            return false;
        }

        return true;
    }

    private async Task<bool> DeleteAuthenticationSecretAsync(
        BrowserProfileId profileId,
        SecretRef reference)
    {
        var result = await _secretVault!.DeleteAsync(
            new DeleteSecretRequest(
                reference,
                AuthenticationScope(profileId),
                AuthenticationManagementPurpose(profileId)),
            CancellationToken.None);
        return result is SecretVaultResult<Unit>.Success;
    }

    private static SecretScope AuthenticationScope(BrowserProfileId profileId) =>
        new(SecretScopeKind.BrowserProfile, profileId.Value);

    private static SecretUsePurpose AuthenticationManagementPurpose(
        BrowserProfileId profileId) =>
        new(SecretUseKind.UserManagement, profileId.Value);

    private void LoadAuthenticationForm()
    {
        var selectedId = SelectedProfile?.Id;
        var authentication = selectedId is { } id
            ? _catalog?.Snapshot.BrowserProfiles
                .SingleOrDefault(item => item.Value.Id == id)
                ?.Value.Authentication
            : null;
        HasAuthentication = authentication is not null;
        AuthenticationHost = authentication?.Host ?? string.Empty;
        AuthenticationOriginScheme = authentication?.OriginScheme ?? "https";
        AuthenticationRoutes.Clear();
        AuthenticationRoutes.Add(new("local", "Direct"));
        if (_catalog is not null)
        {
            foreach (var stored in _catalog.Snapshot.Connections)
            {
                var connection = stored.Value.ResolveHostConnection(id => _catalog.Snapshot.Connections
                    .SingleOrDefault(candidate => candidate.Value.Id == id)?.Value);
                if (connection?.Endpoint is ConnectionEndpoint.Ssh)
                {
                    AuthenticationRoutes.Add(new(BrowserHttpAuthentication.SshRouteIdentity(connection), $"SSH · {connection.Name}"));
                }
            }
            foreach (var stored in _catalog.Snapshot.NetworkConnections)
            {
                AuthenticationRoutes.Add(new(BrowserHttpAuthentication.NetworkRouteIdentity(stored.Value), $"Workspace network · {stored.Value.Name}"));
            }
        }
        var routeIdentity = authentication?.RouteIdentity ?? "local";
        AuthenticationRoute = AuthenticationRoutes.SingleOrDefault(route => string.Equals(route.Identity, routeIdentity, StringComparison.Ordinal));
        AuthenticationPort = authentication?.Port?.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty;
        AuthenticationRealm = authentication?.Realm ?? string.Empty;
        AuthenticationScheme = authentication?.Scheme
            ?? BrowserAuthenticationScheme.Basic;
        AuthenticationUsername = authentication?.Username ?? string.Empty;
        AuthenticationPassword = string.Empty;
        _loadedAuthenticationForm = AuthenticationForm;
    }

    private readonly record struct AuthenticationFormState(
        string OriginScheme,
        string? RouteIdentity,
        string Host,
        string Port,
        string Realm,
        BrowserAuthenticationScheme Scheme,
        string Username);

    private async Task ClearAsync(BrowserProfileDataCategory category)
    {
        if (_dataControl is null || !TrySelectedBinding(out var selection, out var revision))
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _clearCancellation = cancellation;
        OnPropertyChanged(nameof(IsClearing));
        RaiseCommandState();
        try
        {
            var result = await _dataControl.ClearAsync(
                new BrowserProfileClearRequest(selection, revision, category),
                cancellation.Token);
            OperationStatus = result.Message;
        }
        finally
        {
            if (ReferenceEquals(_clearCancellation, cancellation))
            {
                _clearCancellation = null;
            }

            OnPropertyChanged(nameof(IsClearing));
            RaiseCommandState();
            RefreshState();
        }
    }

    private bool CanClearSelectedLiveProfile() =>
        _dataControl is not null
        && !IsClearing
        && SelectedProfile is { IsEnabled: true }
        && TrySelectedBinding(out _, out _);

    private bool TrySelectedBinding(
        out BrowserProfileSelection selection,
        out long revision)
    {
        if (SelectedProfile is not { } selected)
        {
            selection = default;
            revision = 0;
            return false;
        }

        if (selected.Persistence == BrowserProfilePersistence.PrivateSession
            || selected.Id == BuiltInBrowserProfiles.Default.Id
                && _preferences.Current.Sharing == BrowserProfileSharing.PerWorkspace)
        {
            selection = default;
            revision = 0;
            return false;
        }

        var partition = selected.Id == BuiltInBrowserProfiles.Default.Id
            ? BrowserProfileKey.Global
            : BrowserProfileKey.ForNamed(selected.Id.Value);
        selection = new BrowserProfileSelection(selected.Id, partition);
        revision = selected.Revision;
        return true;
    }

    private int _stateGeneration;

    private void RefreshState() => _ = RefreshStateAsync();

    private async Task RefreshStateAsync()
    {
        var generation = ++_stateGeneration;
        if (_dataControl is null)
        {
            StateText = "Clearing browser data isn’t available in this version of Asura.";
            return;
        }

        if (!TrySelectedBinding(out var selection, out var revision))
        {
            StateText = SelectedProfile?.Persistence
                == BrowserProfilePersistence.PrivateSession
                ? "Each private panel owns a different in-memory context. Close that panel to destroy its cookies, cache, and site data."
                : "The built-in profile has one encrypted browser session per workspace. Settings won't issue a broad cross-workspace clear.";
            return;
        }

        // This editor is constructed with every shell, even if settings never
        // opens. Only explicit settings activation may start browser recovery.
        if (!_usageRequested) { return; }
        try
        {
            var state = await _dataControl.ReadStateAsync(selection, revision, CancellationToken.None);
            if (generation != _stateGeneration) { return; }
            StateText = state.HasData
                ? $"{Count(state.ActiveContexts, "open browser session", "open browser sessions")}, "
                    + $"{Count(state.ActiveLeases, "panel using it", "panels using it")}, "
                    + $"{Count(state.StoredBytes, "encrypted byte", "encrypted bytes")} saved between runs."
                : "No saved or active web data for this profile.";
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (generation != _stateGeneration) { return; }
            StateText = "Saved browser data is kept while the browser is unavailable. Use Retry browser to recover it.";
        }
    }

    private void CancelClear() => _clearCancellation?.Cancel();

    private void RaiseCommandState()
    {
        foreach (var command in new[]
                 {
                     ToggleEnabledCommand,
                     DeleteCommand,
                     SaveAuthenticationCommand,
                     DetachAuthenticationCommand,
                     ClearCookiesCommand,
                     ClearAuthenticationCommand,
                     ResetEphemeralContentCommand,
                 }.OfType<AsyncActionCommand>())
        {
            command.RaiseCanExecuteChanged();
        }

        (CancelClearCommand as AsyncActionCommand)?.RaiseCanExecuteChanged();
    }

    private static string Count(long count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count:N0} {plural}";
}
