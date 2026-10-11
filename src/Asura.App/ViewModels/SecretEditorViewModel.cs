using System.Text;
using Asura.Application;

namespace Asura.App.ViewModels;

public enum SecretEditAction
{
    Relabel,
    Rotate,
}

public sealed record SecretEditRequest(
    SecretEditAction Action,
    string Label,
    SecretMaterial? Replacement);

public sealed class SecretEditorViewModel : ObservableObject
{
    private string _label;
    private string _replacementValue = string.Empty;

    public SecretEditorViewModel(SecretMetadataViewModel secret)
    {
        Secret = secret ?? throw new ArgumentNullException(nameof(secret));
        _label = secret.Label;
    }

    public SecretMetadataViewModel Secret { get; }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string ReplacementValue
    {
        get => _replacementValue;
        set
        {
            if (SetProperty(ref _replacementValue, value))
            {
                OnPropertyChanged(nameof(CanRotate));
            }
        }
    }

    /// <summary>
    /// Rotate Value has nothing to do until there is a value to rotate to, so
    /// it stays unavailable until one is typed rather than refusing afterward.
    /// </summary>
    public bool CanRotate => !string.IsNullOrEmpty(ReplacementValue);

    public SecretEditRequest CreateRelabelRequest() => new(
        SecretEditAction.Relabel,
        RequireLabel(),
        null);

    public SecretEditRequest CreateRotateRequest()
    {
        if (string.IsNullOrEmpty(ReplacementValue))
        {
            throw InvalidField.Exception(
                nameof(ReplacementValue),
                "Enter a replacement value before you rotate the credential.");
        }

        var bytes = Encoding.UTF8.GetBytes(ReplacementValue);
        ReplacementValue = string.Empty;
        return new SecretEditRequest(
            SecretEditAction.Rotate,
            RequireLabel(),
            SecretMaterial.TakeOwnership(bytes));
    }

    public void ClearReplacement() => ReplacementValue = string.Empty;

    private string RequireLabel()
    {
        var label = Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            throw InvalidField.Exception(nameof(Label), "Enter a label for the credential.");
        }

        return label;
    }
}
