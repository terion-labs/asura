using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.Overlays;
using Asura.Application;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// Restores focus to the interactive surface that owns the current shell route.
/// The window lends this navigator its visual root and lifetime; the navigator
/// owns the route, realization-retry, and cancelled-close focus rules.
/// </summary>
internal sealed class ShellFocusNavigator(
    Window window,
    MainWindowViewModel viewModel,
    Func<SettingsView> settingsRoute,
    Func<LayoutDesignerView> layoutDesigner,
    Func<WorkspaceEditorView?> workspaceEditor,
    CancellationToken lifetime)
{
    private const int PanelFocusAttempts = 6;

    private bool _restoreRouteWhenActivated;

    public void FocusCurrentRoute()
    {
        if (viewModel.IsWorkspaceVisible)
        {
            FocusActivePanel();
        }
    }

    public void FocusActivePanel() => FocusActivePanel(PanelFocusAttempts);

    public void FocusSavedScreenUndo() => Post(() =>
        settingsRoute().FocusSavedScreenUndo());

    /// <summary>
    /// Focuses Undo in the delete notice of <paramref name="host"/>: this
    /// window, or the Settings window showing its settings.
    /// </summary>
    public void FocusDefinitionDeleteUndo(Window host) => Post(() =>
        host.GetVisualDescendants()
            .OfType<DefinitionDeleteUndoNoticeView>()
            .FirstOrDefault()
            ?.FocusUndo());

    public void FocusLayoutDesignerName() => Post(() =>
    {
        if (viewModel.IsLayoutDesignerVisible)
        {
            layoutDesigner().FocusNameEditor();
        }
    });

    public void FocusDefinitionEditor() => Post(() =>
    {
        if (viewModel.IsDefinitionEditorVisible
            && workspaceEditor() is { } editor)
        {
            editor.FocusInitialControl();
        }
    });

    /// <summary>
    /// Moves keyboard focus to the next or previous area of the window, in
    /// reading order: workspace rail, tab strip, the active panel's header, the
    /// active panel's content, then the agent panel. This is AppKit's
    /// Control-F6, and the way out of a terminal that sends every Tab to its
    /// shell. Areas that are hidden or have nothing to focus are skipped.
    /// </summary>
    public void FocusAdjacentArea(int direction)
    {
        if (!viewModel.IsWorkspaceVisible)
        {
            return;
        }

        var areas = FocusAreas().ToList();
        if (areas.Count == 0)
        {
            return;
        }

        var current = window.FocusManager?.GetFocusedElement() is Visual focused
            ? areas.FindIndex(area => area.Contains(focused))
            : -1;
        areas[AdjacentAreaIndex(current, areas.Count, direction)].Focus();
    }

    /// <summary>
    /// The area after (or before) <paramref name="current"/>, wrapping at both
    /// ends. With focus outside every area, forward starts at the first area
    /// and backward at the last.
    /// </summary>
    internal static int AdjacentAreaIndex(int current, int count, int direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return current < 0 || current >= count
            ? direction >= 0 ? 0 : count - 1
            : (current + (direction >= 0 ? 1 : -1) + count) % count;
    }

    public TerminalPresentationHost? FindActiveTerminalHost()
    {
        var activePanel = viewModel.ActivePanel;
        return activePanel is null
            ? null
            : window.GetVisualDescendants()
                .OfType<TerminalPresentationHost>()
                .FirstOrDefault(control =>
                    ReferenceEquals(control.DataContext, activePanel));
    }

    public void RestoreAfterCancelledClose()
    {
        _restoreRouteWhenActivated = true;
        window.Activate();
        Post(RestoreRouteIfActive);
    }

    public void NotifyWindowActivated()
    {
        if (_restoreRouteWhenActivated)
        {
            Post(RestoreRouteIfActive);
        }
    }

    private void FocusActivePanel(int attemptsRemaining) => Post(() =>
    {
        if (viewModel.ActivePanel is not { } activePanel)
        {
            return;
        }

        var terminal = FindActiveTerminalHost();
        if (terminal is not null)
        {
            terminal.RequestInputFocus();
            return;
        }

        var browser = window.GetVisualDescendants()
            .OfType<BrowserPresentationHost>()
            .FirstOrDefault(control =>
                ReferenceEquals(control.DataContext, activePanel));
        if (browser is not null)
        {
            browser.RequestInputFocus();
            return;
        }

        // Render-backed panels are realized during layout. Retrying at Loaded
        // priority avoids moving focus elsewhere while a native surface is
        // still becoming available.
        if (attemptsRemaining > 0
            && activePanel is TerminalRuntimePanelViewModel
                or BrowserRuntimePanelViewModel)
        {
            FocusActivePanel(attemptsRemaining - 1);
            return;
        }

        if (activePanel is TerminalRuntimePanelViewModel)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "terminal.focus.fallback",
                SecretSafeDiagnosticKind.Unexpected);
        }

        window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control =>
                ReferenceEquals(control.DataContext, activePanel)
                && control.Classes.Contains("RuntimePanelFocusTarget"))
            ?.Focus();
    });

    private IEnumerable<FocusArea> FocusAreas()
    {
        var descendants = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.IsEffectivelyVisible)
            .ToList();

        var currentTile = descendants.OfType<WorkspaceRailTile>()
            .OrderByDescending(tile => tile.IsCurrent)
            .FirstOrDefault();
        var rail = currentTile?.GetVisualAncestors()
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("FloatingSidebar"));
        if (currentTile is not null && rail is not null)
        {
            yield return new FocusArea(rail, () => _ = FocusFirst(currentTile) || FocusFirst(rail));
        }

        var tabStrip = descendants.OfType<RuntimeTabStripView>().FirstOrDefault();
        if (tabStrip is not null && FirstFocusable(tabStrip) is not null)
        {
            var activeTab = viewModel.RuntimeWorkspace?.ActiveTab;
            yield return new FocusArea(
                tabStrip,
                () => _ = (activeTab is not null
                        && FocusFirst(tabStrip, control => ReferenceEquals(control.DataContext, activeTab)))
                    || FocusFirst(tabStrip));
        }

        var activePanel = viewModel.ActivePanel;
        var chrome = activePanel is null
            ? null
            : descendants.OfType<PanelChrome>().FirstOrDefault(control =>
                ReferenceEquals(control.DataContext, activePanel));
        var header = chrome?.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("PanelHeader"));
        if (header is not null && FirstFocusable(header) is not null)
        {
            yield return new FocusArea(header, () => _ = FocusFirst(header));
        }

        if (chrome is not null)
        {
            yield return new FocusArea(chrome, FocusActivePanel);
        }

        var agent = descendants.OfType<AgentWorkspaceView>().FirstOrDefault();
        if (agent is not null && FirstFocusable(agent) is not null)
        {
            yield return new FocusArea(
                agent,
                () => _ = FocusFirst(
                        agent,
                        control => string.Equals(control.Name, "AgentChatPromptInput", StringComparison.Ordinal))
                    || FocusFirst(agent));
        }
    }

    private static bool FocusFirst(Visual root, Func<Control, bool>? prefer = null)
    {
        var target = FirstFocusable(root, prefer);
        return target is not null && target.Focus(NavigationMethod.Tab);
    }

    private static InputElement? FirstFocusable(Visual root, Func<Control, bool>? prefer = null)
    {
        var candidates = root.GetSelfAndVisualDescendants()
            .OfType<InputElement>()
            .Where(element => element.Focusable
                && element.IsEffectivelyVisible
                && element.IsEffectivelyEnabled);
        if (prefer is null)
        {
            return candidates.FirstOrDefault();
        }

        return root.GetSelfAndVisualDescendants()
            .OfType<Control>()
            .Where(prefer)
            .SelectMany(control => control.GetSelfAndVisualDescendants().OfType<InputElement>())
            .FirstOrDefault(element => element.Focusable
                && element.IsEffectivelyVisible
                && element.IsEffectivelyEnabled);
    }

    private void RestoreRouteIfActive()
    {
        if (!_restoreRouteWhenActivated || !window.IsVisible || !window.IsActive)
        {
            return;
        }

        _restoreRouteWhenActivated = false;
        FocusCurrentRoute();
    }

    /// <param name="Root">Everything inside belongs to the area.</param>
    /// <param name="Focus">Puts focus where a person entering the area expects it.</param>
    private sealed record FocusArea(Visual Root, Action Focus)
    {
        public bool Contains(Visual visual) =>
            ReferenceEquals(Root, visual) || Root.IsVisualAncestorOf(visual);
    }

    private void Post(Action action) => Dispatcher.UIThread.Post(
        () =>
        {
            if (!lifetime.IsCancellationRequested)
            {
                action();
            }
        },
        DispatcherPriority.Loaded);
}
