using System.ComponentModel;
using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// Owns the recovery projection lifecycle for the active runtime workspace:
/// subscriptions, safe serialization, queued writes, and write-failure display.
/// </summary>
public sealed class RuntimeWorkspaceRecoveryCoordinator : IDisposable
{
    private readonly RuntimeRecoveryWriter? _writer;
    private readonly Func<RuntimeWorkspaceViewModel?> _currentWorkspace;
    private readonly Func<RuntimeHistorySource?> _currentSource;
    private readonly Func<bool> _isShutdown;
    private readonly Action _queueWorkspaceAutoSave;
    private readonly Action<string> _setError;
    private readonly IUiThreadDispatcher _uiThreadDispatcher;
    private bool _sealed;
    private bool _disposed;
    private string? _snapshotKey = RuntimeWorkspaceRecoveryCodec.SnapshotKey;

    public RuntimeWorkspaceRecoveryCoordinator(
        RuntimeRecoveryWriter? writer,
        Func<RuntimeWorkspaceViewModel?> currentWorkspace,
        Func<RuntimeHistorySource?> currentSource,
        Func<bool> isShutdown,
        Action queueWorkspaceAutoSave,
        Action<string> setError,
        IUiThreadDispatcher uiThreadDispatcher)
    {
        _writer = writer;
        _currentWorkspace = currentWorkspace
            ?? throw new ArgumentNullException(nameof(currentWorkspace));
        _currentSource = currentSource
            ?? throw new ArgumentNullException(nameof(currentSource));
        _isShutdown = isShutdown
            ?? throw new ArgumentNullException(nameof(isShutdown));
        _queueWorkspaceAutoSave = queueWorkspaceAutoSave
            ?? throw new ArgumentNullException(nameof(queueWorkspaceAutoSave));
        _setError = setError ?? throw new ArgumentNullException(nameof(setError));
        _uiThreadDispatcher = uiThreadDispatcher
            ?? throw new ArgumentNullException(nameof(uiThreadDispatcher));
        _writer?.WriteFailed += OnWriteFailed;
    }

    /// <summary>
    /// The key this window's snapshots are written under, one per window so a
    /// second window no longer overwrites the first. Null when the window has
    /// no recovery slot and is not restored.
    /// </summary>
    public string? SnapshotKey => _snapshotKey;

    /// <summary>
    /// Gives the window its recovery slot, or none. Assigned before the
    /// window restores or writes anything, so each window reads and writes
    /// only its own snapshot.
    /// </summary>
    public void AssignWindowSlot(int? slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _snapshotKey = slot is { } value
            ? RuntimeWorkspaceRecoveryCodec.SnapshotKeyFor(value)
            : null;
    }

    /// <summary>
    /// The window was closed on its own while Asura goes on running. Its slot
    /// is emptied, so neither the next window to take the slot nor the next
    /// launch brings back a workspace that was deliberately closed. Also
    /// after the window's model is disposed: a quit that a later window
    /// stops releases the slots of the windows it had already closed.
    /// </summary>
    public void ReleaseWindowSlot()
    {
        if (_snapshotKey is not { } snapshotKey)
        {
            return;
        }

        _snapshotKey = null;
        // A writer already sealed for exit refuses this, and that is fine:
        // the run is ending and nothing else will take the slot.
        _ = _writer?.Enqueue(
            snapshotKey,
            RuntimeWorkspaceRecoveryCodec.SchemaVersion,
            RuntimeWorkspaceRecoveryCodec.Serialize(null));
    }

    public void Track(RuntimeWorkspaceViewModel? workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (workspace is null)
        {
            return;
        }

        foreach (var panel in workspace.Tabs.SelectMany(tab => tab.Panels))
        {
            Track(panel);
        }

        foreach (var tab in workspace.Tabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.PropertyChanged += OnTabPropertyChanged;
        }
    }

