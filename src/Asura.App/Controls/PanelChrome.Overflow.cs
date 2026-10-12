using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Asura.App.Controls;

internal sealed partial class PanelChrome
{
    public static readonly StyledProperty<Control?> HeaderOverflowContentProperty =
        AvaloniaProperty.Register<PanelChrome, Control?>(nameof(HeaderOverflowContent));

    public static readonly DirectProperty<PanelChrome, bool> IsHeaderOverflowOpenProperty =
        AvaloniaProperty.RegisterDirect<PanelChrome, bool>(nameof(IsHeaderOverflowOpen), chrome => chrome.IsHeaderOverflowOpen);

    /// <summary>
    /// What an action is called as an item of the "…" menu, in the title case a
    /// menu item takes. A header button says what it does in a tooltip, which is
    /// sentence case; without this the menu falls back to that tooltip.
    /// </summary>
    public static readonly AttachedProperty<string?> OverflowHeaderProperty =
        AvaloniaProperty.RegisterAttached<PanelChrome, Control, string?>("OverflowHeader");

    private bool _isHeaderOverflowOpen;
    private bool _overflowActionInvoked;
    private StackPanel? _headerActions;
    private Panel? _secondaryActions;
    private Panel? _overflowActions;
    private Button? _overflowTrigger;
    private MenuFlyout? _overflowMenu;

    public Control? HeaderOverflowContent
    {
        get => GetValue(HeaderOverflowContentProperty);
        set
        {
            SetValue(HeaderOverflowContentProperty, value);
            UpdateHeaderOverflow();
        }
    }

    public bool IsHeaderOverflowOpen
    {
        get => _isHeaderOverflowOpen;
        private set => SetAndRaise(IsHeaderOverflowOpenProperty, ref _isHeaderOverflowOpen, value);
    }

    /// <summary>The "…" menu while it is open, for tests to read.</summary>
    internal MenuFlyout? HeaderOverflowMenu => _overflowMenu;

    public static string? GetOverflowHeader(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(OverflowHeaderProperty);
    }

    public static void SetOverflowHeader(Control control, string? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(OverflowHeaderProperty, value);
    }

    private void AttachHeaderOverflow(TemplateAppliedEventArgs e)
    {
        _headerActions = e.NameScope.Find<StackPanel>("PART_HeaderActions");
        _secondaryActions = e.NameScope.Find<Panel>("PART_SecondaryActions");
        _overflowActions = e.NameScope.Find<Panel>("PART_OverflowActions");
    }

    private void DetachHeaderOverflow() => _overflowMenu?.Hide();

    /// <summary>
    /// Moves the panel's secondary actions between the header and the hidden
    /// holder behind the "…" button. The controls are moved rather than
    /// copied, so their icons, enabled states and accessibility labels cannot
    /// drift between the header and the menu that stands in for them, and
    /// their bindings stay live while they are out of sight.
    /// </summary>
    private void UpdateHeaderOverflow()
    {
        var destination = IsHeaderCondensed ? _overflowActions : _headerActions;
        if (_secondaryActions?.Parent is Panel current && destination is not null && current != destination)
        {
            current.Children.Remove(_secondaryActions);
            destination.Children.Insert(0, _secondaryActions);
        }

        if (!IsHeaderCondensed && HeaderOverflowContent is null)
        {
            _overflowMenu?.Hide();
        }
    }

    /// <summary>
    /// Opens the "…" menu under <paramref name="trigger"/>, or closes it if it
    /// is open.
    ///
    /// The menu lists the actions the header had no room for, in the order the
    /// header gives them up: the panel's own (collapse, expand, float, split),
    /// then what the panel itself moved out of its header. A row of icons
    /// wrapping under the header was a second toolbar the Mac doesn't have,
    /// and its glyphs said less than the names a menu reads out.
    /// </summary>
    public void ToggleHeaderOverflow(Button trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (_overflowMenu is not null)
        {
            _overflowMenu.Hide();
            return;
        }

        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var item in OverflowMenuItems())
        {
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            return;
        }

        _overflowTrigger = trigger;
        _overflowActionInvoked = false;
        _overflowMenu = menu;
        menu.Closed += OnOverflowMenuClosed;
        IsHeaderOverflowOpen = true;
        menu.ShowAt(trigger);
    }

    private IEnumerable<Control> OverflowMenuItems()
    {
        IEnumerable<Button>[] groups =
        [
            IsHeaderCondensed && _secondaryActions is not null
                ? _secondaryActions.Children.OfType<Button>()
                : [],
            HeaderOverflowContent switch
            {
                Button button => [button],
                { } content => content.GetLogicalDescendants().OfType<Button>(),
                null => [],
            },
        ];

        var separate = false;
        foreach (var group in groups)
        {
            var actions = group.Where(button => button.IsVisible).ToArray();
            if (actions.Length == 0)
            {
                continue;
            }

            if (separate)
            {
                yield return new Separator();
            }

            separate = true;
            foreach (var action in actions)
            {
                yield return OverflowMenuItem(action);
            }
        }
    }

    private MenuItem OverflowMenuItem(Button action)
    {
        var item = new MenuItem
        {
            Header = GetOverflowHeader(action)
                ?? ToolTip.GetTip(action) as string
                ?? Avalonia.Automation.AutomationProperties.GetName(action),
            IsEnabled = action.IsEffectivelyEnabled,
        };
        item.Click += (_, _) =>
        {
            _overflowActionInvoked = true;
            _overflowMenu?.Hide();
            if (action.IsEffectivelyEnabled
                && ControlAutomationPeer.CreatePeerForElement(action) is IInvokeProvider invoke)
            {
                invoke.Invoke();
            }
        };
        return item;
    }

    /// <summary>
    /// Escape or a click elsewhere puts focus back on the "…" button, so the
    /// keyboard is where it was. An action chosen from the menu keeps whatever
    /// focus that action gave.
    /// </summary>
    private void OnOverflowMenuClosed(object? sender, EventArgs e)
    {
        if (sender is MenuFlyout menu)
        {
            menu.Closed -= OnOverflowMenuClosed;
        }

        _overflowMenu = null;
        IsHeaderOverflowOpen = false;
        if (!_overflowActionInvoked && _overflowTrigger is { IsEffectivelyVisible: true } trigger)
        {
            trigger.Focus(NavigationMethod.Tab);
        }
    }

    private void OnOverflowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ToggleHeaderOverflow(button);
            e.Handled = true;
        }
    }
}
