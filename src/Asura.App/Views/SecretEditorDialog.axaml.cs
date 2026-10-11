using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class SecretEditorDialog : DialogWindow
{
    private SecretEditorViewModel? _observed;

    public SecretEditorDialog()
    {
        InitializeComponent();
    }

    public SecretEditorDialog(SecretEditorViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        Observe(DataContext as SecretEditorViewModel);
        base.OnDataContextChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Observe(null);
        if (DataContext is SecretEditorViewModel viewModel)
        {
            viewModel.ClearReplacement();
        }

        base.OnClosed(e);
    }

    private void Observe(SecretEditorViewModel? viewModel)
    {
        _observed?.PropertyChanged -= OnViewModelPropertyChanged;
        _observed = viewModel;
        _observed?.PropertyChanged += OnViewModelPropertyChanged;

        UpdateDefaultButton();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, nameof(SecretEditorViewModel.CanRotate), StringComparison.Ordinal))
        {
            UpdateDefaultButton();
        }
    }

    /// <summary>
    /// Rotate Value is unavailable until a replacement value is typed, so
    /// until then Return saves the label instead of doing nothing.
    /// </summary>
    private void UpdateDefaultButton()
    {
        var canRotate = _observed?.CanRotate == true;
        if (this.FindControl<Button>("RotateValueButton") is { } rotate
            && this.FindControl<Button>("SaveLabelButton") is { } saveLabel)
        {
            rotate.IsDefault = canRotate;
            saveLabel.IsDefault = !canRotate;
        }
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
