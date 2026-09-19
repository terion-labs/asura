using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class ConnectionSecretEditorDialog : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public ConnectionSecretEditorDialog() => InitializeComponent();

    public ConnectionSecretEditorDialog(ConnectionSecretEditorViewModel viewModel) : this() => DataContext = viewModel;

    private ConnectionSecretEditorViewModel ViewModel => (ConnectionSecretEditorViewModel)DataContext!;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Finish the vault write before allowing the result to lose its owner.
        e.Cancel = DataContext is ConnectionSecretEditorViewModel { IsSaving: true };
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (DataContext is ConnectionSecretEditorViewModel viewModel)
        {
            viewModel.Value = string.Empty;
        }
        base.OnClosed(e);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (await ViewModel.SaveAsync(_lifetime.Token) is { } credential)
        {
            Close(credential);
        }
    }
}
