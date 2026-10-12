using Asura.App.Controls;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

/// <summary>
/// Asks for the password of a saved connection stored without one. Closes
/// with the typed value and the user's explicit persistence choice, or null
/// on cancel.
/// </summary>
public sealed partial class DatabasePasswordPromptDialog : DialogWindow
{
    public DatabasePasswordPromptDialog()
    {
        InitializeComponent();
    }

    public DatabasePasswordPromptDialog(
        string connectionName,
        bool canSavePassword = false,
        string? passwordStoreLabel = null,
        string? description = null)
        : this()
    {
        Shell.Title = $"{connectionName} Password";
        AutomationProperties.SetName(PasswordInput, $"{connectionName} password");
        if (canSavePassword)
        {
            PromptDescription.Text =
                "Use this password for the current session, or save it securely for future connections.";
            SavePasswordCheckBox.Content = passwordStoreLabel ?? PlatformTerms.SaveInSecretStore;
            SavePasswordCheckBox.IsVisible = true;
        }

        if (description is not null)
        {
            PromptDescription.Text = description;
        }

        Opened += (_, _) => PasswordInput.Focus();
    }

    private void OnPasswordTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        SavePasswordCheckBox.IsEnabled = !string.IsNullOrEmpty(PasswordInput.Text);
        if (!SavePasswordCheckBox.IsEnabled)
        {
            SavePasswordCheckBox.IsChecked = false;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(new DatabasePasswordPromptResult(
            PasswordInput.Text ?? string.Empty,
            SavePasswordCheckBox.IsVisible && SavePasswordCheckBox.IsChecked == true));
    }
}
