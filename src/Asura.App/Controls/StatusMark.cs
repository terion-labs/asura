using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Controls;

/// <summary>
/// A panel header's status dot that can also say its state without colour.
///
/// Three headers drew a green dot whatever was going on behind it, and a
/// colour alone is the one signal some people cannot read. The mark takes its
/// tone from the panel's real state, names it for the tooltip and VoiceOver,
/// and — when the system asks to differentiate without colour — swaps the dot
/// for a glyph whose shape says the same thing: a check, a warning triangle, a
/// cross, or an empty ring.
/// </summary>
internal sealed class StatusMark : Panel
{
    public static readonly StyledProperty<StatusTone> ToneProperty =
        AvaloniaProperty.Register<StatusMark, StatusTone>(nameof(Tone));

    /// <summary>What the state is, in words: the tooltip and accessible name.</summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<StatusMark, string?>(nameof(Description));

    /// <summary>
    /// Whether the glyph is shown in place of the dot. Follows the
    /// <c>ShellDifferentiateWithoutColor</c> resource unless set.
    /// </summary>
    public static readonly StyledProperty<bool> ShowsGlyphProperty =
        AvaloniaProperty.Register<StatusMark, bool>(nameof(ShowsGlyph));

    private const string DifferentiateWithoutColorKey = "ShellDifferentiateWithoutColor";
    private readonly Border _dot;
    private readonly SymbolIcon _glyph;

    public StatusMark()
    {
        VerticalAlignment = VerticalAlignment.Center;
        _dot = new Border { Classes = { "StatusDot" } };
        _glyph = new SymbolIcon
        {
            Classes = { "StatusMarkGlyph" },
            IconVariant = IconVariant.Filled,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        _glyph.Bind(
            SymbolIcon.FontSizeProperty,
            _glyph.GetResourceObservable("ShellIconSizeXs"));
        Children.Add(_dot);
        Children.Add(_glyph);
        Bind(
            ShowsGlyphProperty,
            this.GetResourceObservable(
                DifferentiateWithoutColorKey,
                value => value is true),
            BindingPriority.Style);
        ApplyTone(Tone);
    }

    public StatusTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool ShowsGlyph
    {
        get => GetValue(ShowsGlyphProperty);
        set => SetValue(ShowsGlyphProperty, value);
    }

    internal SymbolIcon Glyph => _glyph;

    internal Border Dot => _dot;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ToneProperty)
        {
            ApplyTone(change.GetNewValue<StatusTone>());
        }
        else if (change.Property == DescriptionProperty)
        {
            var description = change.GetNewValue<string?>();
            ToolTip.SetTip(this, description);
            AutomationProperties.SetName(this, description);
        }
        else if (change.Property == ShowsGlyphProperty)
        {
            var showsGlyph = change.GetNewValue<bool>();
            _glyph.IsVisible = showsGlyph;
            _dot.IsVisible = !showsGlyph;
        }
    }

    private void ApplyTone(StatusTone tone)
    {
        StatusToneBrush.SetBackground(_dot, tone);
        _glyph.Symbol = StatusToneGlyph.For(tone);
        // An empty ring says "nothing to report" by its shape; filled, it
        // would read as one more dot.
        _glyph.IconVariant = tone == StatusTone.Neutral
            ? IconVariant.Regular
            : IconVariant.Filled;
        _glyph.Bind(
            SymbolIcon.ForegroundProperty,
            _glyph.GetResourceObservable(StatusToneBrush.ResourceKey(tone)));
    }
}
