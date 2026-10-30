using Asura.App;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Browser;
using Asura.ConnectionBackend;
using Asura.Infrastructure;
using Asura.SessionHost;
using Asura.Terminal;
using Asura.Updates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dock.Settings;
using Microsoft.Extensions.DependencyInjection;
using AsuraApplication = Asura.App.App;

namespace Asura.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], ConnectionBackendCommand.Marker, StringComparison.Ordinal))
        {
            Environment.ExitCode = args.Length == 3
                ? ConnectionBackendCommand.RunAsync(args[1], args[2]).GetAwaiter().GetResult() : 64;
            return;
        }

        if (args.Length == 1 && string.Equals(args[0], DatabaseOperationWorker.Marker, StringComparison.Ordinal))
        {
            Environment.ExitCode = DatabaseOperationWorker.RunChildAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Length == 1 && string.Equals(args[0], DatabaseDiagramWorker.Marker, StringComparison.Ordinal))
        {
            Environment.ExitCode = DatabaseDiagramWorker.RunAsync().GetAwaiter().GetResult();
            return;
        }

        if (WorkspaceSshCommand.IsInvocation(args))
        {
            Environment.ExitCode = WorkspaceSshCommand
                .RunAsync(args, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return;
        }

        if (WorkspaceSocksProxyCommand.IsInvocation(args))
        {
            Environment.ExitCode = WorkspaceSocksProxyCommand
                .RunAsync(args, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return;
        }

        if (ConnectionCredentialProcessHost.IsPrivateHelperInvocation(args))
        {
            Environment.ExitCode = ConnectionCredentialProcessHost
                .TryRunAsync(args, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult()
                ?? 1;
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], DesktopStartupFailurePresenter.RecoveryUiSwitch, StringComparison.Ordinal))
        {
            DesktopStartupFailurePresenter.TryShow("Asura needs to recover",
                "The workspace stopped unexpectedly. Your saved profile is kept.", args[1..]);
            return;
        }

        try
        {
            var cefExitCode = BrowserEngineRuntime.ExecuteSubprocess();
            if (cefExitCode >= 0)
            {
                Environment.ExitCode = cefExitCode;
                return;
            }
        }
        catch (Exception error)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.cef-subprocess.failed",
                error);
            if (args.Any(argument => argument.StartsWith("--type=", StringComparison.Ordinal)))
            {
                Environment.ExitCode = 1;
                return;
            }
            // A missing browser runtime must not prevent the desktop from
            // opening. Browser startup will report its own recoverable error.
        }

        if (args.Contains(
                BrowserNativeCheckboxProbe.CommandLineSwitch,
                StringComparer.Ordinal))
        {
            Environment.ExitCode = BrowserNativeCheckboxProbe.Run();
            return;
        }

        // asura open PATH [--as VIEW]: handed to the running instance, or
        // opened by this one once it is up.
        OpenFolderCommand.Invocation? openFolder = null;
        if (OpenFolderCommand.IsInvocation(args))
        {
            try
            {
                openFolder = OpenFolderCommand.Parse(args, Environment.CurrentDirectory);
            }
            catch (ArgumentException error)
            {
                // The command's own answer to the person who typed it, not a
                // diagnostic: standard error carries only the audited
                // projections, and the exit status says it failed.
                Console.Out.WriteLine("asura: " + error.Message);
                Console.Out.WriteLine(OpenFolderCommand.Usage);
                Environment.ExitCode = 64;
                return;
            }

            args = [];
        }

        DesktopProfileConfiguration profile;
        try
        {
            profile = DesktopProfileConfiguration.FromCommandLine(args);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError("desktop.profile-selection.rejected", error);
            DesktopStartupFailurePresenter.TryShow("Choose a workspace to recover",
                "The selected workspace could not be opened. Your saved data is kept.", args);
            Environment.ExitCode = 2;
            return;
        }
        if (profile.IsThrowaway)
        {
            args = [.. args.Select(argument => string.Equals(argument, DesktopProfileConfiguration.ThrowawaySwitch, StringComparison.Ordinal)
                ? DesktopProfileConfiguration.ResumeThrowawaySwitch : argument)];
        }
        if (!profile.IsThrowaway)
        {
            VelopackStartup.Run(args);
        }

        // macOS will only host the UI on the process's first thread, and an
        // async Main leaves it at the first await that does real work —
        // resolving an encryption key from the keychain, say. So this thread
        // never awaits: it waits the asynchronous preparation out, starts
        // the lifetime exactly where the platform demands it, then waits the
        // finalization out the same way. Private credential helpers have
        // already exited without loading CEF; normal runs and CEF --type
        // subprocesses preserve CEF's required first-dispatch ordering.
        var originalArguments = args;
        var profileAlreadyExists = Directory.Exists(profile.Data.DataDirectory);
        var prepared = PrepareAsync(profile, openFolder).GetAwaiter().GetResult();
        // Why the person's own profile did not open, for the recovery notice.
        // A recovery workspace asked for by name has no such reason.
        var profileFailure = !profile.IsRecovery && prepared is StartupPreparation.Failed opening
            ? opening.Message
            : null;
        while (prepared is StartupPreparation.Failed)
        {
            // A damaged profile is preserved in place. Open another persistent
            // workspace automatically, with independent storage and keys.
            args = DesktopProfileConfiguration.NextRecoveryArguments(args);
            profile = DesktopProfileConfiguration.FromCommandLine(args);
            profileAlreadyExists = Directory.Exists(profile.Data.DataDirectory);
            prepared = PrepareAsync(profile, openFolder: null).GetAwaiter().GetResult();
            if (prepared is StartupPreparation.Failed && !profileAlreadyExists)
            {
                // If even a new workspace cannot open, another empty directory
                // cannot repair unavailable storage or the operating-system vault.
                break;
            }
        }
        if (prepared is StartupPreparation.Failed failure)
        {
            // Preparation resumes on worker threads. Avalonia, including an
            // error-only lifetime, must start on this original macOS thread.
            DesktopStartupFailurePresenter.TryShow(
                "Asura could not open this profile",
                failure.Message,
                args);
            Environment.ExitCode = 1;
            return;
        }
        if (prepared is not StartupPreparation.Ready ready)
        {
            return;
        }

        var (services, instanceCoordinator) = ready;
        var browserStartup = services.GetRequiredService<DesktopBrowserStartup>();
        string[]? restartArguments = null;
        MainWindowViewModel? mainWindowViewModel = null;
        INativeNotificationService? nativeNotifications = null;
        var finalized = false;
        var browserSealed = false;
        try
        {
            try
            {
                instanceCoordinator.RegisterActivationHandler(OnInstanceActivated);
                var lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    Args = args,
                    ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose,
                };
                var updateShutdown = services
                    .GetRequiredService<DesktopUpdateShutdown>();
                updateShutdown.Attach(
                    lifetime,
                    async cancellationToken =>
                    {
                        await services.GetRequiredService<AsuraApplication>()
                            .PrepareForUpdateRestartAsync(cancellationToken);
                        await browserStartup.BeginShutdownAsync();
                        TeardownPresentationOrReport(mainWindowViewModel);
                        await FinalizeAsync(services);
                        finalized = true;
                        if (browserStartup.IsRunning && !browserSealed)
                        {
                            var profiles = browserStartup.RequireRunningProfile();
                            if (!await BrowserEngineRuntime.ShutdownAsync(profiles))
                            {
                                throw new IOException("The browser did not finish closing.");
                            }
                            Func<string, string, CancellationToken, Task>? copy = OperatingSystem.IsMacOS()
                                ? new BrowserEngineSnapshotCopy(services.GetRequiredService<IConnectionCommandRunner>()).CopyAsync : null;
                            browserSealed = await BrowserEngineRuntime.SealStateAfterShutdownAsync(profiles, copy, CancellationToken.None);
                            if (!browserSealed) { throw new IOException("Saved browser data could not be secured."); }
                        }
                    });
                browserStartup.ConfigureRestart(() =>
                {
                    restartArguments = originalArguments;
                    updateShutdown.Request();
                });
                var encryption = services.GetRequiredService<ApplicationEncryptionRuntime>();
                // Restored views can request CEF during framework setup or as
                // soon as PIN unlock completes. Options need no unlocked keys;
                // publish them first while initialization remains lazy.
                browserStartup.Configure(CreateBrowserEngineOptions(services));
                BrowserEngineRuntime.Configure(BuildAvaloniaApp(services))
                    .SetupWithLifetime(lifetime);
                services.GetRequiredService<AsuraApplication>().RequestDesktopShutdown = updateShutdown.Request;
                services.GetRequiredService<AsuraApplication>().InstallCommandLineTool = CommandLineToolInstaller.InstallAsync;
                if (openFolder is { } startupFolder)
                {
                    services.GetRequiredService<AsuraApplication>().RequestOpenFolder(startupFolder.Request);
                }
                nativeNotifications =
                    services.GetRequiredService<INativeNotificationService>();
                nativeNotifications.Activated += OnNativeNotificationActivated;
                mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
                if (profile.IsRecovery)
                {
                    // Every window says so, not just the first, and every
                    // time — a recovery workspace reused from an earlier
                    // fall-back looks just like all the workspaces being gone.
                    var application = services.GetRequiredService<AsuraApplication>();
                    application.WindowTitleQualifier = "Recovery Workspace";
                    var profileArguments = DesktopProfileConfiguration.ProfileArguments(originalArguments);
                    application.RecoveryWorkspace = new RecoveryWorkspaceNotice(
                        profileFailure,
                        () =>
                        {
                            restartArguments = profileArguments;
                            updateShutdown.Request();
                        });
                }
                lifetime.Exit += (_, _) =>
                    TeardownPresentationOrReport(mainWindowViewModel);
                void InitializeBrowserRuntime()
                {
                    browserStartup.Configure(CreateBrowserEngineOptions(services));
                }

                if (encryption.AwaitingUnlock)
                {
                    DeferredStartupCoordinator.Arm(
                        services.GetRequiredService<IStartupProtection>(),
                        () => InitializeProfileCoreAsync(services),
                        InitializeBrowserRuntime,
                        _ =>
                        {
                            restartArguments = DesktopProfileConfiguration.NextRecoveryArguments(args);
                            updateShutdown.Request();
                        });
                }

                // Attached here, not in App: headless tests run App too, and
                // they must still see a UI exception.
                UiFailureContainment.Attach(Dispatcher.UIThread);
                Environment.ExitCode = lifetime.Start(args);
                // Exit normally performs this while the dispatcher still pumps.
                // The process's STA thread is a safe fallback if the lifetime
                // returns without raising Exit.
                TeardownPresentationOrReport(mainWindowViewModel);
                if (!finalized) { FinalizeAsync(services).GetAwaiter().GetResult(); }
            }
            catch (Exception error)
            {
                SecretSafeDiagnosticProjection.WriteStandardError(
                    "desktop.runtime.failed",
                    error);
                // Avalonia cannot be set up twice in one process. Show recovery
                // in a fresh process after releasing this profile's ownership.
                restartArguments ??= profileAlreadyExists
                    ? DesktopProfileConfiguration.NextRecoveryArguments(args)
                    : [DesktopStartupFailurePresenter.RecoveryUiSwitch, .. args];
                Environment.ExitCode = 1;
            }
            finally
            {
                try
                {
                    services.GetRequiredService<DesktopUpdateShutdown>().Detach();
                    instanceCoordinator.StopAcceptingActivations();
                    nativeNotifications?.Activated -= OnNativeNotificationActivated;

                    // Startup and finalization failures also converge here before
                    // CEF closes browsers and stops its message pump.
                    TeardownPresentationOrReport(mainWindowViewModel);
                    QuiescePresentationOrReport(services);
                    if (browserStartup.IsRunning && !browserSealed)
                    {
                        var profiles = browserStartup.RequireRunningProfile();
                        if (!BrowserEngineRuntime.Shutdown(profiles))
                        {
                            Environment.ExitCode = 1;
                        }
                        else
                        {
                            Func<string, string, CancellationToken, Task>? copyEngineSnapshot =
                                OperatingSystem.IsMacOS()
                                    ? new BrowserEngineSnapshotCopy(services.GetRequiredService<IConnectionCommandRunner>()).CopyAsync
                                    : null;
                            if (!BrowserEngineRuntime.SealStateAfterShutdownAsync(profiles, copyEngineSnapshot)
                                    .GetAwaiter().GetResult())
                            {
                                Environment.ExitCode = 1;
                            }
                        }
                    }

                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    SecretSafeDiagnosticProjection.WriteStandardError("desktop.shutdown.failed", error);
                    Environment.ExitCode = 1;
                }
                try
                {
                    services.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    SecretSafeDiagnosticProjection.WriteStandardError("desktop.service-dispose.failed", error);
                    Environment.ExitCode = 1;
                }
            }
        }
        finally
        {
            try
            {
                instanceCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                SecretSafeDiagnosticProjection.WriteStandardError("desktop.instance-dispose.failed", error);
                Environment.ExitCode = 1;
            }
        }
        if (restartArguments is not null)
        {
            try
            {
                DesktopStartupFailurePresenter.Launch(restartArguments);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                SecretSafeDiagnosticProjection.WriteStandardError("desktop.restart.failed", error);
                Environment.ExitCode = 1;
            }
        }
    }

    private static void TeardownPresentationOrReport(
        MainWindowViewModel? mainWindowViewModel)
    {
        if (mainWindowViewModel is null)
        {
            return;
        }

        try
        {
            mainWindowViewModel.TeardownPresentationForShutdown();
        }
        catch (Exception error)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.presentation-teardown.failed",
                error);
            Environment.ExitCode = 1;
        }
    }

    // Quiescing normally finishes on Exit while the dispatcher still pumps;
    // here it only confirms that. When the UI loop ended without Exit, work
    // that needs the dispatcher can never finish, and waiting for it froze
    // Asura until it was force-quit, so the wait is bounded.
    private static readonly TimeSpan PresentationQuiescenceTimeout = TimeSpan.FromSeconds(15);

    private static void QuiescePresentationOrReport(IServiceProvider services)
    {
        try
        {
            QuiescePresentationAsync(
                    services.GetRequiredService<QuickTerminalController>(),
                    services.GetRequiredService<AsuraApplication>(),
                    CancellationToken.None)
                .WaitAsync(PresentationQuiescenceTimeout)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception error)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.presentation-quiesce.failed",
                error);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// Everything that must happen before the window can exist, on whatever
    /// threads it needs. Null means an existing instance was activated. Errors
    /// are returned to Main for presentation on the process's original thread.
    /// </summary>
    private static async Task<StartupPreparation?> PrepareAsync(DesktopProfileConfiguration profile, OpenFolderCommand.Invocation? openFolder)
    {
        ConfigureDockDiagnostics();

        SingleInstanceStartResult instanceStart;
        try
        {
            instanceStart = await SingleInstanceCoordinator.StartAsync(
                DesktopStorageMigration.CoordinationDirectory(profile),
                openFolder?.Request.Encode(),
                CancellationToken.None);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError("desktop.instance-start.failed", error);
            return new StartupPreparation.Failed("The workspace could not be opened. Try again or use a separate recovery workspace.");
        }
        if (instanceStart is SingleInstanceStartResult.ExistingInstanceActivated)
        {
            return null;
        }
        if (instanceStart is SingleInstanceStartResult.Failure instanceFailure)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.startup.failed",
                SecretSafeDiagnosticKind.Unexpected);
            return new StartupPreparation.Failed(instanceFailure.Error.Message);
        }

        var instanceCoordinator =
            ((SingleInstanceStartResult.Primary)instanceStart).Coordinator;
        if (openFolder is { WasLaunched: false } fromShell && OperatingSystem.IsMacOS())
        {
            // No Asura is running. A shell's command starts it through the
            // system's launcher, as the Dock would, and ends here; the app
            // it starts opens the folder.
            await instanceCoordinator.DisposeAsync();
            if (await OpenFolderCommand.TryLaunchApplicationAsync(fromShell.Request))
            {
                return null;
            }

            return new StartupPreparation.Failed("Asura could not be started to open the folder.");
        }

        ServiceProvider? services = null;
        try
        {
            DesktopStorageMigration.Migrate(profile);
            MacOsAppGroupStorage.ConfigureTemporaryStorage(profile.IsThrowaway ? profile.Data.DataDirectory : null);
            services = DesktopComposition.CreateServiceProvider(profile);
            // Before anything opens the configuration database: an encrypted
            // database needs its key in hand for the very first connection —
            // from the OS keystore, or, when protection sealed the keys under
            // the PIN, from the unlock that has not happened yet.
            var protection = services.GetRequiredService<IStartupProtection>()
                as StartupProtectionRuntime;
            var encryption = services.GetRequiredService<ApplicationEncryptionRuntime>();
            await encryption.InitializeAsync(
                wrappedKeysPending: protection?.HoldsWrappedKeys ?? false,
                CancellationToken.None);
            if (encryption.StartupError is { } encryptionError)
            {
                SecretSafeDiagnosticProjection.WriteStandardError(
                    "desktop.profile-open.failed",
                    SecretSafeDiagnosticKind.Unexpected);
                Abandon();
                return new StartupPreparation.Failed(encryptionError);
            }

            Task<string?> InitializeProfileAsync() => InitializeProfileCoreAsync(services);

            if (!encryption.AwaitingUnlock
                && await InitializeProfileAsync() is { } profileError)
            {
                Abandon();
                return new StartupPreparation.Failed(profileError);
            }

            return new StartupPreparation.Ready(services, instanceCoordinator);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError("desktop.profile-prepare.failed", error);
            Abandon();
            return new StartupPreparation.Failed("Saved data could not be opened. Your profile is kept. Try again or use a separate recovery workspace.");
        }

        void Abandon()
        {
            try
            {
                services?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                SecretSafeDiagnosticProjection.WriteStandardError("desktop.abandoned-profile-dispose.failed", error);
            }
            finally
            {
                services = null;
                try
                {
                    instanceCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    SecretSafeDiagnosticProjection.WriteStandardError("desktop.abandoned-instance-dispose.failed", error);
                }
            }
        }
    }

    private abstract record StartupPreparation
    {
        public sealed record Ready(
            ServiceProvider Services,
            SingleInstanceCoordinator Coordinator) : StartupPreparation;

        public sealed record Failed(string Message) : StartupPreparation;
    }

    private static async Task<string?> InitializeProfileCoreAsync(IServiceProvider services)
    {
        // Browser failures are isolated from configuration and terminal startup.
        // The shell exposes a retry action while the saved browser data stays closed.
        // Browser recovery is deferred until a browser surface is requested.

        var runStore = services.GetRequiredService<IApplicationRunStore>();
        var startResult = await runStore.BeginRunAsync(CancellationToken.None);
        if (!startResult.IsSuccess)
        {
            ReportLifecycleFailure(
                "initialize its recovery marker",
                startResult.Error!);
            return $"Local application data is unavailable ({startResult.Error!.Code}).";
        }

        var startupState = services.GetRequiredService<ApplicationStartupState>();
        startupState.Initialize(startResult.Value!);

        var agentAuditRecovery = await services
            .GetRequiredService<AgentAuditRecovery>()
            .RecoverAsync(CancellationToken.None);
        if (!agentAuditRecovery.IsSuccess)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.agent-audit-recovery.failed",
                SecretSafeDiagnosticKind.Unexpected);
            return "The local agent audit trail is unavailable or invalid.";
        }

        var catalog = services.GetRequiredService<IDefinitionCatalog>();
        var catalogResult = await catalog.InitializeAsync(CancellationToken.None);
        if (!catalogResult.IsSuccess)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.definition-catalog.load-failed",
                SecretSafeDiagnosticKind.Unexpected);
            return $"Saved connections and workspaces are unavailable "
                + $"({catalogResult.Error!.Code}).";
        }

        // Preference failures do not prevent startup. Unreadable agent
        // policy is surfaced in settings with capabilities disabled.
        await services.GetRequiredService<SqliteFilePreviewPreferences>()
            .InitializeAsync(CancellationToken.None);
        await services.GetRequiredService<SqliteBrowserProfilePreferences>()
            .InitializeAsync(CancellationToken.None);
        await services.GetRequiredService<AgentPolicyCoordinator>()
            .InitializeAsync(CancellationToken.None);
        await services.GetRequiredService<Asura.Mcp.Server.LocalMcpServerControl>()
            .InitializeAsync(CancellationToken.None);
        startupState.MarkProfileInitialized();
        return null;
    }

    private static async Task FinalizeAsync(ServiceProvider services)
    {
        await services.GetRequiredService<Asura.Mcp.Server.LocalMcpServerControl>()
            .DisposeAsync().ConfigureAwait(false);
        // The run began either before the lifetime or, with sealed keys,
        // behind the lock screen; quitting at the lock screen means no run
        // marker was ever written and there is nothing to finalize.
        if (services.GetRequiredService<ApplicationStartupState>().Run is not { } run)
        {
            return;
        }

        var mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
        var application = services.GetRequiredService<AsuraApplication>();
        // Normal quit finalizes while the dispatcher pumps; the post-lifetime path is a fallback.
        var completion = await services.GetRequiredService<DesktopRunFinalizer>()
            .FinalizeAsync(
                cancellationToken => QuiescePresentationAsync(
                    services.GetRequiredService<QuickTerminalController>(),
                    application,
                    cancellationToken),
                mainWindowViewModel.FlushRecentSessionHistoryAsync,
                _ => services.GetRequiredService<InMemorySessionHostClient>().DisposeAsync(),
                run.RunId,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!completion.IsSuccess)
        {
            ReportLifecycleFailure(
                "finalize its recovery state",
                completion.Error!);
        }
    }

    private static void ConfigureDockDiagnostics()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ASURA_DOCK_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        DockSettings.EnableDiagnosticsLogging = true;
        DockSettings.DiagnosticsLogHandler = _ =>
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.dock-diagnostic",
                SecretSafeDiagnosticKind.Unexpected);
    }

    internal static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder
            .Configure(() => services.GetRequiredService<AsuraApplication>())
            .UsePlatformDetect()
            .WithInterFont()
            .ConfigureFonts(fontManager =>
                fontManager.AddFontCollection(new AsuraTerminalFontCollection()))
            .SetDragPreviewOpacity(0.9);

    private static BrowserEngineRuntimeOptions CreateBrowserEngineOptions(
        IServiceProvider services)
    {
        var artifacts = services.GetRequiredService<LocalArtifactPaths>();
        var browserPaths = services.GetRequiredService<BrowserProfileStoragePaths>();
        var version = typeof(Program).Assembly.GetName().Version;
        return new BrowserEngineRuntimeOptions(
            browserPaths.RuntimeDirectory,
            Path.Combine(artifacts.ApplicationLogDirectory, "cef.log"),
            version is null ? "0.0.0" : version.ToString(3));
    }

    private static void ReportLifecycleFailure(
        string operation,
        ApplicationRunError error)
    {
        _ = operation;
        _ = error;
        SecretSafeDiagnosticProjection.WriteStandardError(
            "desktop.lifecycle.failed",
            SecretSafeDiagnosticKind.Unexpected);
        Environment.ExitCode = 1;
    }

    /// <summary>
    /// A second launch or a notification brings Asura forward. With every
    /// window closed and Asura still running, as on a Mac, that opens one.
    /// </summary>
    /// <summary>
    /// A second launch reached this instance: it opens the folder it carried,
    /// or just brings a window forward.
    /// </summary>
    private static void OnInstanceActivated(string? request)
    {
        if (OpenFolderRequest.Decode(request) is { } folder)
        {
            Dispatcher.UIThread.Post(() =>
                (Avalonia.Application.Current as AsuraApplication)?.RequestOpenFolder(folder));
            return;
        }

        RequestMainWindowActivation();
    }

    private static void RequestMainWindowActivation()
    {
        Dispatcher.UIThread.Post(() =>
            (Avalonia.Application.Current as AsuraApplication)?.ShowOrOpenMainWindow());
    }

    private static void OnNativeNotificationActivated(
        object? sender,
        NativeNotificationActivatedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        RequestMainWindowActivation();
    }

    private static async Task QuiescePresentationAsync(
        QuickTerminalController quickTerminalController,
        AsuraApplication application,
        CancellationToken cancellationToken)
    {
        quickTerminalController.Dispose();
        await application.QuiesceForShutdownAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
