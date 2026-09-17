using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>Owns forward leases independently of the resource panel that opened them.</summary>
public sealed class KubernetesForwardWorkspaceState(IKubernetesPanelSessionFactory factory, IWorkspaceNetworkConnector? connector = null, IReadOnlyList<DatabaseDriverDescriptor>? databaseDrivers = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly HashSet<Task> _starts = [];
    private Task? _disposal;
    private bool _disposed;
    public ObservableCollection<KubernetesForwardViewModel> Forwards { get; } = [];

    public Task StartAsync(KubernetesConnectionProfile profile, KubernetesResourceReference pod, int port)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) { return Task.FromCanceled(new CancellationToken(canceled: true)); }
            token = _lifetime.Token;
            _starts.Add(completion.Task);
        }
        _ = StartOwnedAsync(profile, pod, port, token, completion);
        return completion.Task;
    }

    private async Task StartOwnedAsync(KubernetesConnectionProfile profile, KubernetesResourceReference pod, int port,
        CancellationToken token, TaskCompletionSource completion)
    {
        try { await StartCoreAsync(profile, pod, port, token); completion.TrySetResult(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
        catch (Exception exception) { completion.TrySetException(exception); }
        finally { lock (_gate) { _starts.Remove(completion.Task); } }
    }

    private async Task StartCoreAsync(KubernetesConnectionProfile profile, KubernetesResourceReference pod, int port, CancellationToken token)
    {
        var routeLifetime = connector?.CaptureRoute().RouteLifetime ?? CancellationToken.None;
        await using var session = await factory.OpenAsync(profile, token);
        token.ThrowIfCancellationRequested();
        var handle = await session.StartPortForwardAsync(new(pod, port), token);
        if (token.IsCancellationRequested) { await handle.DisposeAsync(); token.ThrowIfCancellationRequested(); }
        IWorkspacePrivateEndpointLease? endpoint = null;
        try
        {
            if (connector is IWorkspacePrivateEndpointRegistrar registrar) { endpoint = registrar.RegisterLoopbackEndpoint(handle.LocalPort, routeLifetime); }
        }
        catch { await handle.DisposeAsync(); throw; }
        var forward = new KubernetesForwardViewModel(profile, pod, handle, endpoint, databaseDrivers);
        Forwards.Add(forward);
        forward.ObserveCompletion();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) { return new(_disposal); }
            _disposed = true;
            _disposal = DisposeCoreAsync([.. _starts]);
            return new(_disposal);
        }
    }

    private async Task DisposeCoreAsync(Task[] pending)
    {
        await _lifetime.CancelAsync();
        // Start callers retain their own failure; shutdown still waits for every owned start
        // to release its session/handle before disposing the cancellation source.
        try { await Task.WhenAll(pending); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        foreach (var forward in Forwards) { await forward.StopAsync(); }
        _lifetime.Dispose();
    }

}

public sealed class KubernetesForwardViewModel : ObservableObject
{
    private readonly IKubernetesPortForward _handle;
    private readonly IWorkspacePrivateEndpointLease? _endpoint;
    private readonly AsyncActionCommand _stopCommand;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _stopping;
    private bool _active = true;
    private string _status = "Active";
    public KubernetesForwardViewModel(KubernetesConnectionProfile profile, KubernetesResourceReference pod, IKubernetesPortForward handle, IWorkspacePrivateEndpointLease? endpoint = null, IReadOnlyList<DatabaseDriverDescriptor>? databaseDrivers = null)
    {
        DatabaseDrivers = databaseDrivers ?? [];
        _databaseDriver = DatabaseDrivers.FirstOrDefault();
        _endpoint = endpoint;
        _handle = handle;
        Identity = $"{profile.Name} · {profile.ContextName} · {pod.Namespace}/{pod.Name}:{handle.RemotePort.ToString(CultureInfo.InvariantCulture)}";
        BrowserAddress = endpoint is null ? null : $"http://{endpoint.Host}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
        Address = $"http://127.0.0.1:{handle.LocalPort.ToString(CultureInfo.InvariantCulture)}";
        _stopCommand = new(StopAsync, () => IsActive);
    }
    private DatabaseDriverDescriptor? _databaseDriver;
    private string _databaseName = string.Empty;
    private string _databaseUsername = string.Empty;
    private string _databasePassword = string.Empty;
    public IReadOnlyList<DatabaseDriverDescriptor> DatabaseDrivers { get; }
    public DatabaseDriverDescriptor? DatabaseDriver { get => _databaseDriver; set => SetProperty(ref _databaseDriver, value); }
    public string DatabaseName { get => _databaseName; set => SetProperty(ref _databaseName, value); }
    public string DatabaseUsername { get => _databaseUsername; set => SetProperty(ref _databaseUsername, value); }
    public string DatabasePassword { get => _databasePassword; set => SetProperty(ref _databasePassword, value); }
    public bool CanOpenDatabase => CanOpenBrowser && DatabaseDrivers.Count > 0;
    public string? EndpointHost => _endpoint?.Host;
    public int EndpointPort => _endpoint?.Port ?? 0;
    public string Identity { get; }
    public string Address { get; }
    public string? BrowserAddress { get; }
    public bool CanOpenBrowser => IsActive && _endpoint is not null && !_endpoint.Lifetime.IsCancellationRequested;
    public ICommand StopCommand => _stopCommand;
    public bool IsActive { get => _active; private set { SetProperty(ref _active, value); _stopCommand.RaiseCanExecuteChanged(); OnPropertyChanged(nameof(CanOpenBrowser)); OnPropertyChanged(nameof(CanOpenDatabase)); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public void ObserveCompletion() => _ = ObserveCompletionAsync();
    private async Task ObserveCompletionAsync()
    {
        try { await _handle.Completion.WaitAsync(_endpoint?.Lifetime ?? CancellationToken.None); if (IsActive) { Status = "Connection closed"; } }
        catch (OperationCanceledException) { await StopAsync(); }
        catch (KubernetesRequestException exception) { Status = exception.Message; }
        catch (IOException) { Status = "Forward connection ended."; }
        finally { IsActive = false; if (_endpoint is not null) { await _endpoint.DisposeAsync(); } }
    }
    public Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0) { _ = StopCoreAsync(); }
        return _stopped.Task;
    }
    private async Task StopCoreAsync()
    {
        IsActive = false;
        try { if (_endpoint is not null) { await _endpoint.DisposeAsync(); } await _handle.DisposeAsync(); Status = "Stopped"; }
        catch (KubernetesRequestException exception) { Status = exception.Message; }
        catch (IOException) { Status = "Forward connection ended."; }
        finally { _stopped.TrySetResult(); }
    }
}
