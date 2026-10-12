using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// One control with a name above it and, where it needs one, a sentence below.
///
/// This is the settings shape that does not fit a row: a colour, a font, a
/// platform profile — things that sit side by side in a grid rather than stacked
/// down a page. It was hand-built seventeen times, each restating the label style
/// and the hint's font size, which is how two fields in the same grid ended up
/// with different gaps between label and control.
///
/// It is deliberately separate from <see cref="SettingRow"/>. A row puts the name
/// beside the control and belongs in a list of settings; a field puts the name
/// above it and belongs in a grid of them. Merging the two behind a flag would
/// make every use site declare which of the two it meant.
///
/// The label names the control for assistive technology as well as for the eye,
/// and the hint is its help text. A field can also carry an <see cref="Error"/>:
/// the sentence that says what is wrong with the value and how to fix it, drawn
/// directly under the control it is about — rather than in a log window after
/// the form has closed and taken the typed value with it.
/// </summary>
[TemplatePart("PART_Label", typeof(TextBlock))]
[PseudoClasses(":error", ":noted")]
internal sealed class LabeledField : ContentControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledField, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<LabeledField, string?>(nameof(Hint));

    /// <summary>
    /// What is wrong with the value, in a sentence that says how to fix it. Null
    /// or empty when the value is fine.
    /// </summary>
    public static readonly StyledProperty<string?> ErrorProperty =
        AvaloniaProperty.Register<LabeledField, string?>(nameof(Error));

    public static readonly DirectProperty<LabeledField, bool> HasErrorProperty =
        AvaloniaProperty.RegisterDirect<LabeledField, bool>(
            nameof(HasError),
            field => field.HasError);

    private readonly FieldLabelLink _link = new();
    private TextBlock? _label;
    private bool _hasError;

    static LabeledField()
    {
        HintProperty.Changed.AddClassHandler<LabeledField>((field, _) => field.UpdateAutomation());
        HintProperty.Changed.AddClassHandler<LabeledField>((field, _) => field.UpdateNoted());
        ErrorProperty.Changed.AddClassHandler<LabeledField>((field, _) => field.UpdateError());
        ContentProperty.Changed.AddClassHandler<LabeledField>((field, _) => field.UpdateAutomation());
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public string? Error
    {
        get => GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    /// <summary>Whether the field is showing an <see cref="Error"/>.</summary>
    public bool HasError
    {
        get => _hasError;
        private set => SetAndRaise(HasErrorProperty, ref _hasError, value);
    }

    /// <summary>The control the label names, once the field has found it.</summary>
    internal Control? LabelledControl => _link.Target;

    protected override Type StyleKeyOverride => typeof(LabeledField);

    /// <summary>
    /// Brings the first field under <paramref name="root"/> that is showing an
    /// error into view and puts the keyboard in its control, so a form that
    /// refused to submit lands the person on what to fix. Returns whether there
    /// was one.
    /// </summary>
    public static bool FocusFirstError(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var field = root
            .GetVisualDescendants()
            .OfType<LabeledField>()
            .FirstOrDefault(candidate => candidate.HasError && candidate.IsEffectivelyVisible);
        if (field is null)
        {
            return false;
        }

        field.BringIntoView();
        (field.LabelledControl ?? FieldLabelLink.FindTarget(field.Content))?.Focus(NavigationMethod.Tab);
        return true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _label = e.NameScope.Find<TextBlock>("PART_Label");
        UpdateAutomation();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateAutomation();
    }

    private void UpdateError()
    {
        HasError = !string.IsNullOrWhiteSpace(Error);
        PseudoClasses.Set(":error", HasError);
        UpdateNoted();
        UpdateAutomation();
    }

    /// <summary>
    /// Whether a sentence shows under the control, an error or a hint: a form
    /// row keeps no room for one it has not got.
    /// </summary>
    private void UpdateNoted() =>
        PseudoClasses.Set(":noted", HasError || !string.IsNullOrWhiteSpace(Hint));

    /// <summary>
    /// The error, while there is one, is what a screen reader hears after the
    /// name: it is the sentence that matters until it is fixed.
    /// </summary>
    private void UpdateAutomation() =>
        _link.Update(FieldLabelLink.FindTarget(Content), _label, HasError ? Error : Hint);
}
