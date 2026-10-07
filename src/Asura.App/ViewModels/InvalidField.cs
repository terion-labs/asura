namespace Asura.App.ViewModels;

/// <summary>
/// Names the field a validation failure is about, so the editor can draw the
/// message under that field instead of in a list at the bottom of the form.
///
/// The field travels in <see cref="Exception.Data"/> rather than as a
/// parameter name: the exception stays an ordinary
/// <see cref="ArgumentException"/>, and its message stays the sentence the
/// person reads, without the "(Parameter …)" suffix a parameter name appends.
/// The field is the editor view model's property name, which is what a form's
/// <c>FieldValidation.For</c> names.
/// </summary>
internal static class InvalidField
{
    private const string FieldKey = "Asura.InvalidField";

    public static ArgumentException Exception(string field, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        var exception = new ArgumentException(message);
        exception.Data[FieldKey] = field;
        return exception;
    }

    /// <summary>The field <paramref name="exception"/> is about, if it names one.</summary>
    public static string? Of(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[FieldKey] as string;
    }
}
