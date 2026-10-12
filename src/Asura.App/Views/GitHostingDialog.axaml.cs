using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitHostingDialog : DialogWindow
{
    private GitHostingBrowserViewModel? ViewModel => DataContext as GitHostingBrowserViewModel;
    public GitHostingDialog() => InitializeComponent();
    public GitHostingDialog(GitHostingBrowserViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAccountsAsync();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Dispose();
        };
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnClone(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { CanClone: true, SelectedRepository: { } repository } vm)
        {
            Close(new GitHostingCloneSelection(repository, vm.LoadedAccount));
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
        if (ViewModel is not { SelectedAccount: { } account } vm)
        {
            return;
        }

        // Asura forgets the account and its stored token; the account itself
        // on the host is untouched, and signing in again brings it back.
        if (!await new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = $"Remove “{account.Username}” from Asura?",
            Detail = $"Asura forgets this {GitHostingBrowserViewModel.ProviderName(account.Provider)} account and deletes its token from {Asura.Core.PlatformTerms.SecretStore}.",
            Notice = "The account on the host isn’t changed. Sign in again to add it back.",
            ConfirmLabel = "Remove Account",
            Intent = ConfirmationIntent.Destructive,
        }).ShowDialog<bool>(this))
        {
            return;
        }

        await vm.RemoveAccountAsync();
    }

    // Reloads after the view model holds the new Starred value. The check
    // box's own IsCheckedChanged fires before its binding writes back, so a
    // load started there would ask for the old list and be discarded.
    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(GitHostingBrowserViewModel.Starred), StringComparison.Ordinal)
            && ViewModel is { SelectedAccount: not null, IsBusy: false } vm)
        {
            await vm.LoadRepositoriesAsync(reset: true);
        }
    }
}
