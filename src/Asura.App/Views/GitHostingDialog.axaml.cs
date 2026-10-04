using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitHostingDialog : Window
{
    private GitHostingBrowserViewModel? ViewModel => DataContext as GitHostingBrowserViewModel;
    public GitHostingDialog() => InitializeComponent();
    public GitHostingDialog(GitHostingBrowserViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAccountsAsync();
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnClone(object? sender, RoutedEventArgs e)
    {
        if (Repositories.SelectedItem is GitHostedRepository repository)
        {
            Close(new GitHostingCloneSelection(repository, ViewModel?.LoadedAccount));
        }
    }

    private async void OnLoad(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.LoadRepositoriesAsync(reset: true);
        }
    }

    private async void OnSignIn(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.LoadRepositoriesAsync(reset: true, signIn: true);
        }
    }

    private async void OnLoadMore(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.LoadRepositoriesAsync(reset: false);
        }
    }

    private async void OnRemoveAccount(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.RemoveAccountAsync();
        }
    }
}
