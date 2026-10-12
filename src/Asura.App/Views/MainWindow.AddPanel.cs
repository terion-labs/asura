using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Overlays;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Views;

/// <summary>
/// Add Panel, as a popover anchored to the "+" in the title band. It used to
/// be a card over a blacked-out window that only Esc could close; a popover
/// shows where the panel is coming from, keeps the workspace in view, and
/// closes on Esc or a click anywhere else.
/// </summary>
public sealed partial class MainWindow
{
    private Flyout? _addPanelFlyout;
    private Control? _addPanelFlyoutAnchor;

    /// <summary>The class an anchor wears while its popover is open.</summary>
    internal const string AnchorOpenClass = "anchorOpen";

    /// <summary>Whether the Add Panel popover is showing.</summary>
    internal bool IsAddPanelChooserOpen => _addPanelFlyout?.IsOpen == true;

    /// <summary>The popover's content, once it has been opened.</summary>
    internal NewPanelChooserView? AddPanelChooser => _newPanelChooserOverlay;

    /// <summary>
    /// Add Panel… from the menu bar or its shortcut: the popover on the "+"
    /// when the title band shows it, otherwise on the active panel's header.
    /// </summary>
    public Task ShowNewPanelChooserAsync()
    {
        ShowNewPanelChooser(anchor: null);
        return Task.CompletedTask;
    }

    private void ShowNewPanelChooser(Control? anchor)
    {
        // The palette that asked has done its job.
        ViewModel.CloseOverlay();
        if (ViewModel.RuntimeWorkspace?.ActiveTab is null)
        {
            ViewModel.SetError("Open a workspace tab before adding a panel.");
            return;
        }

        ViewModel.ShowWorkspace();
        if (!ViewModel.IsWorkspaceVisible)
        {
            return;
        }

        var target = anchor ?? AddPanelAnchor();
        if (target is null)
        {
            return;
        }

        var chooser = EnsureNewPanelChooser();
        var flyout = _addPanelFlyout ??= CreateAddPanelFlyout(chooser);
        var margin = this.TryFindResource("ShellSpaceXl", out var value) && value is double spacing
            ? spacing
            : 24;
        chooser.FitTo(Bounds.Size, margin);
        flyout.Placement = ReferenceEquals(target, AddPanelButton())
            ? PlacementMode.BottomEdgeAlignedRight
            : PlacementMode.Bottom;
        // The "+" stays pressed while its popover is open, as a toolbar
        // button that shows a popover does on macOS.
        _addPanelFlyoutAnchor?.Classes.Remove(AnchorOpenClass);
        _addPanelFlyoutAnchor = target;
        target.Classes.Add(AnchorOpenClass);
        flyout.ShowAt(target);
    }

    /// <summary>
    /// The "+" in the title band when it is showing; otherwise the header of
    /// the active panel, or the workspace itself when no panel has one.
    /// </summary>
    private Control? AddPanelAnchor()
    {
        if (AddPanelButton() is { IsEffectivelyVisible: true } button)
        {
            return button;
        }

        var activePanel = ViewModel.ActivePanel;
        var header = activePanel is null
            ? null
            : this.GetVisualDescendants()
                .OfType<PanelChrome>()
                .FirstOrDefault(chrome => ReferenceEquals(chrome.DataContext, activePanel))
                ?.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("PanelHeader")
                    && border.IsEffectivelyVisible);
        return header
            ?? this.FindControl<WorkspaceView>("WorkspaceRouteView") as Control;
    }

    private Button? AddPanelButton() =>
        this.FindControl<WorkspaceView>("WorkspaceRouteView")
            ?.FindControl<Button>("AddPanelButton");

    private NewPanelChooserView EnsureNewPanelChooser()
    {
        if (_newPanelChooserOverlay is { } existing)
        {
            return existing;
        }

        if (!Resources.TryGetResource("NewPanelChooserTemplate", ActualThemeVariant, out var resource)
            || resource is not IDataTemplate template
            || template.Build(ViewModel) is not NewPanelChooserView chooser)
        {
            throw new InvalidOperationException("The Add Panel chooser template is unavailable.");
        }

        chooser.DataContext = ViewModel;
        // Choosing anything in it — a panel type, a connection, a screen, the
        // Layout Designer — is the answer, so the popover goes once its
        // button has done its work.
        chooser.AddHandler(
            Button.ClickEvent,
            (_, _) => _addPanelFlyout?.Hide(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _newPanelChooserOverlay = chooser;
        return chooser;
    }

    private Flyout CreateAddPanelFlyout(NewPanelChooserView chooser)
    {
        var flyout = new Flyout
        {
            Content = chooser,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Standard,
        };
        flyout.FlyoutPresenterClasses.Add("AddPanelChooser");
        flyout.Opened += (_, _) => chooser.FocusInitialAction();
        flyout.Closed += (_, _) =>
        {
            _addPanelFlyoutAnchor?.Classes.Remove(AnchorOpenClass);
            _addPanelFlyoutAnchor = null;
            if (IsActive && ViewModel.ActivePanel is not null)
            {
                FocusActivePanel();
            }
        };
        return flyout;
    }

    /// <summary>
    /// Adds what was chosen in the popover. When the active panel is an empty
    /// one — just placed against an edge — the new panel takes its place
    /// rather than landing beside it.
    /// </summary>
    private async Task AddPanelFromChooserAsync(Func<Task<bool>> create)
    {
        var tab = ViewModel.RuntimeWorkspace?.ActiveTab;
        var placeholder = tab?.ActivePanel as PanelPlaceholderViewModel;
        if (tab is not null && placeholder is not null)
        {
            tab.ReplaceTarget = placeholder.Id;
        }

        try
        {
            if (await create())
            {
                FocusActivePanel();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (tab is not null && placeholder is not null && tab.ReplaceTarget == placeholder.Id)
            {
                tab.ReplaceTarget = null;
            }
        }
    }
}
