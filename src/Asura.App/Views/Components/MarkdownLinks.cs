using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// One link policy for every way a Markdown preview draws text: web and mail
/// links open on a click under a pointing hand, every link offers Open Link
/// and Copy Link on a secondary click, and anything else stays copy-only.
/// </summary>
internal static class MarkdownLinks
{
    /// <summary>The target a link inline points at.</summary>
    internal static readonly AttachedProperty<string?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Inline, string?>("Target", typeof(MarkdownLinks));

    private static readonly Cursor LinkCursor = new(StandardCursorType.Hand);
    private static readonly AttachedProperty<string?> PressedProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, string?>("Pressed", typeof(MarkdownLinks));

    internal static string? GetTarget(Inline inline) => inline.GetValue(TargetProperty);

    internal static void SetTarget(Inline inline, string? target) => inline.SetValue(TargetProperty, target);

    /// <summary>
    /// A link's text in words and the spaces between them, so each can be
    /// drawn on its own: the words underlined, the spaces not.
    /// </summary>
    internal static IEnumerable<string> SplitAtWhitespace(string text)
    {
        var start = 0;
        for (var index = 1; index <= text.Length; index++)
        {
            if (index == text.Length
                || char.IsWhiteSpace(text[index]) != char.IsWhiteSpace(text[index - 1]))
            {
                yield return text[start..index];
                start = index;
            }
        }
    }

    /// <summary>
    /// The schemes a click hands to the system: a web page or a new mail.
    /// Anything else in a document — a file path, a custom scheme — could
    /// start something on this machine, so it is only ever copied.
    /// </summary>
    internal static bool OpensOnClick(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.Ordinal));

    internal static void Open(Visual owner, string target)
    {
        if (!OpensOnClick(target)
            || TopLevel.GetTopLevel(owner)?.Launcher is not { } launcher)
        {
            return;
        }

        _ = launcher.LaunchUriAsync(new Uri(target));
    }

    internal static async Task CopyAsync(Visual owner, string target)
    {
        try
        {
            if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(target);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // As with a copied selection: the clipboard can go away during
            // shutdown, and there is nothing to undo.
        }
    }

    /// <summary>Open Link for what a click would open, then Copy Link.</summary>
    internal static IReadOnlyList<MenuItem> MenuItems(Visual owner, string target)
    {
        var items = new List<MenuItem>();
        if (OpensOnClick(target))
        {
            var open = new MenuItem { Header = "Open Link" };
            open.Click += (_, _) => Open(owner, target);
            items.Add(open);
        }

        var copy = new MenuItem { Header = "Copy Link" };
        copy.Click += (_, _) => _ = CopyAsync(owner, target);
        items.Add(copy);
        return items;
    }

    internal static void ShowMenu(Control owner, string target)
    {
        var menu = new ContextMenu { ItemsSource = MenuItems(owner, target) };
        menu.Open(owner);
    }

    /// <summary>
    /// Makes the link inlines of a selectable text block act like links: a
    /// click opens one, a drag that starts on one still selects text, and the
    /// pointer turns into a hand over what a click opens.
    /// </summary>
    internal static void Attach(SelectableTextBlock text)
    {
        text.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        text.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        text.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        text.AddHandler(InputElement.PointerExitedEvent, OnExited, RoutingStrategies.Direct, handledEventsToo: true);
        text.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <summary>The link under a point in the text block, or null.</summary>
    internal static string? TargetAt(SelectableTextBlock text, Point point)
    {
        if (text.Inlines is not { Count: > 0 } inlines)
        {
            return null;
        }

        var hit = text.TextLayout.HitTestPoint(
            new Point(point.X - text.Padding.Left, point.Y - text.Padding.Top));
        if (!hit.IsInside)
        {
            return null;
        }

        var offset = 0;
        return Find(inlines, hit.TextPosition, ref offset, enclosing: null, out var target)
            ? target
            : null;
    }

    /// <summary>
    /// Walks the inlines in layout order. A run takes as many characters as
    /// its text; an embedded control, such as a formula, takes one.
    /// </summary>
    private static bool Find(
        InlineCollection inlines,
        int position,
        ref int offset,
        string? enclosing,
        out string? target)
    {
        foreach (var inline in inlines)
        {
            var link = GetTarget(inline) ?? enclosing;
            if (inline is Span span)
            {
                if (Find(span.Inlines, position, ref offset, link, out target))
                {
                    return true;
                }

                continue;
            }

            offset += inline is Run run ? run.Text?.Length ?? 0 : 1;
            if (position < offset)
            {
                target = link;
                return true;
            }
        }

        target = null;
        return false;
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is SelectableTextBlock text)
        {
            text.SetValue(
                PressedProperty,
                e.GetCurrentPoint(text).Properties.IsLeftButtonPressed
                    ? TargetAt(text, e.GetPosition(text))
                    : null);
        }
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not SelectableTextBlock text)
        {
            return;
        }

        var pressed = text.GetValue(PressedProperty);
        text.ClearValue(PressedProperty);
        // A click on a link opens it; a drag that starts on one selects
        // text, as it does in any Mac text view.
        if (pressed is not null
            && text.SelectionStart == text.SelectionEnd
            && string.Equals(TargetAt(text, e.GetPosition(text)), pressed, StringComparison.Ordinal))
        {
            Open(text, pressed);
        }
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not SelectableTextBlock text)
        {
            return;
        }

        // A pointing hand over what a click opens; the text beam over
        // everything else, links that only copy included.
        if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed
            && TargetAt(text, e.GetPosition(text)) is { } target
            && OpensOnClick(target))
        {
            text.Cursor = LinkCursor;
        }
        else
        {
            text.ClearValue(InputElement.CursorProperty);
        }
    }

    private static void OnExited(object? sender, PointerEventArgs e)
    {
        if (sender is SelectableTextBlock text)
        {
            text.ClearValue(InputElement.CursorProperty);
        }
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not SelectableTextBlock text
            || !e.TryGetPosition(text, out var point)
            || TargetAt(text, point) is not { } target)
        {
            return;
        }

        ShowMenu(text, target);
        e.Handled = true;
    }
}
