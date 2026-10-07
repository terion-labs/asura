using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Asura.App.Controls;

/// <summary>
/// One setting: what it is, what it does, and the control that changes it.
///
/// Every settings page built this shape by hand — a two-column grid with a fixed
/// right-hand width, a semibold label, a muted description under it, and a
/// hairline border below to separate it from the next one. The widths disagreed
/// (300, 280, 212, 240), the insets disagreed (16,12 and 16,10 and 12), and the
/// description font was restated at every single one. A row that is a component
/// can only disagree with itself.
///
/// The label is the control's accessible name and the description its help
/// text: the row links them, so no use site has to name its control twice.
/// </summary>
[TemplatePart("PART_Row", typeof(Grid))]
[TemplatePart("PART_Label", typeof(TextBlock))]
internal sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SettingRow, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>
    /// How wide the control column is.
    ///
    /// Set on the <see cref="SettingsGroup"/> rather than the row, so every row in
    /// a group lines up. Avalonia has no shared size group, and rows that each
    /// chose their own width are why no two settings pages aligned.
    /// </summary>
    public static readonly StyledProperty<GridLength> ControlWidthProperty =
        AvaloniaProperty.Register<SettingRow, GridLength>(
            nameof(ControlWidth),
            new GridLength(1, GridUnitType.Auto));

    /// <summary>
    /// Whether this row is the last in its group, and so draws no separator.
    /// </summary>
    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsLast));

    /// <summary>
    /// Whether the control sits under the label rather than beside it.
    ///
    /// A row's control column fits a switch, a picker, or a field. A setting
    /// whose control is a list — the folders an isolate can see, the
    /// connections a workspace offers — does not fit a column, and squeezing
    /// one in is how a list of editable rows ended up 470 pixels wide with
    /// its own footnotes stacked beneath it. Stacked, the row keeps its name
    /// and its description and gives the control the full width below them.
    /// </summary>
    public static readonly StyledProperty<bool> IsStackedProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsStacked));

    static SettingRow()
    {
        DescriptionProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateStateClasses());
        DescriptionProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateAutomation());
        ContentProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateAutomation());
        IsLastProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateStateClasses());
        ControlWidthProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.ApplyControlWidth());
        IsStackedProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateStateClasses());
    }

    public SettingRow() => UpdateStateClasses();

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public GridLength ControlWidth
    {
        get => GetValue(ControlWidthProperty);
        set => SetValue(ControlWidthProperty, value);
    }

    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    public bool IsStacked
    {
        get => GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SettingRow);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _row = e.NameScope.Find<Grid>("PART_Row");
        _label = e.NameScope.Find<TextBlock>("PART_Label");
        ApplyControlWidth();
        UpdateAutomation();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateAutomation();
    }

    private readonly FieldLabelLink _link = new();
    private Grid? _row;
    private TextBlock? _label;

    private void UpdateAutomation() =>
        _link.Update(FieldLabelLink.FindTarget(Content), _label, Description);

    private void ApplyControlWidth()
    {
        if (_row is { ColumnDefinitions.Count: 2 })
        {
            _row.ColumnDefinitions[1].Width = ControlWidth;
        }
    }

    private void UpdateStateClasses()
    {
        PseudoClasses.Set(":described", !string.IsNullOrWhiteSpace(Description));
        PseudoClasses.Set(":last", IsLast);
        PseudoClasses.Set(":stacked", IsStacked);
    }
}
