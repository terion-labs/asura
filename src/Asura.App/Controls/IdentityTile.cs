using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using FluentIcons.Common;

namespace Asura.App.Controls;

/// <summary>How large a mark is drawn.</summary>
internal enum IdentityTileSize
{
    /// <summary>Inside a row: a list, a tab strip.</summary>
    Small,

    /// <summary>Beside a name: a rail entry, a card.</summary>
    Medium,

    /// <summary>The subject of the page.</summary>
    Large,
}

/// <summary>
/// The mark a workspace — or anything else with a colour and an icon — is
/// recognised by.
///
/// It was written by hand at every size it appears in: 32 in the settings list,
/// 34 in the editor's rail, 56 in its header, each with its own corner radius
/// and its own guess at what colour an icon should be on a saturated fill. They
/// are one control, because "this thing looks like this" is not a decision a
/// view gets to re-take.
/// </summary>
internal sealed class IdentityTile : TemplatedControl
{
    public static readonly StyledProperty<IBrush?> TintProperty =
        AvaloniaProperty.Register<IdentityTile, IBrush?>(nameof(Tint));

    public static readonly StyledProperty<Symbol> SymbolProperty =
        AvaloniaProperty.Register<IdentityTile, Symbol>(nameof(Symbol), Symbol.Window);

    public static readonly StyledProperty<IdentityTileSize> TileSizeProperty =
        AvaloniaProperty.Register<IdentityTile, IdentityTileSize>(
            nameof(TileSize),
            IdentityTileSize.Medium);

    /// <summary>
    /// What the glyph is drawn in: the ink measured against the tile's own
    /// fill, or the foreground a view set on the tile.
    /// </summary>
    public static readonly DirectProperty<IdentityTile, IBrush?> InkProperty =
        AvaloniaProperty.RegisterDirect<IdentityTile, IBrush?>(
            nameof(Ink),
            tile => tile.Ink);

    private IBrush? _ink;
    private bool _hasOwnForeground;

    static IdentityTile() =>
        TileSizeProperty.Changed.AddClassHandler<IdentityTile>(
            (tile, _) => tile.UpdateStateClasses());

    public IdentityTile()
    {
        UpdateStateClasses();
        RefreshInk();
    }

    /// <summary>
    /// The fill. A string hex binds here as it does to any brush property, so a
    /// view model may publish the colour it stores rather than a brush it would
    /// have to build.
    /// </summary>
    public IBrush? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public Symbol Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public IdentityTileSize TileSize
    {
        get => GetValue(TileSizeProperty);
        set => SetValue(TileSizeProperty, value);
    }

    public IBrush? Ink
    {
        get => _ink;
        private set => SetAndRaise(InkProperty, ref _ink, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ForegroundProperty)
        {
            // A foreground the view wrote on the tile is the view's decision,
            // usually a neutral tile with body-coloured ink. One that came
            // from the theme or the page is only a fallback.
            _hasOwnForeground = change.Priority <= BindingPriority.LocalValue
                && change.NewValue is not null;
            RefreshInk();
        }
        else if (change.Property == TintProperty)
        {
            RefreshInk();
        }
    }

    /// <summary>
    /// The tile is filled with a colour the theme knows nothing about — a
    /// workspace's own — so only the tile can say whether ink or paper reads on
    /// it. The accent pair it used to take was measured against the shell's
    /// accent: a dark navy workspace got black ink at about 1.5:1.
    /// </summary>
    private void RefreshInk() =>
        Ink = !_hasOwnForeground && Tint is ISolidColorBrush { Color.A: 0xFF } solid
            ? new SolidColorBrush(WorkspaceRailTile.Ink(solid.Color))
            : Foreground;

    private void UpdateStateClasses()
    {
        foreach (var size in Enum.GetValues<IdentityTileSize>())
        {
            PseudoClasses.Set($":{size.ToString().ToLowerInvariant()}", size == TileSize);
        }
    }
}
