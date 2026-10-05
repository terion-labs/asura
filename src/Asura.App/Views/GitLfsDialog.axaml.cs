using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitLfsDialog : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly GitRuntimePanelViewModel? _panel;
    private bool _busy;
    private readonly bool _ready;

    public GitLfsDialog()
    {
        InitializeComponent();
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    public GitLfsDialog(GitRuntimePanelViewModel panel) : this()
    {
        _panel = panel;
        RemoteInput.ItemsSource = panel.Remotes.Select(remote => remote.Name).ToArray();
        RemoteInput.SelectedItem = panel.Remotes.FirstOrDefault()?.Name;
        _ready = true;
        Opened += async (_, _) => await ReloadAsync();
    }

    private string? Remote => RemoteInput.SelectedItem as string;
    private void ShowMessage(string text) { Message.Text = text; Message.IsVisible = text.Length > 0; }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        RemoteInput.IsEnabled = !busy;
        LockButton.IsEnabled = !busy && _panel?.CanMutateRepository == true;
        UpdateLockButtons();
    }
    private void UpdateLockButtons()
    {
        var selected = (LocksList.SelectedItem as GitLfsLockItemViewModel)?.Item;
        UnlockButton.IsEnabled = !_busy && _panel?.CanMutateRepository == true && selected?.Ownership == GitLfsLockOwnership.CurrentUser;
        ForceButton.IsEnabled = !_busy && _panel?.CanMutateRepository == true && selected is not null;
    }
    private void OnLockSelection(object? sender, SelectionChangedEventArgs e) => UpdateLockButtons();
    private async void OnRemoteChanged(object? sender, SelectionChangedEventArgs e) { if (_ready && !_busy) { await ReloadAsync(); } }
    private async void OnRefresh(object? sender, RoutedEventArgs e) => await ReloadAsync();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async Task ReloadAsync()
    {
        if (_busy || _panel is not { } panel || _lifetime.IsCancellationRequested) { return; }
        SetBusy(true);
        try
        {
            var token = _lifetime.Token;
            var locks = await panel.ReadLfsLocksAsync(Remote, token);
            var status = await panel.ReadLfsStatusAsync(token);
            var summary = await panel.ReadLfsSummaryAsync(token);
            if (_lifetime.IsCancellationRequested) { return; }
            LocksList.ItemsSource = locks is GitResult<IReadOnlyList<GitLfsLock>>.Success lockRows ? lockRows.Value.Select(item => new GitLfsLockItemViewModel(item)).ToArray() : [];
            StatusList.ItemsSource = status is GitResult<IReadOnlyList<GitLfsStatusFile>>.Success statusRows ? statusRows.Value : [];
            Summary.Text = summary is GitResult<GitTaskOutput>.Success text ? text.Value.Text : ((GitResult<GitTaskOutput>.Failure)summary).Error.Message;
            ShowMessage(locks is GitResult<IReadOnlyList<GitLfsLock>>.Failure failure ? failure.Error.Message
                : status is GitResult<IReadOnlyList<GitLfsStatusFile>>.Failure statusFailure ? statusFailure.Error.Message
                : locks is GitResult<IReadOnlyList<GitLfsLock>>.Success { Value.Count: 0 } ? "No locks on this remote." : "");
        }
        finally { if (!_lifetime.IsCancellationRequested) { SetBusy(false); } }

    }

    private async void OnLock(object? sender, RoutedEventArgs e)
    {
        var path = PathInput.Text?.Trim();
        if (string.IsNullOrEmpty(path)) { ShowMessage("Enter the file path to lock."); PathInput.Focus(); return; }
        await ApplyAsync(new(GitLfsLockAction.Lock, path, Remote));
    }
    private async void OnUnlock(object? sender, RoutedEventArgs e)
    {
        if (LocksList.SelectedItem is GitLfsLockItemViewModel { Item: { Ownership: GitLfsLockOwnership.CurrentUser } selected })
        {
            await ApplyAsync(new(GitLfsLockAction.Unlock, selected.Path, Remote, ExpectedLockId: selected.Id));
        }
    }
    private async void OnForceUnlock(object? sender, RoutedEventArgs e)
    {
        if (LocksList.SelectedItem is not GitLfsLockItemViewModel { Item: { } selected }) { return; }
        if (await new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Title = "Force unlock LFS file",
            Heading = selected.Path,
            Detail = $"Owner: {selected.OwnerName}\nLock: {selected.Id}\nRemote: {Remote}",
            Notice = "This removes the lock even when another account owns it.",
            ConfirmLabel = "Force unlock",
        }).ShowDialog<bool>(this))
        {
            await ApplyAsync(new(GitLfsLockAction.Unlock, selected.Path, Remote, Force: true, ExpectedLockId: selected.Id));
        }
    }
    private async Task ApplyAsync(GitLfsLockRequest request)
    {
        if (_busy || _panel is not { } panel || _lifetime.IsCancellationRequested) { return; }
        SetBusy(true);
        var success = await panel.ManageLfsLockAsync(request);
        if (_lifetime.IsCancellationRequested) { return; }
        SetBusy(false);
        if (success) { await ReloadAsync(); }
        else { ShowMessage(panel.IssueMessage ?? "The lock action could not complete. Refresh before retrying."); }
    }
}