    public void Track(RuntimePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRecoveryRelevant(panel))
        {
            panel.PropertyChanged += OnPanelPropertyChanged;
        }
    }

    public void Untrack(RuntimeWorkspaceViewModel? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        foreach (var panel in workspace.Tabs.SelectMany(tab => tab.Panels))
        {
            Untrack(panel);
        }

        foreach (var tab in workspace.Tabs)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
        }
    }

    public void Untrack(RuntimePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (IsRecoveryRelevant(panel))
        {
            panel.PropertyChanged -= OnPanelPropertyChanged;
        }
    }

    public void QueueSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _queueWorkspaceAutoSave();
        if (_writer is null || _snapshotKey is not { } snapshotKey || _sealed || _isShutdown())
        {
            return;
        }

        string payload;
        try
        {
            payload = RuntimeWorkspaceRecoveryCodec.Serialize(
                _currentWorkspace(),
                _currentSource());
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or InvalidOperationException
                or System.Text.Json.JsonException)
        {
            SecretSafeDiagnostics.WriteTraceAndStandardError(
                "recovery.snapshot-prepare.failed",
                exception);
            _setError("Asura couldn't save what it needs to reopen your tabs after a restart. Your work is unaffected.");
            return;
        }

        var queued = _writer.Enqueue(
            snapshotKey,
            RuntimeWorkspaceRecoveryCodec.SchemaVersion,
            payload);
        if (!queued.IsSuccess)
        {
            PresentWriteFailure(queued.Error!);
        }
    }

    public void Seal()
    {
        if (_sealed)
        {
            return;
        }

        _sealed = true;
        _writer?.WriteFailed -= OnWriteFailed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Seal();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender switch
        {
            FileRuntimePanelViewModel => eventArgs.PropertyName is
                nameof(FileRuntimePanelViewModel.SelectedProfile)
                or nameof(FileRuntimePanelViewModel.CurrentLocation)
                or nameof(FileRuntimePanelViewModel.ShowHidden),
            BrowserRuntimePanelViewModel => eventArgs.PropertyName is
                nameof(BrowserRuntimePanelViewModel.CurrentAddress)
                or nameof(BrowserRuntimePanelViewModel.ConnectionId),
            DatabaseRuntimePanelViewModel => eventArgs.PropertyName is
                nameof(DatabaseRuntimePanelViewModel.RecoveryTarget)
                or nameof(DatabaseRuntimePanelViewModel.TunnelConnectionId),
            RedisRuntimePanelViewModel => eventArgs.PropertyName is
                nameof(RedisRuntimePanelViewModel.RecoveryTarget)
                or nameof(RedisRuntimePanelViewModel.TunnelConnectionId),
            TerminalRuntimePanelViewModel => eventArgs.PropertyName is
                nameof(TerminalRuntimePanelViewModel.MultiplexerSession),
            _ => false,
        })
        {
            QueueSnapshot();
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is RuntimeTabViewModel
            && string.Equals(
                eventArgs.PropertyName,
                nameof(RuntimeTabViewModel.DockLayoutRevision),
                StringComparison.Ordinal))
        {
            QueueSnapshot();
        }
    }

    private void OnWriteFailed(
        object? sender,
        RuntimeRecoveryWriteFailedEventArgs eventArgs)
    {
        _ = sender;
        PresentWriteFailure(eventArgs.Error);
    }

    private void PresentWriteFailure(ApplicationRunError error)
    {
        SecretSafeDiagnosticProjection.WriteStandardError(
            "recovery.runtime-write.failed",
            SecretSafeDiagnosticKind.Unexpected);
        _ = _uiThreadDispatcher.InvokeAsync(
            () => _setError($"Runtime recovery is unavailable ({error.Code})."),
            CancellationToken.None);
    }

    private static bool IsRecoveryRelevant(RuntimePanelViewModel panel) =>
        panel is FileRuntimePanelViewModel
            or BrowserRuntimePanelViewModel
            or DatabaseRuntimePanelViewModel
            or RedisRuntimePanelViewModel
            or TerminalRuntimePanelViewModel;
}
