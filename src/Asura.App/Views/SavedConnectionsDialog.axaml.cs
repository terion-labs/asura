using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class SavedConnectionsDialog : Window
{
    public SavedConnectionsDialog() => InitializeComponent();

    public event EventHandler<LauncherConnectionViewModel>? EditRequested;
    public event EventHandler? AddRequested;

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LauncherConnectionViewModel connection })
        {
            EditRequested?.Invoke(this, connection);
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => AddRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
