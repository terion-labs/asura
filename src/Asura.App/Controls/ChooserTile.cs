using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using FluentIcons.Common;

namespace Asura.App.Controls;

/// <summary>
/// One tile in a chooser grid: an icon in its tile, a name, a sentence.
///
/// The launcher and the panel chooser wrote this body out fourteen times
/// between them, and the copies had drifted — tile 38 against 36, icon 19
/// against 18, description text at two sizes. A tile states what it offers;
/// the anatomy is decided here.
///
/// A tile that cannot be used says why in place of its description, and the
/// sentence stays readable while the rest of the tile dims: "Browser" greyed
/// out with nothing else was a dead end.
/// </summary>
internal sealed class ChooserTile : Button
{
    public static readonly StyledProperty<Symbol> IconProperty =
        AvaloniaProperty.Register<ChooserTile, Symbol>(nameof(Icon), Symbol.Apps);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ChooserTile, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ChooserTile, string?>(nameof(Description));

    /// <summary>Why the tile is disabled and what would make it usable.</summary>
    public static readonly StyledProperty<string?> UnavailableReasonProperty =
        AvaloniaProperty.Register<ChooserTile, string?>(nameof(UnavailableReason));

    public static readonly DirectProperty<ChooserTile, string?> EffectiveDescriptionProperty =
        AvaloniaProperty.RegisterDirect<ChooserTile, string?>(
            nameof(EffectiveDescription),
            tile => tile.EffectiveDescription);

    private string? _effectiveDescription;

    static ChooserTile()
    {
        DescriptionProperty.Changed.AddClassHandler<ChooserTile>((tile, _) => tile.Refresh());
        UnavailableReasonProperty.Changed.AddClassHandler<ChooserTile>((tile, _) => tile.Refresh());
        IsEffectivelyEnabledProperty.Changed.AddClassHandler<ChooserTile>((tile, _) => tile.Refresh());
        // A disabled control shows no tooltip unless asked to, and the
        // tooltip is where a pointer finds the reason.
        ToolTip.ShowOnDisabledProperty.OverrideDefaultValue<ChooserTile>(true);
    }

    public Symbol Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? UnavailableReason
    {
        get => GetValue(UnavailableReasonProperty);
        set => SetValue(UnavailableReasonProperty, value);
    }

    /// <summary>The description, or the reason it cannot be used while disabled.</summary>
    public string? EffectiveDescription
    {
        get => _effectiveDescription;
        private set => SetAndRaise(EffectiveDescriptionProperty, ref _effectiveDescription, value);
    }

    protected override Type StyleKeyOverride => typeof(ChooserTile);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ChooserTileAutomationPeer(this);

    private void Refresh()
    {
        var unavailable = !IsEffectivelyEnabled && !string.IsNullOrWhiteSpace(UnavailableReason);
        EffectiveDescription = unavailable ? UnavailableReason : Description;
        ToolTip.SetTip(this, unavailable ? UnavailableReason : null);
    }

    /// <summary>
    /// Named by its title and described by its sentence, so a screen reader
    /// hears "Browser" and, when it is dimmed, why.
    /// </summary>
    private sealed class ChooserTileAutomationPeer(ChooserTile owner) : ButtonAutomationPeer(owner)
    {
        private new ChooserTile Owner => (ChooserTile)base.Owner;

        protected override string? GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrWhiteSpace(name) ? Owner.Title : name;
        }

        protected override string? GetHelpTextCore() =>
            Owner.EffectiveDescription ?? base.GetHelpTextCore();
    }
}
