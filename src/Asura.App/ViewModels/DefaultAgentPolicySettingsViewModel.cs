using Asura.App.Controls;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Owns default agent-policy editing, explicit and background persistence,
/// provider-option refresh, and the coordinator subscription.
/// </summary>
public sealed class DefaultAgentPolicySettingsViewModel : ObservableObject, IDisposable
{
    private readonly AgentPolicyCoordinator? _coordinator;
    private readonly IUiThreadDispatcher _dispatcher;
    private readonly Action<string> _setError;
    private readonly Action _clearError;
    private readonly object _saveGate = new();
    private AgentPolicy? _pendingPolicy;
    private Task _saveTask = Task.CompletedTask;
    private bool _saveRunning;
    private bool _sealed;
    private bool _disposed;
    private bool _hasEdits;

    public DefaultAgentPolicySettingsViewModel(
        AgentPolicyCoordinator? coordinator,
        IReadOnlyList<AiProviderProfileDescriptor>? providers,
        IUiThreadDispatcher dispatcher,
        Action<string> setError,
        Action clearError)
    {
        _coordinator = coordinator;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _setError = setError ?? throw new ArgumentNullException(nameof(setError));
        _clearError = clearError ?? throw new ArgumentNullException(nameof(clearError));
        Editor = CreateEditor(_coordinator?.Policy, providers);
        Editor.Changed += OnEditorChanged;
        if (_coordinator is { } activeCoordinator)
        {
            activeCoordinator.Changed += OnCoordinatorChanged;
            if (activeCoordinator.InitializationError is { } error)
            {
                _setError(LoadFailureText(error));
            }
        }
    }

    public event EventHandler? PolicyChanged;

    public SavedScreenAgentPolicyEditorViewModel Editor { get; private set; }

    public AgentPolicy? Policy => _coordinator?.Policy;

    public bool CanSave => _coordinator is not null && Editor.IsValid;

    /// <summary>
    /// True once someone has changed the default and left it incomplete. A
    /// page nobody has touched shows only the notice that says what to do
    /// first, never an error that blames the person for it.
    /// </summary>
    public bool ShowsIncompleteDefault => _hasEdits && !Editor.IsValid;

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_coordinator is null)
        {
            _setError("Default AI configuration storage is unavailable.");
            return;
        }

        AgentPolicy? policy;
        try
        {
            policy = Editor.Build();
        }
        catch (ArgumentException exception)
        {
            _setError(FailureReason.Refusal(
                "agent.default-policy.save.refused",
                exception,
                "Choose an available AI provider and enter a model, then save again."));
            return;
        }

        if (policy is null)
        {
            _setError("The default AI settings can't be turned off. Choose another default first.");
            return;
        }

        var result = await _coordinator.SaveAsync(policy, cancellationToken);
        if (!result.IsSuccess)
        {
            _setError(SaveFailureText(result.Error!));
            return;
        }

        _clearError();
    }

    public void RefreshProviders(
        IReadOnlyList<AiProviderProfileDescriptor>? providers)
    {
        ThrowIfDisposed();
        AgentPolicy? draft = null;
        if (Editor.IsValid)
        {
            draft = Editor.Build();
        }

        Editor.Changed -= OnEditorChanged;
        Editor.Dispose();
        Editor = CreateEditor(draft ?? _coordinator?.Policy, providers);
        Editor.Changed += OnEditorChanged;
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowsIncompleteDefault));
        QueuePersistence(onlyWhenMissing: true);
    }

    public void QueuePersistence(bool onlyWhenMissing)
    {
        ThrowIfDisposed();
        if (_sealed
            || _coordinator is null
            || _coordinator.InitializationError is not null
            || onlyWhenMissing && _coordinator.Policy is not null
            || !Editor.IsValid)
        {
            return;
        }

        AgentPolicy? policy;
        try
        {
            policy = Editor.Build();
        }
        catch (ArgumentException)
        {
            return;
        }

        if (policy is null)
        {
            return;
        }

        lock (_saveGate)
        {
            _pendingPolicy = policy;
            if (_saveRunning)
            {
                return;
            }

            _saveRunning = true;
            _saveTask = PersistQueuedPoliciesAsync();
        }
    }

    public async Task QuiesceAsync()
    {
        Seal();
        Task pending;
        lock (_saveGate)
        {
            pending = _saveTask;
        }

        await pending.ConfigureAwait(false);
    }

    public void Seal()
    {
        _sealed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sealed = true;
        if (_coordinator is { } activeCoordinator)
        {
            activeCoordinator.Changed -= OnCoordinatorChanged;
        }

        Editor.Changed -= OnEditorChanged;
        Editor.Dispose();
        PolicyChanged = null;
    }

    private async Task PersistQueuedPoliciesAsync()
    {
        while (true)
        {
            AgentPolicy? policy;
            lock (_saveGate)
            {
                policy = _pendingPolicy;
                _pendingPolicy = null;
                if (policy is null)
                {
                    _saveRunning = false;
                    return;
                }
            }

            var result = await _coordinator!
                .SaveAsync(policy, CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.IsSuccess && !_disposed)
            {
                await _dispatcher.InvokeAsync(
                        () => _setError(SaveFailureText(result.Error!)),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    private async void OnCoordinatorChanged(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        try
        {
            await _dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_disposed)
                        {
                            PolicyChanged?.Invoke(this, EventArgs.Empty);
                        }
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
    }

    private void OnEditorChanged(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        _hasEdits = true;
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowsIncompleteDefault));
        QueuePersistence(onlyWhenMissing: false);
    }

    private static SavedScreenAgentPolicyEditorViewModel CreateEditor(
        AgentPolicy? policy,
        IReadOnlyList<AiProviderProfileDescriptor>? providers) =>
        new(policy, providers)
        {
            IsEnabled = true,
        };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Why the saved default couldn't be read. The store's message names its
    /// own row and format ("The stored default agent policy is unreadable."),
    /// so the code chooses the sentence.
    /// </summary>
    internal static string LoadFailureText(ApplicationRunError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code == ApplicationRunErrorCode.StorageUnavailable
            ? "Asura couldn’t read the default AI settings because its settings storage is busy, so agent capabilities are off. "
                + "Restart Asura, or review the settings and save them to turn agent capabilities back on."
            : "Asura couldn’t read the saved default AI settings, so agent capabilities are off. "
                + "Review the settings and save them to turn agent capabilities back on.";
    }

    /// <summary>Why the default couldn't be saved, chosen by the store's code.</summary>
    internal static string SaveFailureText(ApplicationRunError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            ApplicationRunErrorCode.Cancelled =>
                "Saving the default AI settings was canceled. Your changes are still here; save again to keep them.",
            ApplicationRunErrorCode.StorageUnavailable =>
                "Asura couldn’t save the default AI settings because its settings storage is busy. Try again in a moment.",
            _ => "Asura couldn’t save the default AI settings. Try again.",
        };
    }
}
