using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Asura.App.Controls;

/// <summary>
/// Ties the name a form component draws to the control it names, so a
/// screen reader hears the same words a sighted user reads.
///
/// <see cref="LabeledField"/>, <see cref="SettingRow"/> and
/// <see cref="ToggleField"/> each drew a label beside or above their control
/// and linked it to nothing: about a quarter of the application's fields were
/// announced as "edit text" with no name at all. The component knows its label
/// and its control, so it states the link once, here, for every use.
///
/// A name or label the view set on the control itself always wins: the link
/// fills only what was left empty, and remembers what it filled so a later
/// change replaces its own words and never the view's.
/// </summary>
internal sealed class FieldLabelLink
{
    private static readonly AttachedProperty<string?> LinkedHelpTextProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "LinkedHelpText",
            typeof(FieldLabelLink));

    private Control? _target;
    private Control? _label;

    /// <summary>The control the label currently names, if there is one.</summary>
    public Control? Target => _target;

    /// <summary>
    /// The control a component's content offers to be named: the content itself
    /// when it takes a value, or else the first control inside it that does. A
    /// button is never chosen — it already says what it does.
    /// </summary>
    public static Control? FindTarget(object? content) => content switch
    {
        Control control when TakesValue(control) => control,
        Control control => control.GetLogicalDescendants().OfType<Control>().FirstOrDefault(TakesValue),
        _ => null,
    };

    /// <summary>
    /// Names <paramref name="target"/> by <paramref name="label"/> and describes
    /// it with <paramref name="helpText"/>, undoing the previous link when the
    /// target has changed.
    /// </summary>
    public void Update(Control? target, Control? label, string? helpText)
    {
        if (!ReferenceEquals(target, _target) || !ReferenceEquals(label, _label))
        {
            Release();
        }

        _target = target;
        _label = label;
        if (target is null)
        {
            return;
        }

        if (label is not null
            && string.IsNullOrWhiteSpace(AutomationProperties.GetName(target))
            && AutomationProperties.GetLabeledBy(target) is null)
        {
            AutomationProperties.SetLabeledBy(target, label);
        }

        var current = AutomationProperties.GetHelpText(target);
        var linked = target.GetValue(LinkedHelpTextProperty);
        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, linked, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(helpText))
        {
            target.ClearValue(AutomationProperties.HelpTextProperty);
            target.ClearValue(LinkedHelpTextProperty);
            return;
        }

        AutomationProperties.SetHelpText(target, helpText);
        target.SetValue(LinkedHelpTextProperty, helpText);
    }

    private void Release()
    {
        if (_target is not { } previous)
        {
            return;
        }

        if (_label is not null && ReferenceEquals(AutomationProperties.GetLabeledBy(previous), _label))
        {
            previous.ClearValue(AutomationProperties.LabeledByProperty);
        }

        var linked = previous.GetValue(LinkedHelpTextProperty);
        if (linked is not null
            && string.Equals(AutomationProperties.GetHelpText(previous), linked, StringComparison.Ordinal))
        {
            previous.ClearValue(AutomationProperties.HelpTextProperty);
        }

        previous.ClearValue(LinkedHelpTextProperty);
        _target = null;
        _label = null;
    }

    private static bool TakesValue(Control control) => control is
        TextBox
        or ComboBox
        or NumericUpDown
        or ToggleSwitch
        or Slider
        or AutoCompleteBox
        or CalendarDatePicker
        or DatePicker
        or TimePicker
        or ColorPicker;
}
