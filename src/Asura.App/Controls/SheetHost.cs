using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// The dialogs a window is showing inside itself, as macOS 26 System Settings
/// shows its sheets: the window dims, and the dialog stands on it as a card.
///
/// A dialog in a window of its own could fall behind the window it belonged
/// to, and Settings could fall behind the main window with it, leaving no
/// way back. A sheet cannot: it is part of the window it asks over. The
/// dialog keeps its window object, so its code, its name scope, its
/// <c>Close(result)</c> and its lifecycle are unchanged; only its content
/// moves into the card for as long as it is shown.
/// </summary>
internal sealed class SheetHost
{
    private static readonly ConditionalWeakTable<Window, SheetHost> Hosts = [];

    private readonly Window _window;
    private readonly List<Sheet> _sheets = [];

    private SheetHost(Window window)
    {
        _window = window;
        _window.Closing += OnWindowClosing;
        _window.Closed += OnWindowClosed;
        _window.PropertyChanged += (_, e) =>
        {
            if (e.Property == TopLevel.ClientSizeProperty)
            {
                Resize();
            }
        };
    }

    /// <summary>The window the sheets stand in.</summary>
    public Window Window => _window;

    /// <summary>The dialog in front, which answers the keyboard.</summary>
    public DialogWindow? Front => _sheets.Count == 0 ? null : _sheets[^1].Dialog;

    /// <summary>The sheets of <paramref name="window"/>, made on first use.</summary>
    public static SheetHost For(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Hosts.GetValue(window, static key => new SheetHost(key));
    }

    /// <summary>
    /// Whether <paramref name="window"/> has a sheet up. Its shortcuts and
    /// menu commands wait while it does, as they did behind a modal window.
    /// </summary>
    public static bool IsPresenting(Window? window) =>
        window is not null && Hosts.TryGetValue(window, out var host) && host._sheets.Count > 0;

    /// <summary>The dialog whose card <paramref name="visual"/> sits in, if any.</summary>
    public static DialogWindow? DialogFor(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return visual.FindAncestorOfType<SheetCard>(includeSelf: true)?.Dialog;
    }

    /// <summary>
    /// Shows <paramref name="content"/> for <paramref name="dialog"/> in front
    /// of everything else in the window, the sheets already up included.
    /// </summary>
    public SheetCard Present(DialogWindow dialog, Control content)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(content);
        if (OverlayLayer.GetOverlayLayer(_window) is not { } layer)
        {
            throw new InvalidOperationException("The window has no overlay layer to show a sheet in.");
        }

        var card = new SheetCard(dialog, content);
        var scrim = new Border();
        scrim.Classes.Add("SheetScrim");
        var root = new SheetLayout(scrim, card);
        var sheet = new Sheet(dialog, root, _window.FocusManager?.GetFocusedElement(), NativeSurfaceLayer.Suspend());
        _sheets.Add(sheet);
        layer.Children.Add(root);
        Resize();
        return card;
    }

    /// <summary>
    /// Takes <paramref name="dialog"/>'s card down and gives focus back to
    /// where it was before the card went up.
    /// </summary>
    public void Dismiss(DialogWindow dialog)
    {
        var index = _sheets.FindIndex(sheet => ReferenceEquals(sheet.Dialog, dialog));
        if (index < 0)
        {
            return;
        }

        var sheet = _sheets[index];
        _sheets.RemoveAt(index);
        if (sheet.Root.Parent is Panel layer)
        {
            _ = layer.Children.Remove(sheet.Root);
        }

        sheet.Suspension.Dispose();
        if (index == _sheets.Count)
        {
            // The sheet in front went: focus goes back to the sheet now in
            // front, or to what the window had before any went up.
            var restore = sheet.PreviousFocus;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (restore is InputElement { IsEffectivelyVisible: true } element
                        && TopLevel.GetTopLevel(element) is not null)
                    {
                        _ = element.Focus();
                    }
                },
                DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// The window's own close button waits for its sheet to be answered, as
    /// it does behind a modal window; closing it from code — quitting — does
    /// not wait, and the sheets go with it.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _ = sender;
        if (_sheets.Count > 0 && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Front?.FocusSheet();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        foreach (var sheet in _sheets.ToArray())
        {
            sheet.Dialog.Close();
        }
    }

    /// <summary>
    /// The overlay layer gives a child its own size, not the window's; each
    /// sheet is held to the window's so its wash covers all of it.
    /// </summary>
    private void Resize()
    {
        var size = _window.ClientSize;
        foreach (var sheet in _sheets)
        {
            sheet.Root.Width = size.Width;
            sheet.Root.Height = size.Height;
        }
    }

    private sealed record Sheet(
        DialogWindow Dialog,
        SheetLayout Root,
        IInputElement? PreviousFocus,
        IDisposable Suspension);
}

