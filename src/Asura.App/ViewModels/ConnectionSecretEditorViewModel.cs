using System.Text;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>Creates a vault entry scoped to the connection draft's stable identity.</summary>
public sealed class ConnectionSecretEditorViewModel(
    ISecretVault vault,
    SecretScope scope,
    SecretKind kind,
    string label) : ObservableObject
{
    private readonly SecretRef _reference = SecretRef.New();
    private string _label = label;
    private string _value = string.Empty;
    private string _error = string.Empty;
    private bool _isSaving;

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                ReportError(value);
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public string StorageHint => vault.Availability.CanPersist
        ? "Saved credentials remain in the vault if you cancel the connection. Manage them in Settings."
        : vault.Availability.Message;

    public string Title => kind == SecretKind.PrivateKey ? "Add private key" : "Add credential";

    public bool IsMultiline => kind is SecretKind.PrivateKey or SecretKind.Other;

    public string ValueHint => kind switch
    {
        SecretKind.PrivateKey => "Paste the complete private key, including its BEGIN and END lines.",
        SecretKind.Other => "For S3, enter JSON with accessKeyId, secretAccessKey, and optional sessionToken.",
        SecretKind.Passphrase => "Enter the passphrase that unlocks the private key.",
        _ => "Enter the password for this connection.",
    };

    public async Task<SecretMetadata?> SaveAsync(CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return null;
        }

        Error = string.Empty;
        if (string.IsNullOrWhiteSpace(Label) || string.IsNullOrEmpty(Value))
        {
            Error = "Enter a label and a credential value.";
            return null;
        }

        IsSaving = true;
        try
        {
            using var material = SecretMaterial.TakeOwnership(Encoding.UTF8.GetBytes(Value));
            Value = string.Empty;
            var request = new CreateSecretRequest(_reference, Label.Trim(), kind, scope,
                new SecretUsePurpose(SecretUseKind.UserManagement, scope.OwnerId!));
            var result = await vault.CreateAsync(request, material, cancellationToken);
            if (result is SecretVaultResult<SecretMetadata>.Success success)
            {
                return success.Value;
            }

            Error = SecretVaultError.Create(((SecretVaultResult<SecretMetadata>.Failure)result).Error.Code).Message;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            // Provider exceptions can include input values; only show a fixed message.
            Error = "The credential could not be saved. Check the credential vault and try again.";
            return null;
        }
        finally
        {
            Value = string.Empty;
            IsSaving = false;
        }
    }
}
