using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
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
    /// A reading of what the control is set to, shown at the trailing end of
    /// a stacked row's label line: a slider's value, which a slider does not
    /// draw itself. A screen reader hears the value from the control.
    /// </summary>
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Value));

    /// <summary>
    /// How wide the control column is at most.
    ///
    /// Set on the <see cref="SettingsGroup"/> rather than the row, so every row in
    /// a group lines up. Avalonia has no shared size group, and rows that each
    /// chose their own width are why no two settings pages aligned.
    ///
    /// It is a ceiling, not a claim: the label keeps
    /// <see cref="LabelMinimumWidth"/> and the control takes what is left up to
    /// this width, and when that is too little for a control the control moves
    /// under the label. A fixed column starved the label at narrow window
    /// widths until a two-word setting wrapped one word to a line. A row whose
    /// control is a switch ignores it and takes the switch's own width.
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

    /// <summary>
    /// Whether the control leads and the label follows it: a checkbox whose
    /// title is the setting's name, with the description under the title.
    /// </summary>
    public static readonly StyledProperty<bool> IsLeadingProperty =
        AvaloniaProperty.Register<SettingRow, bool>(nameof(IsLeading));

    /// <summary>The narrowest the label column is allowed to become.</summary>
    internal const double LabelMinimumWidth = 240;

    /// <summary>
    /// The narrowest a control is given beside its label before it moves under
    /// it instead.
    /// </summary>
    internal const double ControlMinimumWidth = 140;

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
        IsLeadingProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateStateClasses());
        IsLeadingProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateAutomation());
        LabelProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateLeadingTitle());
        ContentProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.UpdateLeadingTitle());
        ContentProperty.Changed.AddClassHandler<SettingRow>(
            (row, _) => row.ApplyControlWidth());
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

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
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

    public bool IsLeading
    {
        get => GetValue(IsLeadingProperty);
        set => SetValue(IsLeadingProperty, value);
    }

    /// <summary>Whether the control has moved under the label for want of width.</summary>
    internal bool IsWrapped { get; private set; }

    protected override Type StyleKeyOverride => typeof(SettingRow);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _row = e.NameScope.Find<Grid>("PART_Row");
        _label = e.NameScope.Find<TextBlock>("PART_Label");
        _control = e.NameScope.Find<ContentPresenter>("PART_Control");
        IsWrapped = false;
        ApplyControlWidth();
        UpdateLeadingTitle();
        UpdateAutomation();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        FitControl(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateAutomation();
    }

    private readonly FieldLabelLink _link = new();
    private Grid? _row;
    private TextBlock? _label;
    private ContentPresenter? _control;
    private object? _leadingTitle;

    /// <summary>
    /// A leading row's checkbox is named by its own title, so the row links
    /// only its description, as the box's help text. Any other row names its
    /// control by the label it draws.
    /// </summary>
    private void UpdateAutomation()
    {
        if (IsLeading && Content is CheckBox box)
        {
            _link.Update(box, null, Description);
            return;
        }

        _link.Update(FieldLabelLink.FindTarget(Content), _label, Description);
    }

    /// <summary>
    /// The control column this row actually reserves. A switch is one fixed
    /// size, so it takes its own width rather than the group's: reserving a
    /// 300-point column for a 38-point switch squeezed the label and its
    /// description into half the row, wrapping a three-word setting onto four
    /// lines beside an empty half, and at a large text size moved the switch
    /// under its label for want of room it never needed.
    /// </summary>
    /// <remarks>
    /// A pop-up button is the same: in a grouped form it is its value and a
    /// pair of chevrons hugging the trailing edge, as wide as what it shows,
    /// so a column reserved for its widest choice only took the label's room.
    /// </remarks>
    private GridLength ControlColumnWidth =>
        Content is ToggleSwitch or ComboBox ? GridLength.Auto : ControlWidth;

    private void ApplyControlWidth()
    {
        if (_row is { ColumnDefinitions.Count: 2 })
        {
            _row.ColumnDefinitions[1].Width = ControlColumnWidth;
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Gives the control what the row can spare after the label's minimum, up
    /// to the group's width, or moves it under the label when that is less
    /// than a control can use.
    /// </summary>
    private void FitControl(double width)
    {
        if (_row is not { ColumnDefinitions.Count: 2, RowDefinitions.Count: 2 } row || _control is null)
        {
            return;
        }

        var labelInset = _label?.Parent is Control labelColumn
            ? labelColumn.Margin.Left + labelColumn.Margin.Right
            : 0;
        var spare = double.IsFinite(width)
            ? width - Padding.Left - Padding.Right - row.ColumnSpacing - labelInset - LabelMinimumWidth
            : double.PositiveInfinity;
        var columnWidth = ControlColumnWidth;
        double wanted;
        if (columnWidth.IsAbsolute)
        {
            wanted = columnWidth.Value;
        }
        else
        {
            _control.Measure(Size.Infinity);
            wanted = _control.DesiredSize.Width;
        }

        var wrap = spare < Math.Min(wanted, ControlMinimumWidth);
        IsWrapped = wrap;
        Grid.SetRow(_control, wrap ? 1 : 0);
        Grid.SetColumn(_control, wrap ? 0 : 1);
        Grid.SetColumnSpan(_control, wrap ? 2 : 1);
        row.RowSpacing = wrap && this.TryFindResource("ShellSpaceSm", out var gap) && gap is double space
            ? space
            : 0;
        row.ColumnDefinitions[1].Width = wrap
            ? new GridLength(0)
            : columnWidth.IsAbsolute
                ? new GridLength(Math.Min(columnWidth.Value, spare))
                : columnWidth;
        PseudoClasses.Set(":wrapped", wrap);
    }

    /// <summary>
    /// A leading row's checkbox takes the setting's name as its title, so the
    /// words are part of what a click toggles and what a screen reader names.
    /// A checkbox that brought its own content keeps it.
    /// </summary>
    private void UpdateLeadingTitle()
    {
        if (Content is not CheckBox box)
        {
            return;
        }

        if (IsLeading && (box.Content is null || ReferenceEquals(box.Content, _leadingTitle)))
        {
            _leadingTitle = Label;
            box.Content = Label;
        }
        else if (!IsLeading && ReferenceEquals(box.Content, _leadingTitle) && _leadingTitle is not null)
        {
            box.Content = null;
            _leadingTitle = null;
        }
    }

    private void UpdateStateClasses()
    {
        PseudoClasses.Set(":described", !string.IsNullOrWhiteSpace(Description));
        PseudoClasses.Set(":last", IsLast);
        PseudoClasses.Set(":stacked", IsStacked);
        PseudoClasses.Set(":leading", IsLeading);
        UpdateLeadingTitle();
    }
}