/// <summary>
/// One sheet: the wash over the whole window and the card centred on it. The
/// card is as wide and as tall as the dialog's window would have been, and
/// gives way to the window when the window is smaller; a dialog that sized
/// its height to its content still does.
/// </summary>
internal sealed class SheetLayout : Panel
{
    /// <summary>The room a sheet leaves between itself and the window's edges.</summary>
    internal const double WindowMargin = 24;

    private readonly Size _requested;
    private readonly Size _minimum;
    private readonly bool _sizesWidth;
    private readonly bool _sizesHeight;

    public SheetLayout(Border scrim, SheetCard card)
    {
        Scrim = scrim;
        Card = card;
        Children.Add(scrim);
        Children.Add(card);
        var dialog = card.Dialog;
        _sizesWidth = dialog.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight
            || !double.IsFinite(dialog.Width);
        _sizesHeight = dialog.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight
            || !double.IsFinite(dialog.Height);
        _requested = new Size(
            double.IsFinite(dialog.Width) ? dialog.Width : 0,
            double.IsFinite(dialog.Height) ? dialog.Height : 0);
        _minimum = new Size(
            double.IsFinite(dialog.MinWidth) ? dialog.MinWidth : 0,
            double.IsFinite(dialog.MinHeight) ? dialog.MinHeight : 0);
    }

    public Border Scrim { get; }

    public SheetCard Card { get; }

    protected override Size MeasureOverride(Size availableSize)
    {
        Scrim.Measure(availableSize);
        var room = Room(availableSize);
        var width = _sizesWidth ? room.Width : Math.Min(Math.Max(_requested.Width, _minimum.Width), room.Width);
        var height = _sizesHeight ? room.Height : Math.Min(Math.Max(_requested.Height, _minimum.Height), room.Height);
        Card.Measure(new Size(width, height));
        return new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width : Card.DesiredSize.Width,
            double.IsFinite(availableSize.Height) ? availableSize.Height : Card.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Scrim.Arrange(new Rect(finalSize));
        var room = Room(finalSize);
        var width = _sizesWidth
            ? Math.Min(Math.Max(Card.DesiredSize.Width, _minimum.Width), room.Width)
            : Math.Min(Math.Max(_requested.Width, _minimum.Width), room.Width);
        var height = _sizesHeight
            ? Math.Min(Math.Max(Card.DesiredSize.Height, _minimum.Height), room.Height)
            : Math.Min(Math.Max(_requested.Height, _minimum.Height), room.Height);
        Card.Arrange(new Rect(
            Math.Round((finalSize.Width - width) / 2),
            Math.Round((finalSize.Height - height) / 2),
            width,
            height));
        return finalSize;
    }

    private static Size Room(Size size) => new(
        Math.Max(0, size.Width - (2 * WindowMargin)),
        Math.Max(0, size.Height - (2 * WindowMargin)));
}

/// <summary>The card a sheet's dialog stands on. Tab stays inside it.</summary>
internal sealed class SheetCard : Border
{
    public SheetCard(DialogWindow dialog, Control content)
    {
        Dialog = dialog;
        Child = content;
        Classes.Add("Sheet");
        ClipToBounds = true;
        Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        Bind(DataContextProperty, dialog.GetObservable(DataContextProperty));
    }

    public DialogWindow Dialog { get; }

    /// <summary>Styled as a <see cref="Border"/>, which is what the theme's sheet rule names.</summary>
    protected override Type StyleKeyOverride => typeof(Border);
}
