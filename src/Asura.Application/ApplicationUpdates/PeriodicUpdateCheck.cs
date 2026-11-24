namespace Asura.Application.ApplicationUpdates;

/// <summary>
/// Checks for an update shortly after launch and then every few hours, for
/// the whole app rather than per window, so the workspaces rail can offer an
/// update without anyone asking first. It leaves a found, downloading or
/// downloaded update alone: checking again would hide the offer while it ran.
/// </summary>
public sealed class PeriodicUpdateCheck : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    private readonly IApplicationUpdateService _updates;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _loop;

    public PeriodicUpdateCheck(IApplicationUpdateService updates, TimeProvider time)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Starts checking. A build that cannot update itself, or one whose
    /// install source updates it, never starts.
    /// </summary>
    public void Start()
    {
        if (_loop is null && _updates.Snapshot.CanCheck)
        {
            _loop = RunAsync(_lifetime.Token);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, _time, cancellationToken).ConfigureAwait(false);
            while (true)
            {
                if (_updates.Snapshot.Stage is ApplicationUpdateStage.Idle
                    or ApplicationUpdateStage.UpToDate
                    or ApplicationUpdateStage.Failed)
                {
                    // The service reports its own failures as a Failed stage.
                    await _updates.CheckAsync(cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(Interval, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
