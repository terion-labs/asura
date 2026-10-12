using Asura.App.Views;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>How a dialog window stands against the window it came from.</summary>
public enum DialogPresentation
{
    /// <summary>
    /// A task the person finishes or cancels before going back: attached under
    /// the owner's title bar like a sheet, not minimizable, not zoomable.
    /// </summary>
    Sheet,

    /// <summary>
    /// A tool consulted while working — a history browser, a statistics view.
    /// It opens beside the owner without blocking it, can be minimized, and
    /// closes with ⌘W or Escape.
    /// </summary>
    Inspector,
}

/// <summary>
/// Every dialog's window, so every dialog answers the keyboard the same way.
///
/// Fourteen of thirty-four dialogs had no Escape path at all, eight marked a
/// default button, none answered ⌘., and each that did handle Escape did it
/// with its own handler. Every one could be minimized while its owner stayed
/// blocked, and each opened wherever the platform centred it. A dialog states
/// its buttons — a Cancel that carries <c>IsCancel</c> and a primary action
/// that carries <c>IsDefault</c> unless it destroys something — and this
/// class supplies the rest:
///
/// <list type="bullet">
/// <item>Escape and ⌘. run the dialog's own Cancel button, so cancelling by
/// key and by click take one path (and a disabled Cancel — a save in flight —
/// refuses both). A search field with text gets the first Escape.</item>
/// <item>A sheet opens under the owner's title bar, centred on it, rather than
/// floating mid-screen, and cannot be minimized or zoomed.</item>
/// <item>On macOS the native title text is hidden: the dialog's own header is
/// the title the person reads, and the window keeps its <c>Title</c> for the
/// Window menu and VoiceOver.</item>
/// </list>
///
/// The architecture tests hold every <c>*Dialog.axaml</c> to this base and to
/// the button contract.
/// </summary>
public class DialogWindow : Window
{
    public static readonly StyledProperty<DialogPresentation> PresentationProperty =
        AvaloniaProperty.Register<DialogWindow, DialogPresentation>(nameof(Presentation));

    static DialogWindow()
    {
        PresentationProperty.Changed.AddClassHandler<DialogWindow>(
            (window, _) => window.ApplyPresentation());
    }

    public DialogWindow()
    {
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplyPresentation();

        // Tunnelled, so the window decides before a focused child that would
        // otherwise swallow the key.
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
        MacOsDialogTitleBar.HideTitle(this);
    }

    public DialogPresentation Presentation
    {
        get => GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Window);

    /// <summary>
    /// Whether this key is one of the cancel gestures: Escape everywhere, and
    /// ⌘. on macOS, where it is the standard cancel in every alert and sheet.
    /// </summary>
    public static bool IsCancelGesture(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape)
        {
            return e.KeyModifiers == KeyModifiers.None;
        }

        return OperatingSystem.IsMacOS()
            && e.KeyModifiers == KeyModifiers.Meta
            && (e.Key == Key.OemPeriod || e.PhysicalKey == PhysicalKey.Period);
    }

    /// <summary>
    /// Cancels the dialog the way its Cancel button does. A dialog whose Cancel
    /// needs more — a draft to discard, an inline form to fold away first —
    /// overrides this rather than handling Escape itself.
    /// </summary>
    protected virtual void Cancel()
    {
        var cancel = this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.IsCancel && button.IsEffectivelyVisible);
        if (cancel is null)
        {
            Close();
            return;
        }

        // A disabled Cancel means the dialog cannot be left right now — a
        // write is in flight — and the keyboard is held to the same rule.
        if (cancel.IsEffectivelyEnabled
            && ControlAutomationPeer.CreatePeerForElement(cancel) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
    }

    /// <summary>
    /// Whether a focused control owns this cancel gesture for itself — a key
    /// recorder that captures ⌘. as a shortcut. Escape still reaches an open
    /// drop-down and a search field with text before this is asked.
    /// </summary>
    protected virtual bool LetsCancelGestureThrough(KeyEventArgs e) => false;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MacOsDialogTitleBar.HideTitle(this);
        if (Presentation == DialogPresentation.Sheet && Owner is Window owner)
        {
            PlaceUnderTitleBar(owner);
        }
    }

    private void ApplyPresentation()
    {
        var inspector = Presentation == DialogPresentation.Inspector;
        CanMinimize = inspector;
        CanMaximize = inspector;
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (Presentation == DialogPresentation.Inspector
            && e.Key == Key.W
            && e.KeyModifiers == KeyModifiers.Meta
            && OperatingSystem.IsMacOS())
        {
            e.Handled = true;
            Close();
            return;
        }

        if (!IsCancelGesture(e)
            || SearchField.ClaimsEscape(e)
            || ClosesOpenDropDown(e)
            || LetsCancelGestureThrough(e))
        {
            return;
        }

        e.Handled = true;
        Cancel();
    }

    /// <summary>An open drop-down closes on Escape before its dialog does.</summary>
    private static bool ClosesOpenDropDown(KeyEventArgs e) =>
        e.Key == Key.Escape
        && (e.Source as Visual)?.FindAncestorOfType<ComboBox>(includeSelf: true)
            is { IsDropDownOpen: true };

    /// <summary>
    /// A sheet hangs from its owner: horizontally centred on it, its top at
    /// the bottom of the owner's title band, and kept on the owner's screen.
    /// </summary>
    private void PlaceUnderTitleBar(Window owner)
    {
        var band = TitleBandHeight(owner);
        var ownerTopLeft = owner.PointToScreen(new Point(0, band));
        var ownerTopRight = owner.PointToScreen(new Point(owner.ClientSize.Width, band));
        var width = (int)Math.Round((FrameSize?.Width ?? ClientSize.Width) * DesktopScaling);
        var height = (int)Math.Round((FrameSize?.Height ?? ClientSize.Height) * DesktopScaling);
        var x = ownerTopLeft.X + ((ownerTopRight.X - ownerTopLeft.X - width) / 2);
        var y = ownerTopLeft.Y;
        if (Screens.ScreenFromWindow(owner)?.WorkingArea is { } area)
        {
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
        }

        Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// How far the owner's title band reaches. A window that draws its own
    /// band into the title bar — the main window's, with its tabs and
    /// controls — marks it with the <c>TitleBar</c> decoration role, and that
    /// band can be taller than the platform's own title bar; a sheet hangs
    /// from whichever is lower, so it never covers the owner's chrome.
    /// </summary>
    internal static double TitleBandHeight(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var band = owner.WindowDecorationMargin.Top;
        foreach (var visual in owner.GetVisualDescendants())
        {
            if (visual is Control { IsEffectivelyVisible: true } control
                && WindowDecorationProperties.GetElementRole(control) == WindowDecorationsElementRole.TitleBar
                && control.TranslatePoint(new Point(0, control.Bounds.Height), owner) is { } bottom)
            {
                band = Math.Max(band, bottom.Y);
            }
        }

        // A title band is a strip, never most of the window.
        return Math.Clamp(band, 0, owner.ClientSize.Height / 2);
    }
}
