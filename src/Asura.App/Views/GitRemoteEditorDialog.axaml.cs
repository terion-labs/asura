using Asura.App.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

/// <summary>What the remote editor was closed with; null means cancelled.</summary>
public sealed record GitRemoteEditorResult(string Name, string Url);

/// <summary>
/// One dialog for both remote gestures: adding a remote and editing one.
/// The caller supplies the initial values for edit and reads back a result;
/// the Git-side consequences (rename versus set-url) are the adapter's call.
/// </summary>
public sealed partial class GitRemoteEditorDialog : DialogWindow
{
    public GitRemoteEditorDialog()
    {
        InitializeComponent();
    }

    public GitRemoteEditorDialog(
        string title,
        string action,
        string? initialName = null,
        string? initialUrl = null)
        : this()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        this.FindControl<DialogShell>("Shell")!.Title = title;
        this.FindControl<Button>("ConfirmButton")!.Content = action;
        this.FindControl<TextBox>("NameInput")!.Text = initialName ?? string.Empty;
        this.FindControl<TextBox>("UrlInput")!.Text = initialUrl ?? string.Empty;
        Opened += (_, _) =>
        {
            var input = this.FindControl<TextBox>("NameInput")!;
            input.Focus();
            input.SelectAll();
        };
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
        var url = this.FindControl<TextBox>("UrlInput")?.Text?.Trim() ?? string.Empty;
        if (name.Length == 0 || url.Length == 0)
        {
            var shell = this.FindControl<DialogShell>("Shell")!;
            FieldValidation.Clear(shell);
            FieldValidation.Show(
                shell,
                name.Length == 0 ? "Name" : "Url",
                name.Length == 0 ? "Enter a name for the remote." : "Enter the remote's URL.");
            return;
        }

        Close(new GitRemoteEditorResult(name, url));
    }
}
