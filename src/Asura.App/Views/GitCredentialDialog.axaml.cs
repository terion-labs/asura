using Asura.Application;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitCredentialDialog : Window
{
    public GitCredentialDialog() => InitializeComponent();

    public GitCredentialDialog(Uri remote, bool canSave, bool rejected) : this()
    {
        RemoteLabel.Text = $"{remote.Scheme}://{remote.Authority}";
        UsernameInput.Text = Uri.UnescapeDataString(remote.UserInfo);
        SaveCredentials.IsVisible = canSave;
        if (rejected)
        {
            Description.Text = "The saved credentials did not work. Enter a username and password or access token to retry.";
        }

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
            Validation.IsVisible = true;
        }
    }
}
