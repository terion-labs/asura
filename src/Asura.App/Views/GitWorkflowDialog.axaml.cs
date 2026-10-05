using Asura.App.Controls;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed record GitWorkflowField(string Key, string Label, string Value = "", bool Required = true,
    bool Multiline = false, IReadOnlyList<string>? Choices = null);

/// <summary>A reviewable form for named Git operations, with the same field and keyboard behavior as other dialogs.</summary>
public sealed partial class GitWorkflowDialog : Window
{
    private readonly List<(GitWorkflowField Field, Control Input)> _inputs = [];

    public GitWorkflowDialog() => InitializeComponent();

    public GitWorkflowDialog(string title, string detail, IReadOnlyList<GitWorkflowField> fields, string action = "Apply") : this()
    {
        Title = title;
        Shell.Title = title;
        Shell.Subtitle = detail;
        ApplyButton.Content = action;
        foreach (var field in fields)
        {
            Control input = field.Choices is { } choices
                ? new ComboBox { ItemsSource = choices, SelectedItem = choices.Contains(field.Value, StringComparer.Ordinal) ? field.Value : choices.FirstOrDefault() }
                : new TextBox { Text = field.Value, AcceptsReturn = field.Multiline, TextWrapping = field.Multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap, MinHeight = field.Multiline ? 160 : 0 };
            AutomationProperties.SetName(input, field.Label);
            Fields.Children.Add(new LabeledField { Label = field.Label, Content = input });
            _inputs.Add((field, input));
        }
        Opened += (_, _) => _inputs.FirstOrDefault().Input?.Focus();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(null); }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, input) in _inputs)
        {
            var value = input switch { TextBox text => text.Text ?? "", ComboBox choice => choice.SelectedItem as string ?? "", _ => "" };
            if (field.Required && string.IsNullOrWhiteSpace(value))
            {
                ValidationMessage.Text = $"Enter {field.Label.ToLowerInvariant()}.";
                ValidationMessage.IsVisible = true;
                input.Focus();
                return;
            }
            values.Add(field.Key, field.Multiline ? value : value.Trim());
        }
        Close(values);
    }
}
