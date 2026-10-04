using System.Collections.ObjectModel;
using Asura.Core;
using Asura.Git;

namespace Asura.App.ViewModels;

public sealed class GitHostingBrowserViewModel(IGitRepositoryClient client, ConnectionProfile connection) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private GitHostingProvider _provider;
    private string _apiAddress = "https://api.github.com/";
    private string _filter = "";
    private string _status = "";
    private bool _starred;
    private bool _isBusy;
    private bool _hasMore;
    private int _page;
    private GitHostingAccount? _selectedAccount;
    private bool _presentingAccounts;
    private int _selectionVersion;
    public GitHostingAccount? LoadedAccount { get; private set; }
    private void InvalidateRepositories()
    {
        if (_presentingAccounts)
        {
            return;
        }
        _selectionVersion++;
        _repositories.Clear();
        _page = 0;
        HasMore = false;
        LoadedAccount = null;
        OnPropertyChanged(nameof(Repositories));
    }
    public ObservableCollection<GitHostingAccount> Accounts { get; } = [];
    private readonly List<GitHostedRepository> _repositories = [];
    public IReadOnlyList<GitHostedRepository> Repositories => [.. _repositories.Where(repository => repository.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))];
    public IReadOnlyList<GitHostingProvider> Providers { get; } = Enum.GetValues<GitHostingProvider>();
    public GitHostingProvider Provider
    {
        get => _provider;
        set
        {
            if (SetProperty(ref _provider, value))
            {
                InvalidateRepositories();
                ApiAddress = value == GitHostingProvider.GitHub ? "https://api.github.com/" : "https://gitlab.com/api/v4/";
            }
        }
    }

    public GitHostingAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                InvalidateRepositories();
                if (value is not null)
                {
                    Provider = value.Provider;
                    ApiAddress = value.ApiBase.AbsoluteUri;
                }
            }
        }
    }

    public string ApiAddress { get => _apiAddress; set { if (SetProperty(ref _apiAddress, value)) { InvalidateRepositories(); } } }
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) { OnPropertyChanged(nameof(Repositories)); } } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool Starred { get => _starred; set { if (SetProperty(ref _starred, value)) { InvalidateRepositories(); } } }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool HasMore { get => _hasMore; private set => SetProperty(ref _hasMore, value); }

    public async Task LoadAccountsAsync()
    {
        var result = await client.ReadHostingAccountsAsync(connection, _lifetime.Token);
        if (result is GitResult<IReadOnlyList<GitHostingAccount>>.Success success)
        {
            var reference = SelectedAccount?.Reference;
            Accounts.Clear();
            foreach (var account in success.Value)
            {
                Accounts.Add(account);
            }

            SelectedAccount = Accounts.FirstOrDefault(account => account.Reference == reference) ?? Accounts.FirstOrDefault();
        }
    }

    public async Task LoadRepositoriesAsync(bool reset, bool signIn = false)
    {
        if (IsBusy)
        {
            return;
        }

        if (!Uri.TryCreate(ApiAddress, UriKind.Absolute, out var api))
        {
            Status = "Enter the provider's HTTPS API base URL.";
            return;
        }

        var selectionVersion = _selectionVersion;
        var token = _lifetime.Token;
        var requestedAccount = signIn ? null : SelectedAccount;
        IsBusy = true;
        try
        {
            var result = await client.ReadHostedRepositoriesAsync(connection, Provider, api, requestedAccount,
                reset ? 1 : _page + 1, Starred, token);
            if (token.IsCancellationRequested || selectionVersion != _selectionVersion)
            {
                return;
            }
            if (result is GitResult<GitHostedRepositoryPage>.Failure failure)
            {
                Status = failure.Error.Message;
                return;
            }

            var page = ((GitResult<GitHostedRepositoryPage>.Success)result).Value;
            _presentingAccounts = true;
            try
            {
                await LoadAccountsAsync();
                token.ThrowIfCancellationRequested();
                SelectedAccount = page.Account is { } account ? Accounts.FirstOrDefault(item => item.Reference == account.Reference) ?? account : requestedAccount;
            }
            finally { _presentingAccounts = false; }
            LoadedAccount = page.Account ?? requestedAccount;
            if (reset)
            {
                _repositories.Clear();
            }

            _repositories.AddRange(page.Repositories);
            _page = page.Page;
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(Repositories));
            Status = $"{_repositories.Count} repositories{(HasMore ? " · more available" : "")}";
        }
        catch (OperationCanceledException)
        {
            Status = "Repository request cancelled or timed out.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveAccountAsync()
    {
        if (SelectedAccount is not { } account)
        {
            return;
        }

        var result = await client.RemoveHostingAccountAsync(connection, account, _lifetime.Token);
        Status = result is GitResult<GitUnit>.Failure failure ? failure.Error.Message : "Account removed from Asura.";
        InvalidateRepositories();
        await LoadAccountsAsync();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
