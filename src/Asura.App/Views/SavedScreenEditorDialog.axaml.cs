using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

public sealed partial class SavedScreenEditorDialog : DialogWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SavedScreenPersistenceOperation? _persist;
    private bool _closeApproved;
    private bool _closeInProgress;

    public SavedScreenEditorDialog()
    {
        InitializeComponent();
    }

    public SavedScreenEditorDialog(
        SavedScreenEditorViewModel viewModel,
        SavedScreenPersistenceOperation persist)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        DataContext = viewModel;
    }

    private SavedScreenEditorViewModel ViewModel => DataContext as SavedScreenEditorViewModel
        ?? throw new InvalidOperationException("The saved-screen editor view model is unavailable.");

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeApproved
            && DataContext is SavedScreenEditorViewModel { IsSaving: true })
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }

        if (!_closeApproved
            && DataContext is SavedScreenEditorViewModel viewModel
            && viewModel.RequestCancel() == SavedScreenEditorCancelDisposition.ConfirmDiscard)
        {
            e.Cancel = true;
            if (!_closeInProgress)
            {
                _ = RequestCancelAsync();
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }

    private async void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await RequestCancelAsync();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await TryPersistAsync(ViewModel.SaveAsync);
    }

    private async void OnDuplicateClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        await TryPersistAsync(ViewModel.DuplicateAsync);
    }

    private async Task TryPersistAsync(
        Func<SavedScreenPersistenceOperation, CancellationToken, ValueTask<bool>> persistDraft)
    {
        ArgumentNullException.ThrowIfNull(persistDraft);
        try
        {
            var persist = _persist
                ?? throw new InvalidOperationException(
                    "Saved-screen persistence is unavailable.");
            var saved = await persistDraft(persist, _lifetime.Token);
            if (!saved)
            {
                return;
            }

            _closeApproved = true;
            Close(true);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            // Under the field it is about, with everything typed kept; a
            // failure about the whole screen goes to the list at the top.
            if (!FieldValidation.Present(this, exception))
            {
                ViewModel.ErrorNotices.Report(exception.Message);
                this.FindControl<Control>("FormErrors")?.BringIntoView();
            }
        }
    }

    private async Task RequestCancelAsync()
    {
        if (_closeInProgress || ViewModel.IsSaving)
        {
            return;
        }

        if (ViewModel.RequestCancel() == SavedScreenEditorCancelDisposition.Close)
        {
            _closeApproved = true;
            Close(false);
            return;
        }

        _closeInProgress = true;
        try
        {
            var discard = await Confirmations.DiscardChanges(
                    "Discard saved screen changes?",
                    "The unsaved name, description, panel assignments, and startup settings will be lost.")
                .ShowDialog<bool>(this);
            if (!discard)
            {
                return;
            }

            _closeApproved = true;
            Close(false);
        }
        finally
        {
            _closeInProgress = false;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
