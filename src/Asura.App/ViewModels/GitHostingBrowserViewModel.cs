using System.Collections.ObjectModel;
using Asura.Core;
using Asura.Git;
using Avalonia.Data.Converters;

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
    private GitHostedRepository? _selectedRepository;
    private bool _presentingAccounts;
    private int _selectionVersion;
    public GitHostingAccount? LoadedAccount { get; private set; }

    /// <summary>A provider as its company spells it.</summary>
    public static string ProviderName(GitHostingProvider provider) => provider switch
    {
        GitHostingProvider.GitHub => "GitHub",
        GitHostingProvider.GitLab => "GitLab",
        _ => provider.ToString(),
    };

    /// <summary><see cref="ProviderName"/> for a provider pop-up's rows.</summary>
    public static IValueConverter ProviderNameConverter { get; } =
        new FuncValueConverter<GitHostingProvider, string>(ProviderName);
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
        RaiseRepositoriesChanged();
    }

    private void RaiseRepositoriesChanged()
    {
        OnPropertyChanged(nameof(Repositories));
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(EmptyListText));
    }

    /// <summary>Whether the repository list has no row to show, so it says why instead.</summary>
    public bool IsListEmpty => Repositories.Count == 0;

    /// <summary>What the empty repository list says: what to do next, or that the filter matched nothing.</summary>
    public string EmptyListText =>
        _repositories.Count > 0 ? $"No repositories match “{Filter}”."
        : LoadedAccount is not null ? (Starred ? "This account hasn’t starred any repositories." : "This account has no repositories.")
        : SelectedAccount is not null ? (Starred ? "Choose Load Repositories to list this account’s starred repositories." : "Choose Load Repositories to list this account’s repositories.")
        : "Sign in to a hosting account to list its repositories.";

    /// <summary>Remove Account acts on the chosen account, so it waits for one.</summary>
    public bool CanRemoveAccount => SelectedAccount is not null && !IsBusy;
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
                OnPropertyChanged(nameof(CanRemoveAccount));
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
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) { RaiseRepositoriesChanged(); } } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool Starred { get => _starred; set { if (SetProperty(ref _starred, value)) { InvalidateRepositories(); } } }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRemoveAccount));
                OnPropertyChanged(nameof(CanClone));
            }
        }
    }

    /// <summary>The repository chosen in the list, which Clone Repository… clones.</summary>
    public GitHostedRepository? SelectedRepository
    {
        get => _selectedRepository;
        set { if (SetProperty(ref _selectedRepository, value)) { OnPropertyChanged(nameof(CanClone)); } }
    }

    /// <summary>Clone Repository… waits for a chosen repository and for any load or sign-in to finish.</summary>
    public bool CanClone => SelectedRepository is not null && !IsBusy;
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
            RaiseRepositoriesChanged();
            Status = $"{CountText.OrNone(_repositories.Count, "repository", "repositories")}{(HasMore ? " · more available" : "")}";
        }
        catch (OperationCanceledException)
        {
            Status = "Repository request canceled or timed out.";
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
