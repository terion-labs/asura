using Asura.App.Controls;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Asura.App.Views;

public sealed record GitWorkflowField(string Key, string Label, string Value = "", bool Required = true,
    bool Multiline = false, IReadOnlyList<string>? Choices = null)
{
    internal const string Checked = "true";
    internal const string Unchecked = "false";

    /// <summary>A yes-or-no option, drawn as a checkbox rather than a Yes/No pop-up.</summary>
    public bool IsCheckBox { get; init; }

    /// <summary>A yes-or-no option. Read it back with <see cref="GitWorkflowDialog.IsChecked"/>.</summary>
    public static GitWorkflowField CheckBox(string key, string label, bool value) =>
        new(key, label, value ? Checked : Unchecked, Required: false) { IsCheckBox = true };
}

/// <summary>The form's button: its verb, and whether pressing it destroys something.</summary>
public sealed record GitWorkflowAction(string Verb, bool IsDestructive = false);

/// <summary>What is wrong with one field's value, said in a way that says how to fix it.</summary>
public sealed record GitWorkflowFieldError(string Key, string Message);

/// <summary>
/// One Git form: what it is called, what it does, its fields, and the verb its
/// button carries. Every form says its own verb — Clone, Save Stash, Compare —
/// because a column of identical "Apply" buttons, force push among them, said
/// nothing about what each one would do.
/// </summary>
public sealed record GitWorkflowForm(
    string Title,
    string Detail,
    GitWorkflowAction Action,
    IReadOnlyList<GitWorkflowField> Fields)
{
    /// <summary>
    /// The verb when it depends on what is chosen in the form — Run, Save, or
    /// Delete for a saved command. It is re-read whenever a value changes.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>, GitWorkflowAction>? ActionFor { get; init; }

    /// <summary>
    /// Checks the values before the form closes. A failure stays in the form,
    /// under the field it names, with everything the person typed kept.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>, GitWorkflowFieldError?>? Validate { get; init; }
}

/// <summary>A reviewable form for named Git operations, with the same field and keyboard behavior as other dialogs.</summary>
public sealed partial class GitWorkflowDialog : DialogWindow
{
    private readonly List<(GitWorkflowField Field, Control Input)> _inputs = [];
    private readonly GitWorkflowForm? _form;

    public GitWorkflowDialog() => InitializeComponent();

    public GitWorkflowDialog(GitWorkflowForm form) : this()
    {
        ArgumentNullException.ThrowIfNull(form);
        _form = form;
        Shell.Title = form.Title;
        Shell.Subtitle = form.Detail;
        foreach (var field in form.Fields)
        {
            var input = CreateInput(field);
            AutomationProperties.SetName(input, field.Label);
            if (field.IsCheckBox)
            {
                Fields.Children.Add(input);
            }
            else
            {
                var labeled = new LabeledField { Label = field.Label, Content = input };
                FieldValidation.SetFor(labeled, field.Key);
                Fields.Children.Add(labeled);
            }

            input.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty
                    || change.Property == ComboBox.SelectedItemProperty
                    || change.Property == CheckBox.IsCheckedProperty)
                {
                    PresentAction();
                }
            };
            _inputs.Add((field, input));
        }

        PresentAction();
        Opened += (_, _) => _inputs.FirstOrDefault().Input?.Focus();
    }

    /// <summary>Whether a checkbox field was ticked when the form closed.</summary>
    public static bool IsChecked(IReadOnlyDictionary<string, string> values, string key)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(key, out var value)
            && string.Equals(value, GitWorkflowField.Checked, StringComparison.Ordinal);
    }

    private static Control CreateInput(GitWorkflowField field)
    {
        if (field.IsCheckBox)
        {
            return new CheckBox
            {
                Content = field.Label,
                IsChecked = string.Equals(field.Value, GitWorkflowField.Checked, StringComparison.Ordinal),
            };
        }

        if (field.Choices is { } choices)
        {
            return new ComboBox
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                ItemsSource = choices,
                SelectedItem = choices.Contains(field.Value, StringComparer.Ordinal) ? field.Value : choices.FirstOrDefault(),
            };
        }

        return new TextBox
        {
            Text = field.Value,
            AcceptsReturn = field.Multiline,
            TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = field.Multiline ? 160 : 0,
        };
    }

    private Dictionary<string, string> ReadValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, input) in _inputs)
        {
            var value = input switch
            {
                CheckBox box => box.IsChecked == true ? GitWorkflowField.Checked : GitWorkflowField.Unchecked,
                TextBox text => text.Text ?? "",
                ComboBox choice => choice.SelectedItem as string ?? "",
                _ => "",
            };
            values[field.Key] = field.Multiline ? value : value.Trim();
        }

        return values;
    }

    /// <summary>
    /// Dresses the button for what it will do: an accent default for a routine
    /// verb, the destructive style and no default for one that loses work.
    /// </summary>
    private void PresentAction()
    {
        if (_form is null)
        {
            return;
        }

        var action = _form.ActionFor?.Invoke(ReadValues()) ?? _form.Action;
        ApplyButton.Content = action.Verb;
        AutomationProperties.SetName(ApplyButton, action.Verb);
        ApplyButton.Classes.Remove(action.IsDestructive ? "PrimaryButton" : "DestructiveButton");
        var role = action.IsDestructive ? "DestructiveButton" : "PrimaryButton";
        if (!ApplyButton.Classes.Contains(role))
        {
            ApplyButton.Classes.Add(role);
        }

        ApplyButton.IsDefault = !action.IsDestructive;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var values = ReadValues();
        FieldValidation.Clear(Shell);
        Shell.FooterHint = null;
        var missing = _inputs.FirstOrDefault(input =>
            input.Field.Required && string.IsNullOrWhiteSpace(values[input.Field.Key]));
        var error = missing.Field is { } field
            ? new GitWorkflowFieldError(field.Key, "Enter a value to continue.")
            : _form?.Validate?.Invoke(values);
        if (error is null)
        {
            Close(values);
            return;
        }

        if (!FieldValidation.Show(Shell, error.Key, error.Message))
        {
            // A rule about the form as a whole stays in the form too.
            Shell.FooterHint = error.Message;
        }
    }
}
