namespace Asura.Application;

/// <summary>Reports browser startup failures without making the shell unavailable.</summary>
public interface IBrowserStartupRecovery
{
    string? Error { get; }
    bool RequiresRestart { get; }
    bool CanRetry { get; }
    event EventHandler? Changed;
    Task RetryAsync(CancellationToken cancellationToken);
}
