using Asura.App.Views;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>How a dialog window stands against the window it came from.</summary>
public enum DialogPresentation
{
    /// <summary>
    /// A task the person finishes or cancels before going back. Its opener
    /// waits for the answer.
    /// </summary>
    Sheet,

    /// <summary>
    /// A tool consulted while working — a history browser, a statistics view.
    /// Its opener does not wait for it, and it closes with ⌘W or Escape.
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
/// <item>Over a window that is on screen, the dialog is a sheet inside it
/// (<see cref="SheetHost"/>) rather than a window of its own, so it cannot
/// fall behind the window it asks over. Its code does not change: it is
/// still shown with <c>ShowDialog</c> and answers with <c>Close(result)</c>.</item>
/// </list>
///
/// The architecture tests hold every <c>*Dialog.axaml</c> to this base and to
/// the button contract.
/// </summary>
public class DialogWindow : Window
{
    public static readonly StyledProperty<DialogPresentation> PresentationProperty =
        AvaloniaProperty.Register<DialogWindow, DialogPresentation>(nameof(Presentation));

    private SheetHost? _sheetHost;
    private SheetCard? _sheetCard;
    private object? _closeResult;

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

    /// <summary>Whether the dialog is showing as a sheet inside another window.</summary>
    internal bool IsSheet => _sheetCard is not null;

    /// <summary>
    /// The dialog's content wherever it is showing: in its card while it is a
    /// sheet, in its own window otherwise.
    /// </summary>
    internal Visual ContentRoot => _sheetCard ?? (Visual)this;

    /// <summary>The window's file pickers: those of the window the sheet is in.</summary>
    public new IStorageProvider StorageProvider =>
        _sheetHost?.Window.StorageProvider ?? base.StorageProvider;

    /// <summary>The clipboard, reached through the window the sheet is in.</summary>
    public new IClipboard? Clipboard => _sheetHost?.Window.Clipboard ?? base.Clipboard;

    /// <summary>
    /// Shows the dialog as a sheet in <paramref name="owner"/> and answers with
    /// what it closes with. An owner that is not on screen gets a window of
    /// its own, as before.
    /// </summary>
    public new Task<TResult> ShowDialog<TResult>(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return SheetWindowFor(owner) is { } window
            ? ShowAsSheetAsync<TResult>(window)
            : base.ShowDialog<TResult>(owner);
    }

    /// <inheritdoc cref="ShowDialog{TResult}(Window)"/>
    public new Task ShowDialog(Window owner) => ShowDialog<object?>(owner);

    /// <summary>
    /// Shows an inspector without waiting for it, as a sheet in
    /// <paramref name="owner"/> when it is on screen.
    /// </summary>
    public new void Show(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (SheetWindowFor(owner) is { } window)
        {
            _ = ShowAsSheetAsync<object?>(window);
            return;
        }

        base.Show(owner);
    }

    /// <summary>Brings the dialog forward: a sheet takes the keyboard.</summary>
    public new void Activate()
    {
        if (IsSheet)
        {
            FocusSheet();
            return;
        }

        base.Activate();
    }

    /// <summary>Closes the dialog with no answer.</summary>
    public new void Close() => Close(null);

    /// <summary>
    /// Closes the dialog with <paramref name="dialogResult"/> as its answer. A
    /// close its <c>Closing</c> refuses leaves it showing, as for a window.
    /// </summary>
    public new void Close(object? dialogResult)
    {
        _closeResult = dialogResult;
        base.Close(dialogResult);
    }

    /// <summary>Cancels the sheet the way its Cancel button does.</summary>
    internal void CancelSheet() => Cancel();

    /// <summary>Gives the keyboard to the sheet: its focused field, or its first.</summary>
    internal void FocusSheet()
    {
        if (_sheetCard is not { } card)
        {
            return;
        }

        if (TopLevel.GetTopLevel(card)?.FocusManager?.GetFocusedElement() is Visual focused
            && card.IsVisualAncestorOf(focused))
        {
            return;
        }

        var first = card.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(element => element is { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true }
                && KeyboardNavigation.GetIsTabStop(element));
        _ = first?.Focus(NavigationMethod.Tab) ?? card.Focus();
    }

