using Asura.App.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

/// <summary>What the tag dialog was closed with; null means cancelled.</summary>
public sealed record GitTagCreateResult(string Name, string? Message);

/// <summary>
/// Creates a tag at HEAD. The message field decides the tag's nature: empty
/// makes a lightweight tag, anything else an annotated one.
/// </summary>
public sealed partial class GitTagCreateDialog : DialogWindow
{
    public GitTagCreateDialog()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("NameInput")!.Focus();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var name = this.FindControl<TextBox>("NameInput")?.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            FieldValidation.Show(this.FindControl<DialogShell>("Shell")!, "Name", "Enter a name for the tag.");
            return;
        }

        var annotation = this.FindControl<TextBox>("MessageInput")?.Text?.Trim();
        Close(new GitTagCreateResult(
            name,
            string.IsNullOrEmpty(annotation) ? null : annotation));
    }
}
