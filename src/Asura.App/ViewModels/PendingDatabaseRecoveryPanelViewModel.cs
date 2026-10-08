using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>Uses the normal unavailable-panel surface while its local target resolves.</summary>
public sealed class PendingDatabaseRecoveryPanelViewModel : UnavailableRuntimePanelViewModel
{
    private readonly Func<PendingDatabaseRecoveryPanelViewModel, CancellationToken, Task<bool>> _restore;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _running;
    private bool _disposed;

    public PendingDatabaseRecoveryPanelViewModel(PanelInstanceId id, string title, string target,
        Func<PendingDatabaseRecoveryPanelViewModel, CancellationToken, Task<bool>> restore)
        : base(id, PanelKind.DatabaseViewer, title, "Database", $"Restoring the database connection from {PlatformTerms.SecretStore}…")
    {
        Target = target;
        _restore = restore;
        StateActionCommand = new AsyncActionCommand(StartInitializationAsync, () => !_running && !_disposed);
    }

    public string Target { get; }

    public override string StateHeading => "Database connection";

    public override string StateActionLabel => "Retry";

    public override System.Windows.Input.ICommand StateActionCommand { get; }

    public async Task StartInitializationAsync()
    {
        if (_running || _disposed)
        {
            return;
        }
        _running = true;
        try
        {
            if (!await _restore(this, _lifetime.Token) && !_disposed)
            {
                CapabilityMessage = $"Asura couldn't restore this database connection from {PlatformTerms.SecretStore}. Unlock {PlatformTerms.SecretStore} and try again, or choose the connection again.";
                OnPropertyChanged(nameof(CapabilityMessage));
            }
        }
        catch (OperationCanceledException) when (_disposed || _lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                CapabilityMessage = $"Asura couldn't restore the database connection. Unlock {PlatformTerms.SecretStore} and try again, or choose the connection again.";
                OnPropertyChanged(nameof(CapabilityMessage));
            }
        }
        finally
        {
            _running = false;
            ((AsyncActionCommand)StateActionCommand).RaiseCanExecuteChanged();
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
        base.Dispose();
    }
}
