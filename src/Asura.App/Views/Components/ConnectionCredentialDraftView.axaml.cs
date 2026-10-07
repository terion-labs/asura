using Asura.App.ViewModels;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// Writes one new credential to the vault, in place inside the connection
/// editor. The editor opens it with a draft, listens for
/// <see cref="Created"/> or <see cref="Cancelled"/>, and folds it away.
/// </summary>
public sealed partial class ConnectionCredentialDraftView : UserControl
{
    public ConnectionCredentialDraftView() => InitializeComponent();

    /// <summary>The credential was stored and should be selected.</summary>
    public event EventHandler<SecretMetadata>? Created;

    /// <summary>The draft was put away without storing anything.</summary>
    public event EventHandler? Cancelled;

    /// <summary>The editor's lifetime: closing it cancels a write in flight.</summary>
    public CancellationToken Lifetime { get; set; }

    /// <summary>Whether a vault write is in flight, during which the draft cannot be put away.</summary>
    public bool IsSaving => DataContext is ConnectionSecretEditorViewModel { IsSaving: true };

    /// <summary>
    /// Makes the draft's Save the button Return presses, or gives Return back.
    /// While a draft is open, Return in it stores the credential rather than
    /// saving the whole connection around it.
    /// </summary>
    public bool IsSaveDefault
    {
        get => SaveButton.IsDefault;
        set => SaveButton.IsDefault = value;
    }

    /// <summary>Puts the keyboard in the draft's first field.</summary>
    public void FocusFirstField() => LabelInput.Focus(NavigationMethod.Tab);

    /// <summary>
    /// Folds the draft away, clearing anything typed into the value box.
    /// Refused while the vault write is in flight.
    /// </summary>
    public bool TryCancel()
    {
        if (IsSaving)
        {
            return false;
        }

        if (DataContext is ConnectionSecretEditorViewModel draft)
        {
            draft.Value = string.Empty;
        }

        Cancelled?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = TryCancel();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (DataContext is ConnectionSecretEditorViewModel draft
            && await draft.SaveAsync(Lifetime) is { } credential)
        {
            Created?.Invoke(this, credential);
        }
    }
}
