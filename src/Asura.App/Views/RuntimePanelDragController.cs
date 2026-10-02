using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// Extends Dock's in-tab drag with runtime tab targets. Dock still owns splits;
/// a tab drop cancels that gesture before transferring the live panel owner.
/// </summary>
internal sealed class RuntimePanelDragController
{
    private readonly Window _window;
    private readonly CancellationToken _lifetime;
    private Drag? _drag;
    private Grid? _target;
    private bool _started;

    public RuntimePanelDragController(Window window, CancellationToken lifetime)
    {
        _window = window;
        _lifetime = lifetime;
        window.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.Closed += (_, _) => Cancel();
        window.Deactivated += (_, _) => Cancel();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (e.Source is not Visual visual
            || visual.FindAncestorOfType<PanelDockHandle>(includeSelf: true) is not { } handle
            || FloatingPanelLayer.For(handle) is not null
            || handle.FindAncestorOfType<RuntimeDockControl>() is not { RuntimeTab: { } tab } dock
            || handle.FindAncestorOfType<RuntimePanelContentControl>()?.Content is not RuntimePanelViewModel panel
            || _window.DataContext is not MainWindowViewModel { RuntimeWorkspace: { } workspace }
            || !e.Pointer.IsPrimary
            || !e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Cancel();
        _drag = new Drag(handle, dock, e.Pointer, e.GetPosition(_window), workspace.Id, tab.Id, panel.Id);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        _ = sender;
        if (_drag is not { } drag || !ReferenceEquals(e.Pointer, drag.Pointer))
        {
            return;
        }

        // The press and capture/release lifecycle owns this gesture. Native
        // macOS drag moves may omit button modifiers while capture is held.
        var delta = e.GetPosition(_window) - drag.Origin;
        if (!_started && Math.Abs(delta.X) <= 4 && Math.Abs(delta.Y) <= 4)
        {
            return;
        }

        // Avalonia already captures the pressed visual inside the handle.
        // Recapturing its parent would send Dock a premature capture loss.
        _started = true;

        ClearTarget();
        _target = ResolveTarget(e.GetPosition(_window), drag);
        _target?.Classes.Set("panelDropTarget", true);
    }

    private async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        _ = sender;
        if (e.InitialPressMouseButton != MouseButton.Left
            || _drag is not { } drag
            || !ReferenceEquals(e.Pointer, drag.Pointer))
        {
            return;
        }

        var destination = _started
            ? ResolveTarget(e.GetPosition(_window), drag)?.DataContext as RuntimeTabViewModel
            : null;
        if (destination is null)
        {
            // Let Dock finish a valid in-tab split before clearing its state.
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_drag, drag))
                {
                    Cancel();
                }
            });
            return;
        }

        e.Handled = true;
        Cancel();
        if (_window.DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.MovePanelToTabAsync(
                drag.WorkspaceId, drag.TabId, drag.PanelId, destination.Id, _lifetime);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed while the host was processing the move.
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            viewModel.SetError($"The panel could not move: {exception.Message}");
        }
    }

    private Grid? ResolveTarget(Point position, Drag drag)
    {
        if (_window.DataContext is not MainWindowViewModel { RuntimeWorkspace: { } workspace }
            || workspace.Id != drag.WorkspaceId)
        {
            return null;
        }

        return (_window.InputHitTest(position) as Visual)?
            .GetSelfAndVisualAncestors().OfType<Grid>()
            .FirstOrDefault(grid => grid.Classes.Contains("RuntimeTabDropTarget")
                && grid.DataContext is RuntimeTabViewModel tab
                && tab.Id != drag.TabId
                && workspace.Tabs.Contains(tab));
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _ = sender;
        if (ReferenceEquals(e.Pointer, _drag?.Pointer))
        {
            Cancel();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (_drag is not null && e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void Cancel()
    {
        var drag = _drag;
        _drag = null;
        _started = false;
        ClearTarget();
        if (drag is not null)
        {
            // Dock has no public cancel method; capture loss is its supported
            // reset path and removes its preview and placement adorners.
            drag.Dock.RaiseEvent(new PointerCaptureLostEventArgs(drag.Dock, drag.Pointer));
            if (drag.Pointer.Captured is Visual captured
                && captured.FindAncestorOfType<PanelDockHandle>(includeSelf: true) == drag.Handle)
            {
                drag.Pointer.Capture(null);
            }
        }
    }

    private void ClearTarget()
    {
        _target?.Classes.Set("panelDropTarget", false);
        _target = null;
    }

    private sealed record Drag(
        PanelDockHandle Handle,
        RuntimeDockControl Dock,
        IPointer Pointer,
        Point Origin,
        WorkspaceInstanceId WorkspaceId,
        TabInstanceId TabId,
        PanelInstanceId PanelId);
}
