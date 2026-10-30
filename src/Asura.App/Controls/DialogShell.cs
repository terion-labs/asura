using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// Which of the shell's two dialog builds this window is.
/// </summary>
internal enum DialogChrome
{
    /// <summary>A focused task: a name prompt, a transfer, a recorder.</summary>
    Utility,

    /// <summary>A definition editor: larger title and an identity subtitle.</summary>
    Editor,
}

/// <summary>
/// A dialog's frame: the header band, the scrolling body, and the footer with
/// its rule and its button cluster.
///
/// Ten dialogs carried this by hand in two dialects — a "TopChrome" band whose
/// subtitle size drifted between files, and an editor grid whose row heights
/// came in three variants and whose close glyph was a literal "×" in three of
/// four. A dialog states its title, its body, and its buttons; the frame is
/// decided here.
///
/// There is no close glyph. Every dialog has a Cancel button in its footer,
/// and a second way out in the corner only competed with it. The header's
/// title is also the window's: the shell writes it to the window it sits in,
/// so the Window menu and VoiceOver name the dialog in exactly the words the
/// header shows, and a bound title keeps the two in step.
/// </summary>
internal sealed class DialogShell : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogShell, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DialogShell, string?>(nameof(Subtitle));

    /// <summary>Extra header state — a status chip beside an editor's title.</summary>
    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<DialogShell, object?>(nameof(HeaderContent));

    /// <summary>The footer's button cluster, right-aligned after the hint.</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<DialogShell, object?>(nameof(Footer));

    /// <summary>
    /// An auxiliary action at the footer's leading edge, apart from Cancel and
    /// the default button: Test beside Save, Duplicate beside Save Screen. A
    /// Mac dialog keeps such a button away from the ones that close it, so a
    /// press meant for one never lands on the other.
    /// </summary>
    public static readonly StyledProperty<object?> FooterLeadingProperty =
        AvaloniaProperty.Register<DialogShell, object?>(nameof(FooterLeading));

    /// <summary>The muted sentence at the footer's left edge.</summary>
    public static readonly StyledProperty<string?> FooterHintProperty =
        AvaloniaProperty.Register<DialogShell, string?>(nameof(FooterHint));

    public static readonly StyledProperty<DialogChrome> ChromeProperty =
        AvaloniaProperty.Register<DialogShell, DialogChrome>(nameof(Chrome));

    /// <summary>
    /// Whether the body scrolls. On for editors; a fixed-size utility dialog
    /// sizes itself to its content instead.
    /// </summary>
    public static readonly StyledProperty<bool> ScrollsProperty =
        AvaloniaProperty.Register<DialogShell, bool>(nameof(Scrolls), true);

    public static readonly DirectProperty<DialogShell, bool> IsFooterVisibleProperty =
        AvaloniaProperty.RegisterDirect<DialogShell, bool>(
            nameof(IsFooterVisible),
            shell => shell.IsFooterVisible);

    private bool _isFooterVisible;

    static DialogShell()
    {
        ChromeProperty.Changed.AddClassHandler<DialogShell>(
            (shell, _) => shell.UpdateChromeClass());
        FooterProperty.Changed.AddClassHandler<DialogShell>(
            (shell, _) => shell.UpdateFooterVisibility());
        FooterHintProperty.Changed.AddClassHandler<DialogShell>(
            (shell, _) => shell.UpdateFooterVisibility());
        FooterLeadingProperty.Changed.AddClassHandler<DialogShell>(
            (shell, _) => shell.UpdateFooterVisibility());
        TitleProperty.Changed.AddClassHandler<DialogShell>(
            (shell, _) => shell.NameWindow());
    }

    public DialogShell()
    {
        UpdateChromeClass();
        UpdateFooterVisibility();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public object? FooterLeading
    {
        get => GetValue(FooterLeadingProperty);
        set => SetValue(FooterLeadingProperty, value);
    }

    public string? FooterHint
    {
        get => GetValue(FooterHintProperty);
        set => SetValue(FooterHintProperty, value);
    }

    public DialogChrome Chrome
    {
        get => GetValue(ChromeProperty);
        set => SetValue(ChromeProperty, value);
    }

    public bool Scrolls
    {
        get => GetValue(ScrollsProperty);
        set => SetValue(ScrollsProperty, value);
    }

    public bool IsFooterVisible
    {
        get => _isFooterVisible;
        private set => SetAndRaise(IsFooterVisibleProperty, ref _isFooterVisible, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Shown as a page, the window's title band names the dialog, so
        // its own header keeps only the line under the title.
        PseudoClasses.Set(":page", this.FindAncestorOfType<SheetCard>() is { IsPage: true });
        NameWindow();
    }

    /// <summary>The window is titled with the header's words, whatever they bind to.</summary>
    private void NameWindow()
    {
        // A sheet names its dialog, not the window it stands in.
        if (!string.IsNullOrWhiteSpace(Title)
            && (SheetHost.DialogFor(this) ?? TopLevel.GetTopLevel(this)) is Window window)
        {
            window.Title = Title;
        }
    }

    private void UpdateChromeClass() =>
        PseudoClasses.Set(":editor", Chrome == DialogChrome.Editor);

    private void UpdateFooterVisibility() =>
        IsFooterVisible = Footer is not null || FooterHint is not null || FooterLeading is not null;
}