    /// <summary>
    /// The window a dialog over <paramref name="owner"/> stands in: the owner
    /// itself, or, for a dialog asked from a sheet, the window that sheet is
    /// in. None when the owner is not on screen.
    /// </summary>
    private static Window? SheetWindowFor(Window owner) => owner switch
    {
        DialogWindow { _sheetHost: { } host } => host.Window,
        { IsVisible: true } => owner,
        _ => null,
    };

    /// <summary>
    /// An editor — a connection, a provider, a saved screen — is a page of
    /// its own where the window offers pages, as an item is in System
    /// Settings; a question is a sheet over whatever it asks about.
    /// </summary>
    private static bool PrefersPage(Control content) =>
        content is DialogShell { Chrome: DialogChrome.Editor };

    /// <summary>
    /// Return presses the sheet's default button, and only the sheet's: the
    /// window it stands in may mark a default button of its own, and that
    /// one waits behind the sheet like everything else. A field that takes
    /// Return for itself — a multi-line text box — has handled it already.
    /// </summary>
    private void OnSheetReturn(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Handled
            || e.Key != Key.Enter
            || e.KeyModifiers != KeyModifiers.None
            || _sheetCard is not { } card)
        {
            return;
        }

        e.Handled = true;
        var button = card.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(candidate => candidate.IsDefault && candidate.IsEffectivelyVisible);
        if (button is { IsEffectivelyEnabled: true }
            && ControlAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke)
        {
            invoke.Invoke();
        }
    }

    private async Task<TResult> ShowAsSheetAsync<TResult>(Window window)
    {
        if (Content is not Control content)
        {
            throw new InvalidOperationException("A dialog shown as a sheet needs a control as its content.");
        }

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(object? sender, EventArgs e) => closed.TrySetResult();

        _closeResult = null;
        Content = null;
        var host = SheetHost.For(window);
        var pages = PrefersPage(content) ? window as IDialogPageHost : null;
        var page = new SheetCard(this, content, isPage: true);
        SheetCard card;
        if (pages?.TryShowDialogPage(page) == true)
        {
            card = page;
        }
        else
        {
            page.Child = null;
            pages = null;
            card = host.Present(this, content);
        }

        var styles = Styles.ToArray();
        foreach (var style in styles)
        {
            _ = Styles.Remove(style);
            card.Styles.Add(style);
        }

        _sheetHost = host;
        _sheetCard = card;
        card.AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
        card.AddHandler(KeyDownEvent, OnSheetReturn, RoutingStrategies.Bubble);
        void OnTitleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TitleProperty && pages is not null)
            {
                pages.RetitleDialogPage(card, Title ?? string.Empty);
            }
        }

        PropertyChanged += OnTitleChanged;
        Closed += OnClosed;
        try
        {
            // As a window opens once it is laid out, so does a sheet: its
            // fields have their templates by then, and Opened can focus one.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (!closed.Task.IsCompleted)
            {
                FocusSheet();
                OnOpened(EventArgs.Empty);
            }

            await closed.Task;
        }
        finally
        {
            Closed -= OnClosed;
            PropertyChanged -= OnTitleChanged;
            card.RemoveHandler(KeyDownEvent, OnDialogKeyDown);
            card.RemoveHandler(KeyDownEvent, OnSheetReturn);
            if (pages is not null)
            {
                pages.HideDialogPage(card);
            }
            else
            {
                host.Dismiss(this);
            }

            foreach (var style in styles)
            {
                _ = card.Styles.Remove(style);
                Styles.Add(style);
            }

            card.Child = null;
            Content = content;
            _sheetHost = null;
            _sheetCard = null;
        }

        return _closeResult is TResult result ? result : default!;
    }

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
        var cancel = ContentRoot.GetVisualDescendants()
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
        if (IsSheet)
        {
            return;
        }

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
