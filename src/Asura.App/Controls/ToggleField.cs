using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Asura.App.Controls;

/// <summary>
/// A named on/off setting: the label, an optional sentence about it, and the
/// switch. Four editor dialogs carried this as the same three-element grid,
/// each wired to its neighbour by element name — the shape is one control's
/// worth of decisions, so it is one control.
///
/// The switch is the state. The field used to write "On" or "Off" under its
/// label as well, which repeated what the switch already shows and what a
/// screen reader already announces from it. What remains under the label is a
/// sentence about the setting: the same one in both states, or — where the
/// two states mean different things worth saying — one for each.
///
/// The label is the switch's accessible name and the sentence its help text,
/// so the switch is no longer an unnamed toggle in the accessibility tree.
/// </summary>
[TemplatePart("PART_Label", typeof(TextBlock))]
[TemplatePart("PART_Switch", typeof(ToggleSwitch))]
[PseudoClasses(":described")]
internal sealed class ToggleField : TemplatedControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ToggleField, string>(nameof(Label), string.Empty);

    /// <summary>
    /// What the setting does, shown under the label: the explanation a setting
    /// would otherwise hide in a tooltip.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<ToggleField, string?>(nameof(Description));

    /// <summary>What being on means, where that differs from being off.</summary>
    public static readonly StyledProperty<string?> OnLabelProperty =
        AvaloniaProperty.Register<ToggleField, string?>(nameof(OnLabel));

    /// <summary>What being off means, where that differs from being on.</summary>
    public static readonly StyledProperty<string?> OffLabelProperty =
        AvaloniaProperty.Register<ToggleField, string?>(nameof(OffLabel));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ToggleField, bool>(
            nameof(IsChecked),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The sentence shown under the label for the current state.</summary>
    public static readonly DirectProperty<ToggleField, string?> StateDescriptionProperty =
        AvaloniaProperty.RegisterDirect<ToggleField, string?>(
            nameof(StateDescription),
            field => field.StateDescription);

    private readonly FieldLabelLink _link = new();
    private TextBlock? _label;
    private ToggleSwitch? _switch;
    private string? _stateDescription;

    static ToggleField()
    {
        DescriptionProperty.Changed.AddClassHandler<ToggleField>((field, _) => field.Refresh());
        OnLabelProperty.Changed.AddClassHandler<ToggleField>((field, _) => field.Refresh());
        OffLabelProperty.Changed.AddClassHandler<ToggleField>((field, _) => field.Refresh());
        IsCheckedProperty.Changed.AddClassHandler<ToggleField>((field, _) => field.Refresh());
        AutomationProperties.NameProperty.Changed.AddClassHandler<ToggleField>((field, _) => field.Refresh());
    }

    public ToggleField() => Refresh();

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

    public string? OnLabel
    {
        get => GetValue(OnLabelProperty);
        set => SetValue(OnLabelProperty, value);
    }

    public string? OffLabel
    {
        get => GetValue(OffLabelProperty);
        set => SetValue(OffLabelProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public string? StateDescription
    {
        get => _stateDescription;
        private set => SetAndRaise(StateDescriptionProperty, ref _stateDescription, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _label = e.NameScope.Find<TextBlock>("PART_Label");
        _switch = e.NameScope.Find<ToggleSwitch>("PART_Switch");
        Refresh();
    }

    /// <summary>
    /// A name a view gave the field itself is passed to the switch, which is
    /// the element a screen reader actually lands on.
    /// </summary>
    private void Refresh()
    {
        var stateDescription = (IsChecked ? OnLabel : OffLabel) ?? Description;
        StateDescription = string.IsNullOrWhiteSpace(stateDescription) ? null : stateDescription;
        PseudoClasses.Set(":described", StateDescription is not null);
        if (_switch is null)
        {
            return;
        }

        var name = AutomationProperties.GetName(this);
        if (string.IsNullOrWhiteSpace(name))
        {
            _switch.ClearValue(AutomationProperties.NameProperty);
        }
        else
        {
            AutomationProperties.SetName(_switch, name);
        }

        _link.Update(_switch, _label, StateDescription);
    }
}
