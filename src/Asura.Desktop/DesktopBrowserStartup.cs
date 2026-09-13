using Asura.Application;
using Asura.Browser;
using Asura.Files;
using Asura.Infrastructure;
using Avalonia.Threading;

namespace Asura.Desktop;

/// <summary>
/// Keeps failed browser storage closed while the rest of the desktop starts.
/// Storage recovery can be retried before CEF starts; a failed CEF initialization
/// requires a fresh process because its native global state is not restartable.
/// </summary>
internal sealed class DesktopBrowserStartup(
    BrowserProfileStoragePaths paths,
    IApplicationEncryption encryption,
    IBrowserProfileAuthenticationResolver authentication) : IBrowserStartupRecovery, IBrowserProfileDataControl, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _retryGate = new(1, 1);
    private EncryptedBrowserProfileStateStore? _state;
    private CefBrowserProfileStore? _profiles;
    private BrowserEngineRuntimeOptions? _options;
    private Action? _restart;
    private readonly BrowserProfileStoragePaths _originalPaths = paths;
    private BrowserProfileStoragePaths _activePaths = paths;
    private bool _usingRecoveryBrowser;
    private bool _usingRecoveryWorkspace;
    private bool _initializationFailed;
    private bool _disposed;

    public string? Error { get; private set; }
    public bool RequiresRestart { get; private set; }
    public bool IsRunning { get; private set; }
    public bool CanRetry => !IsRunning;
    public event EventHandler? Changed;

    public void ConfigureRestart(Action restart) => _restart = restart;

    public void ReportRecoveryWorkspace()
    {
        _usingRecoveryWorkspace = true;
        RequiresRestart = true;
        SetError("Your saved connections and settings could not be opened. You can keep working in this recovery workspace.");
    }

    public bool RecoverProfile()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning || _initializationFailed)
            {
                return IsRunning;
            }
            if (TryRecover(_originalPaths))
            {
                _usingRecoveryBrowser = false;
                SetError(null);
                return true;
            }
            // Never replace an unreadable archive or seal fresh cookies into
            // it. Reuse a separate encrypted browser store on subsequent starts.
            for (var number = 1; ; number++)
            {
                var suffix = ".recovery-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var recoveryPaths = new BrowserProfileStoragePaths(_originalPaths.PersistentDirectory + suffix, _originalPaths.RuntimeDirectory + suffix);
                var alreadyExists = Directory.Exists(recoveryPaths.PersistentDirectory)
                    || Directory.Exists(recoveryPaths.RuntimeDirectory);
                if (TryRecover(recoveryPaths))
                {
                    _usingRecoveryBrowser = true;
                    SetError(alreadyExists ? null : "Saved website data could not be restored. You may need to sign in again.");
                    return true;
                }
                if (!alreadyExists)
                {
                    break;
                }
            }
            SetError("The browser is unavailable. You can use the other tools and retry after checking available disk space.");
            return false;
        }
    }

    private bool TryRecover(BrowserProfileStoragePaths selectedPaths)
    {
        try
        {
            _profiles?.Dispose();
            _state?.Dispose();
            _profiles = null;
            _state = new EncryptedBrowserProfileStateStore(selectedPaths.PersistentDirectory, encryption);
            var profiles = new CefBrowserProfileStore(authentication, _state, selectedPaths.RuntimeDirectory);
            if (!profiles.RecoverOrphanedRuntimeState())
            {
                profiles.Dispose();
                return false;
            }
            _profiles = profiles;
            _activePaths = selectedPaths;
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError("desktop.browser-profile-recovery.failed", error);
            return false;
        }
    }

    public bool Start(BrowserEngineRuntimeOptions options)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }
            _options = options;
            if (IsRunning || _initializationFailed)
            {
                return IsRunning;
            }
            if (_profiles is null)
            {
                SetError(Error ?? "Saved browser sessions need recovery. Retry the browser when this workspace has finished opening.");
                return false;
            }
            try
            {
                BrowserEngineRuntime.Initialize(new BrowserEngineRuntimeOptions(
                    _activePaths.RuntimeDirectory, options.LogFilePath, options.ProductVersion));
                IsRunning = true;
                RequiresRestart = _usingRecoveryBrowser;
                if (_usingRecoveryWorkspace)
                {
                    ReportRecoveryWorkspace();
                }
                else
                {
                    SetError(_usingRecoveryBrowser ? Error : null);
                }
                return true;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                SecretSafeDiagnosticProjection.WriteStandardError("desktop.cef-initialize.failed", error);
                _initializationFailed = true;
                RequiresRestart = true;
                SetError("The browser could not start. You can use the other tools and restart Asura to try the browser again.");
                return false;
            }
        }
    }

    public async Task RetryAsync(CancellationToken cancellationToken)
    {
        if (!await _retryGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            if (RequiresRestart)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    (_restart ?? throw new InvalidOperationException("Restart is not ready."))());
            }
            else if (await Task.Run(RecoverProfile, cancellationToken).ConfigureAwait(false)
                && _options is { } options)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Start(options));
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError("desktop.browser-retry.failed", error);
            SetError("The browser is still unavailable. You can use the other tools and try again.");
        }
        finally
        {
            _retryGate.Release();
        }
    }

    public CefBrowserProfileStore RequireRunningProfile() => IsRunning && _profiles is not null
        ? _profiles
        : throw new InvalidOperationException(Error ?? "The browser is starting. Try opening this panel again.");

    public BrowserProfileDataState ReadState(BrowserProfileSelection selection, long expectedRevision) =>
        !_disposed && _profiles is not null
            ? _profiles.ReadState(selection, expectedRevision)
            : throw new InvalidOperationException(Error ?? "Browser profile recovery has not finished.");

    public ValueTask<BrowserProfileClearResult> ClearAsync(BrowserProfileClearRequest request, CancellationToken cancellationToken) =>
        IsRunning && _profiles is not null
            ? _profiles.ClearAsync(request, cancellationToken)
            : ValueTask.FromResult(new BrowserProfileClearResult(BrowserProfileClearStatus.Failed, 0,
                "Saved browser data is kept while recovery is pending. Retry recovery before clearing it."));

    private void SetError(string? message)
    {
        Error = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            IsRunning = false;
            _profiles?.Dispose();
            _state?.Dispose();
        }
    }
}
