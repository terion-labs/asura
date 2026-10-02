using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed class BrowserRuntimePanelViewModel : RuntimePanelViewModel
{
    private const int MaximumPendingHistoryRecords = 1_000;
    private static readonly TimeSpan TitleHistoryInterval = TimeSpan.FromMilliseconds(250);
    private readonly object _initializationGate = new();
    private readonly object _historyGate = new();
    private readonly LinkedList<(BrowserSessionState State, bool IsTitleUpdate)> _pendingHistory = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IBrowserRendererViewFactory _rendererViewFactory;
    private readonly ConnectionProfile _connection;
    private readonly IBrowserHistory? _history;
    private BrowserAddress? _recordedAddress;
    private long _recordedRevision = -1;
    private string? _recordedTitle;
    private CancellationTokenSource? _historySearch;
    private IReadOnlyList<BrowserHistorySuggestion> _historySuggestions = [];
    private bool _isHistoryVisible;
    private readonly BrowserProfileBinding _profile;
    private readonly string? _connectionDisplayName;
    private BrowserRendererView? _rendererView;
    private BrowserAddress _currentAddress;
    private Task _initialization = Task.CompletedTask;
    private string? _routeErrorMessage;
    private bool _initializationStarted;
    private bool _historyRecording;
    private bool _historyClearing;
    private Task _historyRecordingCompletion = Task.CompletedTask;
    private bool _disposed;

    public BrowserRuntimePanelViewModel(
        PanelInstanceId id,
        string title,
        SessionOwner owner,
        BrowserAddress initialAddress,
        ISessionHostClient sessionClient,
        ClientId clientId,
        ConnectionProfile connection,
        IBrowserRendererViewFactory rendererViewFactory,
        string? connectionDisplayName = null)
        : this(
            id,
            title,
            owner,
            initialAddress,
            sessionClient,
            clientId,
            connection,
            BrowserProfileBinding.Legacy(BrowserProfileKey.Global),
            rendererViewFactory,
            connectionDisplayName)
    {
    }

    public BrowserRuntimePanelViewModel(
        PanelInstanceId id,
        string title,
        SessionOwner owner,
        BrowserAddress initialAddress,
        ISessionHostClient sessionClient,
        ClientId clientId,
        ConnectionProfile connection,
        BrowserProfileKey profile,
        IBrowserRendererViewFactory rendererViewFactory,
        string? connectionDisplayName = null)
        : this(
            id,
            title,
            owner,
            initialAddress,
            sessionClient,
            clientId,
            connection,
            BrowserProfileBinding.Legacy(profile),
            rendererViewFactory,
            connectionDisplayName)
    {
    }

    public BrowserRuntimePanelViewModel(
        PanelInstanceId id,
        string title,
        SessionOwner owner,
        BrowserAddress initialAddress,
        ISessionHostClient sessionClient,
        ClientId clientId,
        ConnectionProfile connection,
        BrowserProfileBinding profile,
        IBrowserRendererViewFactory rendererViewFactory,
        string? connectionDisplayName = null,
        IBrowserHistory? history = null)
        : base(id, PanelKind.Browser, title, "Browser")
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(initialAddress);
        SessionClient = sessionClient
            ?? throw new ArgumentNullException(nameof(sessionClient));
        ClientId = clientId;
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _connectionDisplayName = connectionDisplayName;
        _history = history;
        if (connection.Endpoint is not (ConnectionEndpoint.Local or ConnectionEndpoint.Ssh))
        {
            throw new ArgumentException(
                "A browser connection must be local or SSH.",
                nameof(connection));
        }

        _rendererViewFactory = rendererViewFactory
            ?? throw new ArgumentNullException(nameof(rendererViewFactory));
        _currentAddress = initialAddress;
        SessionRequest = new EnsureBrowserSessionRequest(
            SessionId.New(),
            owner,
            title,
            initialAddress);
    }

    public ISessionHostClient SessionClient { get; }

    public ClientId ClientId { get; }

    public EnsureBrowserSessionRequest SessionRequest { get; private set; }

    internal override void UpdateSessionOwner(SessionOwner owner) =>
        SessionRequest = SessionRequest with { Owner = owner };

    public ConnectionId ConnectionId => _connection.Id;

    public BrowserProfileKey Profile => _profile.Selection.Partition;

    public BrowserProfileBinding ProfileBinding => _profile;

    public string BrowserProfileDisplayName =>
        _profile.Selection.Partition.Kind == BrowserProfileKind.Workspace
            ? $"{_profile.Definition.Name} · Isolated workspace"
            : _profile.Definition.Name;

    public string ConnectionDisplayName => _connectionDisplayName ?? (_connection.Endpoint is ConnectionEndpoint.Local
        ? "Local"
        : _connection.Name);

    public BrowserRendererView? RendererView
    {
        get => _rendererView;
        private set => SetProperty(ref _rendererView, value);
    }

    public event EventHandler<BrowserNewTabRequestedEventArgs>? NewTabRequested;

    public string? RouteErrorMessage
    {
        get => _routeErrorMessage;
        private set
        {
            if (SetProperty(ref _routeErrorMessage, value))
            {
                ReportError(value, "Browser connection failed");
                OnPropertyChanged(nameof(HasRouteError));
            }
        }
    }

    public bool HasRouteError => RouteErrorMessage is not null;

    internal bool HasInteractiveAttachment =>
        RendererView?.Attachment?.Matches(
            SessionClient,
            ClientId,
            SessionRequest.SessionId) is true;

    internal async Task EnsureHostedRendererAsync(
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await StartInitialization().WaitAsync(linkedCancellation.Token);
        var renderer = RendererView
            ?? throw new InvalidOperationException(
                "The browser renderer is unavailable.");
        _ = await renderer.EnsureAttachmentAsync(
            SessionClient,
            ClientId,
            SessionRequest,
            ViewportDescriptor.Empty,
            linkedCancellation.Token);
    }

    public Task StartInitialization()
    {
        lock (_initializationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initializationStarted)
            {
                return _initialization;
            }

            _initializationStarted = true;
            _initialization = InitializeAsync();
            return _initialization;
        }
    }

    public BrowserAddress CurrentAddress
    {
        get => _currentAddress;
        private set => SetProperty(ref _currentAddress, value);
    }

    internal void ApplyBrowserState(BrowserSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        CurrentAddress = state.Address;
        if (state.LoadState == BrowserLoadState.Ready && state.DocumentRevision > 0
            && (_recordedAddress != state.Address || _recordedRevision != state.DocumentRevision
                || !string.Equals(_recordedTitle, state.Title, StringComparison.Ordinal)))
        {
            var isTitleUpdate = _recordedAddress == state.Address && _recordedRevision == state.DocumentRevision;
            _recordedAddress = state.Address;
            _recordedRevision = state.DocumentRevision;
            _recordedTitle = state.Title;
            QueueHistoryState(state, isTitleUpdate);
        }
    }

    public IReadOnlyList<BrowserHistorySuggestion> HistorySuggestions
    {
        get => _historySuggestions;
        private set => SetProperty(ref _historySuggestions, value);
    }

    public bool IsHistoryVisible
    {
        get => _isHistoryVisible;
        set
        {
            if (!value)
            {
                CancelHistorySearch();
            }
            SetProperty(ref _isHistoryVisible, value);
        }
    }

    public void ShowHistory(string query)
    {
        CancelHistorySearch();
        if (!_disposed)
        {
            _ = PopulateHistoryAsync(query);
        }
    }

    public void CancelHistorySearch() => _historySearch?.Cancel();

    public void HideHistory() => IsHistoryVisible = false;

    private async Task PopulateHistoryAsync(string query)
    {
        using var search = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _historySearch = search;
        try
        {
            await Task.Delay(120, search.Token);
            var entries = await SearchHistoryAsync(query, search.Token);
            if (!search.IsCancellationRequested)
            {
                HistorySuggestions = [.. entries.Select(entry => new BrowserHistorySuggestion(entry))];
                IsHistoryVisible = entries.Count > 0 || HistoryStatus is not null;
            }
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_historySearch, search))
            {
                _historySearch = null;
            }
        }
    }

    public string? HistoryStatus { get; private set; }

    public async Task<IReadOnlyList<BrowserHistoryEntry>> SearchHistoryAsync(
        string query, CancellationToken cancellationToken)
    {
        if (_history is null || _profile.Definition.Persistence == BrowserProfilePersistence.PrivateSession
            || _profile.Definition.Privacy.History == BrowserActivityRetention.DoNotRecord)
        {
            return [];
        }
        try
        {
            var entries = await _history.SearchAsync(_profile.Selection, query, cancellationToken);
            HistoryStatus = null;
            OnPropertyChanged(nameof(HistoryStatus));
            return entries;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HistoryStatus = "Browser history unavailable: " + exception.Message;
            OnPropertyChanged(nameof(HistoryStatus));
            return [];
        }
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken)
    {
        if (_history is null)
        {
            return;
        }
        Task recording;
        lock (_historyGate)
        {
            _historyClearing = true;
            _pendingHistory.Clear();
            recording = _historyRecordingCompletion;
        }
        try
        {
            // Wait for the one in-flight write before clearing so a deferred
            // title cannot recreate the row after the user clears history.
            await recording.WaitAsync(cancellationToken);
            await _history.ClearAsync(_profile.Selection, cancellationToken);
            HistorySuggestions = [];
            HistoryStatus = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HistoryStatus = "Could not clear browser history: " + exception.Message;
        }
        finally
        {
            lock (_historyGate)
            {
                _historyClearing = false;
            }
        }
        OnPropertyChanged(nameof(HistoryStatus));
        IsHistoryVisible = HistoryStatus is not null;
    }

    private void QueueHistoryState(BrowserSessionState state, bool isTitleUpdate)
    {
        if (WorkspacePrivateEndpointAddress.IsReservedHost(state.Address.Value.Host)
            || _history is null || _profile.Definition.Persistence == BrowserProfilePersistence.PrivateSession
            || _profile.Definition.Privacy.History == BrowserActivityRetention.DoNotRecord)
        {
            return;
        }
        lock (_historyGate)
        {
            if (_disposed || _historyClearing)
            {
                return;
            }
            var last = _pendingHistory.Last;
            if (last is not null && last.Value.State.Address == state.Address
                && last.Value.State.DocumentRevision == state.DocumentRevision)
            {
                last.Value = (state, last.Value.IsTitleUpdate);
            }
            else
            {
                // History retains 1,000 addresses. Retaining more deferred
                // navigations cannot add entries to that bounded result.
                if (_pendingHistory.Count == MaximumPendingHistoryRecords)
                {
                    _pendingHistory.RemoveFirst();
                }
                _pendingHistory.AddLast((state, isTitleUpdate));
            }
            if (!_historyRecording)
            {
                _historyRecording = true;
                _historyRecordingCompletion = DrainHistoryAsync();
            }
        }
    }

    private async Task DrainHistoryAsync()
    {
        try
        {
            while (true)
            {
                bool waitForTitle;
                lock (_historyGate)
                {
                    if (_disposed || _historyClearing || _pendingHistory.First is null)
                    {
                        _historyRecording = false;
                        return;
                    }
                    waitForTitle = _pendingHistory.First.Value.IsTitleUpdate;
                }
                if (waitForTitle)
                {
                    await Task.Delay(TitleHistoryInterval, _lifetime.Token);
                }
                BrowserSessionState state;
                lock (_historyGate)
                {
                    if (_disposed || _historyClearing || _pendingHistory.First is null)
                    {
                        continue;
                    }
                    state = _pendingHistory.First.Value.State;
                    _pendingHistory.RemoveFirst();
                }
                await RememberAddressAsync(state);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            lock (_historyGate)
            {
                _pendingHistory.Clear();
                _historyRecording = false;
            }
        }
    }

    private async Task RememberAddressAsync(BrowserSessionState state)
    {
        try
        {
            await _history!.RecordAsync(_profile.Selection, state.Address, state.Title, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HistoryStatus = "Browser history unavailable: " + exception.Message;
            OnPropertyChanged(nameof(HistoryStatus));
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var renderer = await _rendererViewFactory
                .CreateAsync(_connection, _profile, _lifetime.Token);
            lock (_initializationGate)
            {
                if (_disposed)
                {
                    renderer.Dispose();
                    return;
                }

                if (renderer.Renderer is IBrowserNewTabRequestSource newTabSource)
                {
                    newTabSource.NewTabRequested += OnRendererNewTabRequested;
                }

                RendererView = renderer;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RouteErrorMessage = exception.Message;
            throw;
        }
    }

    private void OnRendererNewTabRequested(
        object? sender,
        BrowserNewTabRequestedEventArgs args)
    {
        if (!_disposed)
        {
            NewTabRequested?.Invoke(this, args);
        }
    }

    public override void Dispose()
    {
        lock (_initializationGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _lifetime.Cancel();
        lock (_historyGate)
        {
            _pendingHistory.Clear();
        }
        // Keep the cancelled source valid until this panel is collected.
        // Initialization/attachment work may already hold its token on another
        // continuation; disposing here turns an ordinary shutdown race into an
        // ObjectDisposedException instead of cancellation.
        if (RendererView?.Renderer is IBrowserNewTabRequestSource newTabSource)
        {
            newTabSource.NewTabRequested -= OnRendererNewTabRequested;
        }

        RendererView?.Dispose();
        RendererView = null;
        NewTabRequested = null;
        base.Dispose();
    }
}
