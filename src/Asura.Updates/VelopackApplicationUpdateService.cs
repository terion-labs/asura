using Asura.Application;
using Asura.Application.ApplicationUpdates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace Asura.Updates;

internal sealed class VelopackApplicationUpdateService : IApplicationUpdateService
{
    private const string RepositoryUrl =
        "https://github.com/terion-labs/asura";

    private readonly Func<Action, Task> _requestShutdown;
    private readonly UpdateManager _updates;
    private readonly IVelopackLogger _log;
    private UpdateInfo? _availableUpdate;
    private int _operationInProgress;

    public VelopackApplicationUpdateService(
        DistributionIdentity distribution,
        Func<Action, Task> requestShutdown,
        IVelopackLocator? locator = null)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        ArgumentNullException.ThrowIfNull(requestShutdown);
        if (distribution.UpdateStrategy != ApplicationUpdateStrategy.Velopack)
        {
            throw new ArgumentException(
                "The distribution does not use Velopack.",
                nameof(distribution));
        }

        _requestShutdown = requestShutdown;
        locator ??= VelopackLocator.Current;
        _log = locator.Log;
        _updates = new UpdateManager(
            new GithubSource(RepositoryUrl, accessToken: null, prerelease: false),
            new UpdateOptions
            {
                ExplicitChannel = distribution.Channel,
                AllowVersionDowngrade = false,
            },
            locator);
        Snapshot = !_updates.IsInstalled
            ? new(
                distribution,
                ApplicationUpdateStage.Unavailable,
                Error: ApplicationUpdateError.NotInstalledByVelopack)
            : _updates.UpdatePendingRestart is { } pending
                ? new(
                    distribution,
                    ApplicationUpdateStage.ReadyToRestart,
                    pending.Version.ToString())
                : new(
                    distribution,
                    ApplicationUpdateStage.Idle);
    }

    public event EventHandler<ApplicationUpdateSnapshot>? Changed;

    public ApplicationUpdateSnapshot Snapshot { get; private set; }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Snapshot.CanCheck)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!Snapshot.CanCheck)
            {
                return;
            }

            SetSnapshot(Snapshot with
            {
                Stage = ApplicationUpdateStage.Checking,
                AvailableVersion = null,
                DownloadProgress = null,
                Error = ApplicationUpdateError.None,
            });

            // Velopack does not expose cancellation for feed checks. Once the
            // request starts, this boundary waits for it instead of allowing a
            // second operation to race the first one.
            _availableUpdate = await _updates.CheckForUpdatesAsync()
                .ConfigureAwait(false);
            SetSnapshot(_availableUpdate is null
                ? Snapshot with { Stage = ApplicationUpdateStage.UpToDate }
                : Snapshot with
                {
                    Stage = ApplicationUpdateStage.Available,
                    AvailableVersion =
                        _availableUpdate.TargetFullRelease.Version.ToString(),
                });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            SetFailure(ApplicationUpdateError.CheckFailed);
        }
        finally
        {
            Volatile.Write(ref _operationInProgress, 0);
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken)
    {
        if (_availableUpdate is null || !Snapshot.CanDownload)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (_availableUpdate is null || !Snapshot.CanDownload)
            {
                return;
            }

            SetSnapshot(Snapshot with
            {
                Stage = ApplicationUpdateStage.Downloading,
                DownloadProgress = 0,
                Error = ApplicationUpdateError.None,
            });
            await _updates.DownloadUpdatesAsync(
                    _availableUpdate,
                    progress => SetSnapshot(Snapshot with
                    {
                        DownloadProgress = progress,
                    }),
                    cancellationToken)
                .ConfigureAwait(false);
            SetSnapshot(Snapshot with
            {
                Stage = ApplicationUpdateStage.ReadyToRestart,
                DownloadProgress = 100,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetSnapshot(Snapshot with
            {
                Stage = ApplicationUpdateStage.Available,
                DownloadProgress = null,
            });
            throw;
        }
        catch (Exception)
        {
            SetFailure(ApplicationUpdateError.DownloadFailed);
        }
        finally
        {
            Volatile.Write(ref _operationInProgress, 0);
        }
    }

    public async Task RestartToApplyAsync()
    {
        var release = _availableUpdate?.TargetFullRelease
            ?? _updates.UpdatePendingRestart;
        if (release is null || !Snapshot.CanRestartToApply
            || Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            SetSnapshot(Snapshot with
            {
                Stage = ApplicationUpdateStage.PreparingToRestart,
                Error = ApplicationUpdateError.None,
            });
            _log.LogInformation("Asura update restart: preparing desktop shutdown.");
            await _requestShutdown(() =>
            {
                // Start Velopack's 60-second exit timer only after workspace
                // cleanup finishes, while launch failures can still reach the UI.
                _log.LogInformation("Asura update restart: preparation complete; launching updater.");
                _updates.WaitExitThenApplyUpdates(
                    release,
                    silent: false,
                    restart: true,
                    restartArgs: null);
            }).ConfigureAwait(false);
            _log.LogInformation("Asura update restart: desktop shutdown requested.");
        }
        catch (Exception exception)
        {
            // Exception messages can contain workspace paths or credentials.
            var preparationFailure = exception as UpdateRestartPreparationException;
            _log.LogError(SecretSafeDiagnosticProjection.FromException(
                preparationFailure?.DiagnosticCode ?? "update.restart.failed",
                preparationFailure?.InnerException ?? exception));
            SetFailure(ApplicationUpdateError.ApplyFailed);
        }
        finally
        {
            Volatile.Write(ref _operationInProgress, 0);
        }
    }

    private void SetFailure(ApplicationUpdateError error) =>
        SetSnapshot(Snapshot with
        {
            Stage = ApplicationUpdateStage.Failed,
            DownloadProgress = null,
            Error = error,
        });

    private void SetSnapshot(ApplicationUpdateSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(this, snapshot);
    }
}
