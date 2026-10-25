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
    IBrowserProfileAuthenticationResolver authentication,
    IBrowserHistory? history = null) : IBrowserStartupRecovery, IBrowserProfileDataControl, IDisposable
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
    private bool _initializationFailed;
    private bool _disposed;
    private bool _stopping;

    public string? Error { get; private set; }
    public bool RequiresRestart { get; private set; }
    public bool IsRunning { get; private set; }
    public bool CanRetry => !IsRunning;
    public event EventHandler? Changed;

    public void ConfigureRestart(Action restart) => _restart = restart;

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
            PrepareEncryptedHttpCache(selectedPaths);
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

    internal static void PrepareEncryptedHttpCache(BrowserProfileStoragePaths selectedPaths)
    {
        PrivateContentPathGuard.EnsurePrivateDirectory(selectedPaths.PersistentDirectory);
        var marker = selectedPaths.PersistentDirectory + ".native-http-cache-v1";
        if (File.Exists(marker))
        {
            PrivateContentPathGuard.ValidatePrivateFile(marker);
            return;
        }
        // Old versions used plaintext HTTP caches, including abandoned contexts.
        // Drop those once before enabling persistent encrypted caching. The
        // marker lives beside the store directory, whose entry allowlist stays strict.
        PurgeLegacyHttpCaches(selectedPaths.RuntimeDirectory, selectedPaths.RuntimeDirectory);
        using var committed = PrivateContentPathGuard.CreatePrivateFile(marker, FileShare.None);
    }

    private static void PurgeLegacyHttpCaches(string root, string directory)
    {
        var info = new DirectoryInfo(directory);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.Exists)
        {
            throw new IOException("Legacy browser storage contains a directory link.");
        }
        if (!info.Exists) { return; }
        foreach (var child in info.EnumerateDirectories())
        {
            if (child.LinkTarget is not null || child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Legacy browser storage contains a directory link.");
            }
            var relative = Path.GetRelativePath(root, child.FullName);
            if (BrowserDisposableCache.Contains(relative))
            {
                if (string.Equals(child.Name, "Cache", StringComparison.Ordinal)) { child.Delete(recursive: true); }
                continue;
            }
            PurgeLegacyHttpCaches(root, child.FullName);
        }
    }

    public void Configure(BrowserEngineRuntimeOptions options) => _options = options;

    // Only browser consumers wait here. Recovery and archive extraction never
    // occupy Avalonia's dispatcher, and concurrent tabs share the same engine.
    public async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        await _retryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping) { throw new InvalidOperationException("The browser is closing."); }
            if (IsRunning) { return; }
            if (!await Task.Run(RecoverProfile, CancellationToken.None).ConfigureAwait(false))
            {
                throw new InvalidOperationException(Error ?? "Saved browser data is unavailable.");
            }
            var options = _options ?? throw new InvalidOperationException("Browser startup is not configured.");
            if (!await Dispatcher.UIThread.InvokeAsync(() => Start(options)))
            {
                throw new InvalidOperationException(Error ?? "The browser could not start.");
            }
        }
        finally { _retryGate.Release(); }
    }

    public async Task BeginShutdownAsync()
    {
        await _retryGate.WaitAsync().ConfigureAwait(false);
        try { _stopping = true; }
        finally { _retryGate.Release(); }
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
                SetError(_usingRecoveryBrowser ? Error : null);
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
            if (_disposed || _stopping) { return; }
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

    public async ValueTask<BrowserProfileDataState> ReadStateAsync(
        BrowserProfileSelection selection, long expectedRevision, CancellationToken cancellationToken)
    {
        await EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadState(selection, expectedRevision), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BrowserProfileClearResult> ClearAsync(BrowserProfileClearRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BrowserProfileClearResult(BrowserProfileClearStatus.Cancelled, 0, "Clearing browser data was canceled.");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteTrace("browser.clear-startup.failed", error);
            return new BrowserProfileClearResult(BrowserProfileClearStatus.Failed, 0,
                "Saved browser data is kept while recovery is pending. Retry recovery before clearing it.");
        }
        var result = await Task.Run(async () =>
            await RequireRunningProfile().ClearAsync(request, cancellationToken).ConfigureAwait(false),
            CancellationToken.None).ConfigureAwait(false);
        if (result.Status == BrowserProfileClearStatus.Cleared
            && request.Categories.HasFlag(BrowserProfileDataCategory.AllWebContent) && history is not null)
        {
            try
            {
                await history.ClearAsync(request.Selection, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new BrowserProfileClearResult(BrowserProfileClearStatus.Cancelled, result.ClearedBytes,
                    "Browser data was cleared; clearing browsing history was canceled.");
            }
            catch (Exception exception)
            {
                SecretSafeDiagnosticProjection.WriteTrace("browser.history-clear.failed", exception);
                return new BrowserProfileClearResult(BrowserProfileClearStatus.Failed, result.ClearedBytes,
                    "Browser data was cleared, but Asura couldn't clear the browsing history. Try clearing it again.");
            }
        }
        return result;
    }

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
