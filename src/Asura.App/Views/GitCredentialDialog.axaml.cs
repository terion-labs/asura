using Asura.App.Controls;
using Asura.Application;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitCredentialDialog : DialogWindow
{
    public GitCredentialDialog() => InitializeComponent();

    public GitCredentialDialog(Uri remote, bool canSave, bool rejected) : this()
    {
        Shell.Subtitle = rejected
            ? $"The saved credentials for {remote.Scheme}://{remote.Authority} did not work. Enter a username and password or access token to retry."
            : $"Enter your username and password or personal access token for {remote.Scheme}://{remote.Authority} to continue.";
        UsernameInput.Text = Uri.UnescapeDataString(remote.UserInfo);
        SaveCredentials.IsVisible = canSave;
        Opened += (_, _) => (string.IsNullOrEmpty(UsernameInput.Text) ? UsernameInput : PasswordInput).Focus();
        Closed += (_, _) => { UsernameInput.Text = null; PasswordInput.Text = null; };
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSignInClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var credentials = GitCredentials.Create(UsernameInput.Text ?? "", PasswordInput.Text ?? "",
                SaveCredentials.IsVisible && SaveCredentials.IsChecked == true);
            Close(credentials);
        }
        catch (ArgumentException)
        {
            // Say which field to fix, under that field, and keep both values.
            FieldValidation.Clear(Shell);
            if (Problem(UsernameInput.Text, "a username") is { } username)
            {
                FieldValidation.Show(Shell, "Username", username);
            }
            else if (Problem(PasswordInput.Text, "a password or token") is { } password)
            {
                FieldValidation.Show(Shell, "Password", password);
            }
        }
    }

    private static string? Problem(string? value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? $"Enter {what}."
            : value.IndexOfAny(['\r', '\n', '\0']) >= 0
                ? $"Enter {what} without line breaks."
                : null;
}
