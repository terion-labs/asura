using System.Windows.Input;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed class BrowserStartupRecoveryViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserStartupRecovery? _recovery;
    private readonly IUiThreadDispatcher _dispatcher;
    private bool _isRetrying;
    private bool _disposed;

    public BrowserStartupRecoveryViewModel(IBrowserStartupRecovery? recovery, IUiThreadDispatcher dispatcher)
    {
        _recovery = recovery;
        _dispatcher = dispatcher;
        RetryCommand = new AsyncActionCommand(RetryAsync, () => CanRetry && !_isRetrying);
        recovery?.Changed += OnChanged;
    }

    public string? Message => _recovery?.Error;
    public bool HasError => Message is not null;
    public bool CanRetry => HasError && _recovery?.CanRetry == true;
    public string RetryLabel => _isRetrying ? "Retrying…" : _recovery?.RequiresRestart == true ? "Restart Asura" : "Retry browser";
    public ICommand RetryCommand { get; }

    private async Task RetryAsync()
    {
        _isRetrying = true;
        Refresh();
        try
        {
            if (_recovery is not null)
            {
                await _recovery.RetryAsync(CancellationToken.None);
            }
        }
        finally
        {
            _isRetrying = false;
            Refresh();
        }
    }

    private async void OnChanged(object? sender, EventArgs args)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        }, CancellationToken.None);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(RetryLabel));
        (RetryCommand as AsyncActionCommand)?.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _disposed = true;
        _recovery?.Changed -= OnChanged;
    }
}
