using System.Diagnostics;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
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
    private bool _addPanelGrowsFromPlus;

    /// <summary>The class an anchor wears while its popover is open.</summary>
    internal const string AnchorOpenClass = "anchorOpen";

    /// <summary>The scale the popover grows from, never from nothing.</summary>
    private const float AddPanelStartScale = 0.9f;

    /// <summary>
    /// The longest the popover waits, unseen, for the workspace to finish
    /// building the cell it was opened for before it grows.
    /// </summary>
    private static readonly TimeSpan AddPanelLongestWait = TimeSpan.FromSeconds(1);

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

    /// <param name="fromPlusMenu">
    /// Opened by choosing an edge in the "+" button's menu, the way the
    /// popover is opened by hand. Add Panel… from the menu bar or its
    /// shortcut passes false.
    /// </param>
    private void ShowNewPanelChooser(Control? anchor, bool fromPlusMenu = false)
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
        _addPanelFlyoutAnchor?.RemoveHandler(PointerPressedEvent, OnAddPanelAnchorPressed);
        _addPanelFlyoutAnchor = target;
        target.Classes.Add(AnchorOpenClass);
        target.AddHandler(PointerPressedEvent, OnAddPanelAnchorPressed, RoutingStrategies.Tunnel);
        // Opened from the "+" button's menu, the popover grows from the "+".
        // Opened from the keyboard it is simply there: a shortcut answered
        // with motion feels slower than one answered at once.
        _addPanelGrowsFromPlus = fromPlusMenu
            && ReferenceEquals(target, AddPanelButton())
            && !Classes.Contains("motion-disabled");
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
        flyout.Opened += (_, _) =>
        {
            if (_addPanelGrowsFromPlus && chooser.FindAncestorOfType<FlyoutPresenter>() is { } presenter)
            {
                GrowFromPlusOnceSettled(presenter);
            }

            _addPanelGrowsFromPlus = false;
            chooser.FocusInitialAction();
        };
        flyout.Closed += (_, _) =>
        {
            _addPanelFlyoutAnchor?.Classes.Remove(AnchorOpenClass);
            _addPanelFlyoutAnchor?.RemoveHandler(PointerPressedEvent, OnAddPanelAnchorPressed);
            _addPanelFlyoutAnchor = null;
            if (IsActive && ViewModel.ActivePanel is not null)
            {
                FocusActivePanel();
            }
        };
        return flyout;
    }

    /// <summary>
    /// The popover grows out of the "+" that opened it: from its top-right
    /// corner, which sits under the button, and from clear to solid.
    ///
    /// It opens just as the workspace builds the cell that was placed, which
    /// holds the UI thread for 200–650 ms, and laying out the popover's own
    /// content holds it again. A growth that overlaps that work is drawn in a
    /// few frozen frames. So the popover waits, unseen at its starting size,
    /// until the UI thread has caught up (a Background job runs only after
    /// layout and rendering), and then grows on the compositor with nothing
    /// competing with it. If the UI thread never catches up, the wait ends
    /// on its own after <see cref="AddPanelLongestWait"/> and the popover is
    /// simply there.
    ///
    /// Both steps are compositor animations rather than a change to the
    /// presenter's Opacity: Avalonia copies a control's Opacity onto its
    /// visual whenever the control changes, which would cut an animation
    /// short, while an animation leaves that copy untouched.
    /// </summary>
    private void GrowFromPlusOnceSettled(FlyoutPresenter presenter)
    {
        if (ElementComposition.GetElementVisual(presenter) is not { } visual)
        {
            return;
        }

        visual.CenterPoint = new Vector3D(presenter.Bounds.Width, 0, 0);
        Animate(visual, AddPanelLongestWait, waitOnly: true);

        var waiting = Stopwatch.StartNew();
        Dispatcher.UIThread.Post(
            () =>
            {
                if (IsAddPanelChooserOpen && waiting.Elapsed < AddPanelLongestWait)
                {
                    visual.CenterPoint = new Vector3D(presenter.Bounds.Width, 0, 0);
                    Animate(visual, MotionTokens.Popover, waitOnly: false);
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// From clear and slightly small to the popover as it rests. Waiting
    /// holds the start for the whole duration and lands at rest only at its
    /// very end; growing eases there over the duration.
    /// </summary>
    private static void Animate(
        CompositionVisual visual,
        TimeSpan duration,
        bool waitOnly)
    {
        var compositor = visual.Compositor;
        var held = waitOnly ? 0.999f : 0f;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(held, 0f);
        fade.InsertKeyFrame(1f, 1f, MotionTokens.EaseOut);
        visual.StartAnimation(nameof(CompositionVisual.Opacity), fade);

        var start = new Vector3D(AddPanelStartScale, AddPanelStartScale, 1);
        var grow = compositor.CreateVector3DKeyFrameAnimation();
        grow.Duration = duration;
        grow.InsertKeyFrame(0f, start);
        grow.InsertKeyFrame(held, start);
        grow.InsertKeyFrame(1f, new Vector3D(1, 1, 1), MotionTokens.EaseOut);
        visual.StartAnimation(nameof(CompositionVisual.Scale), grow);
    }

    /// <summary>
    /// A press on the button the popover hangs from closes the popover, as
    /// pressing a toolbar button again does on macOS. Avalonia lets that press
    /// through the popover to its own button, and the "+" would otherwise open
    /// its edge menu on top of the popover it already has open.
    /// </summary>
    private void OnAddPanelAnchorPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (_addPanelFlyout is { IsOpen: true } flyout)
        {
            e.Handled = true;
            flyout.Hide();
        }
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
