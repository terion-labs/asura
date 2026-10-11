using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class SecretEditorDialog : DialogWindow
{
    public SecretEditorDialog()
    {
        InitializeComponent();
    }

    public SecretEditorDialog(SecretEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is SecretEditorViewModel viewModel)
        {
            viewModel.ClearReplacement();
        }

        base.OnClosed(e);
    }

    private SecretEditorViewModel ViewModel => DataContext as SecretEditorViewModel
        ?? throw new InvalidOperationException("The credential editor is unavailable.");

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnRelabelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        TryClose(ViewModel.CreateRelabelRequest);
    }

    private void OnRotateClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        TryClose(ViewModel.CreateRotateRequest);
    }

    private void TryClose(Func<SecretEditRequest> createRequest)
    {
        try
        {
            Close(createRequest());
        }
        catch (ArgumentException exception)
        {
            // Under the field it is about; anything else in the list at the top.
            if (!FieldValidation.Present(this, exception))
            {
                ViewModel.ErrorNotices.Report(FailureReason.Refusal(
                    "secret.editor.refused",
                    exception,
                    "Asura couldn’t update the credential. Check the label and value, then try again."));
            }
        }
    }
}
