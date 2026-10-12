using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Asura.App.Controls;

/// <summary>
/// A card of related settings.
///
/// It owns the two things the rows inside it must agree on and cannot decide for
/// themselves: how wide the control column is, and where the separators go. Both
/// were hand-maintained before — every page picked its own column width, and each
/// separator was a one-pixel <c>Border</c> written between rows, which meant a
/// row added in the wrong place left a hairline dangling under the last one.
/// </summary>
[TemplatePart("PART_Rows", typeof(ItemsPresenter))]
internal sealed class SettingsGroup : ItemsControl
{
    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Heading));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Description));

    /// <summary>
    /// The footnote under the plate: a live status, or a sentence qualifying
    /// the settings above it, in the secondary size on the heading's edge, as
    /// System Settings prints a group's explanation. Inside the plate it read
    /// as a setting that had lost its control; a status that comes with an
    /// action is a row of the plate instead.
    /// </summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SettingsGroup, object?>(nameof(Footer));

    /// <summary>
    /// The width every row's control column takes. Auto by default, so a group of
    /// short controls does not reserve a column it has no use for.
    /// </summary>
    public static readonly StyledProperty<GridLength> ControlWidthProperty =
        AvaloniaProperty.Register<SettingsGroup, GridLength>(
            nameof(ControlWidth),
            new GridLength(1, GridUnitType.Auto));

    static SettingsGroup()
    {
        ControlWidthProperty.Changed.AddClassHandler<SettingsGroup>(
            (group, _) => group.ApplyToRows());
        HeadingProperty.Changed.AddClassHandler<SettingsGroup>(
            (group, _) => group.UpdateStateClasses());
        DescriptionProperty.Changed.AddClassHandler<SettingsGroup>(
            (group, _) => group.UpdateStateClasses());
        FooterProperty.Changed.AddClassHandler<SettingsGroup>(
            (group, change) => group.FollowFooter(change.OldValue as Control, change.NewValue as Control));
    }

    public SettingsGroup() => UpdateStateClasses();

    public string? Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public GridLength ControlWidth
    {
        get => GetValue(ControlWidthProperty);
        set => SetValue(ControlWidthProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SettingsGroup);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ApplyToRows();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ApplyToRows();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FollowSiblings(Parent as Panel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        FollowSiblings(null);
    }

    private Panel? _followedPanel;

    /// <summary>
    /// Whether nothing shown stands before this group in its panel, which is
    /// what lets a page open on its first section without a section's gap
    /// above it. Counting children was not enough: a page's hidden opening
    /// line and an error notice that shows only with an error came first on
    /// some pages and not others, and a search hides whole sections.
    /// </summary>
    private void FollowSiblings(Panel? panel)
    {
        if (_followedPanel is { } previous)
        {
            foreach (var sibling in previous.Children)
            {
                sibling.PropertyChanged -= OnSiblingPropertyChanged;
            }
        }

        _followedPanel = panel;
        if (panel is not null)
        {
            foreach (var sibling in panel.Children)
            {
                if (!ReferenceEquals(sibling, this))
                {
                    sibling.PropertyChanged += OnSiblingPropertyChanged;
                }
            }
        }

        UpdateFirstShown();
    }

    private void OnSiblingPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == IsVisibleProperty)
        {
            UpdateFirstShown();
        }
    }

    private void UpdateFirstShown() =>
        PseudoClasses.Set(
            ":first-shown",
            _followedPanel is { } panel
                && panel.Children.TakeWhile(sibling => !ReferenceEquals(sibling, this)).All(sibling => !sibling.IsVisible));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemCountProperty)
        {
            ApplyToRows();
        }
    }

    /// <summary>
    /// Pushes the group's column width to every row, and tells the row that
    /// closes the plate it is last so it draws no separator. Doing it here is
    /// what keeps a row from ever having to know its own position. A row the
    /// search hides gives up being last, so the row above it closes the plate
    /// instead of leaving a hairline hanging over its bottom edge; a row with
    /// something else under it — a disclosure, a note — keeps its hairline.
    /// </summary>
    private void ApplyToRows()
    {
        var items = Items.OfType<Control>().ToArray();
        var lastShown = items.LastOrDefault(item => item.IsVisible) ?? items.LastOrDefault();
        foreach (var item in items)
        {
            if (item is SettingRow row)
            {
                row.ControlWidth = ControlWidth;
                row.IsLast = ReferenceEquals(row, lastShown);
            }

            item.PropertyChanged -= OnRowPropertyChanged;
            item.PropertyChanged += OnRowPropertyChanged;
        }

        PseudoClasses.Set(":has-rows", Items.Count > 0);
    }

    private void OnRowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == IsVisibleProperty)
        {
            ApplyToRows();
        }
    }

    private void UpdateStateClasses()
    {
        PseudoClasses.Set(":headed", !string.IsNullOrWhiteSpace(Heading));
        PseudoClasses.Set(":described", !string.IsNullOrWhiteSpace(Description));
        PseudoClasses.Set(":footed", Footer is not null and not Control { IsVisible: false });
    }

    /// <summary>
    /// A footer that hides itself — a warning with nothing to warn about —
    /// takes its space under the plate with it.
    /// </summary>
    private void FollowFooter(Control? previous, Control? next)
    {
        previous?.PropertyChanged -= OnFooterPropertyChanged;
        next?.PropertyChanged += OnFooterPropertyChanged;
        UpdateStateClasses();
    }

    private void OnFooterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == IsVisibleProperty)
        {
            UpdateStateClasses();
        }
    }
}
