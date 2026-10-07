using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// Puts a form's validation message under the field it is about.
///
/// The editors reported every failure the same way: a line in an error list
/// at the bottom of a long scrolling dialog, or — in the Git forms — a log
/// window that opened after the form had closed and taken the typed values
/// with it. A field now says which value it holds (<see cref="ForProperty"/>,
/// the editor view model's property name), a failure says which field it is
/// about (<see cref="InvalidField"/>), and the dialog stays open with the
/// message under that field, scrolled into view and focused. A failure that
/// names no field still goes to the form's error list.
///
/// The message clears itself as soon as the person changes the value it is
/// about; it is not left standing over a value that may now be right.
/// </summary>
internal static class FieldValidation
{
    /// <summary>The view-model property the field's control edits.</summary>
    public static readonly AttachedProperty<string?> ForProperty =
        AvaloniaProperty.RegisterAttached<LabeledField, string?>("For", typeof(FieldValidation));

    private static readonly AttachedProperty<bool> IsWatchedProperty =
        AvaloniaProperty.RegisterAttached<LabeledField, bool>("IsWatched", typeof(FieldValidation));

    public static string? GetFor(LabeledField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.GetValue(ForProperty);
    }

    public static void SetFor(LabeledField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        field.SetValue(ForProperty, value);
    }

    /// <summary>
    /// Shows <paramref name="exception"/> under the field it names, focuses
    /// that field, and returns true; or returns false when it names no field
    /// on screen, leaving the caller to report it.
    /// </summary>
    public static bool Present(Visual root, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(exception);
        Clear(root);
        return InvalidField.Of(exception) is { } field && Show(root, field, exception.Message);
    }

    /// <summary>
    /// Shows <paramref name="message"/> under the visible field for
    /// <paramref name="field"/> and focuses it. Returns whether there was one.
    /// </summary>
    public static bool Show(Visual root, string field, string message)
    {
        ArgumentNullException.ThrowIfNull(root);
        var target = Fields(root).FirstOrDefault(candidate =>
            candidate.IsEffectivelyVisible
            && string.Equals(GetFor(candidate), field, StringComparison.Ordinal));
        if (target is null)
        {
            return false;
        }

        Watch(target);
        target.Error = message;
        LabeledField.FocusFirstError(root);
        return true;
    }

    /// <summary>Clears every field message under <paramref name="root"/>.</summary>
    public static void Clear(Visual root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var field in Fields(root))
        {
            field.Error = null;
        }
    }

    private static IEnumerable<LabeledField> Fields(Visual root) =>
        root.GetVisualDescendants().OfType<LabeledField>();

    /// <summary>
    /// Clears the field's message the first time its value changes after the
    /// message went up. One subscription per field, for the field's lifetime.
    /// </summary>
    private static void Watch(LabeledField field)
    {
        if (field.GetValue(IsWatchedProperty)
            || (field.LabelledControl ?? FieldLabelLink.FindTarget(field.Content)) is not { } control)
        {
            return;
        }

        field.SetValue(IsWatchedProperty, true);
        control.PropertyChanged += (_, change) =>
        {
            if (field.HasError && IsValueProperty(change.Property))
            {
                field.Error = null;
            }
        };
    }

    private static bool IsValueProperty(AvaloniaProperty property) =>
        property == TextBox.TextProperty
        || property == SelectingItemsControl.SelectedItemProperty
        || property == NumericUpDown.ValueProperty
        || property == ToggleButton.IsCheckedProperty;
}
