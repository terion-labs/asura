using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitSubmoduleDialog : DialogWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly GitRuntimePanelViewModel? _panel;
    private GitSubmoduleItem? _item;
    private GitRepositoryHandle? _comparisonRepository;
    private string? _comparisonBase;
    private string? _comparisonTarget;
    private bool _busy;
    private int _patchGeneration;

    public GitSubmoduleDialog()
    {
        InitializeComponent();
        GitDialogFooter.Span(Shell, FooterRow);
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    public GitSubmoduleDialog(GitRuntimePanelViewModel panel, GitSubmoduleItem item) : this()
    {
        _panel = panel;
        _item = item;
        Shell.Title = $"Submodule {item.Path}";
        PresentItem();
        Opened += async (_, _) => await ReloadAsync();
    }

    /// <summary>The submodule was opened; the panel should switch to it.</summary>
    public event EventHandler<GitRepositoryHandle>? RepositoryOpened;

    private void ShowMessage(string message) { Message.Text = message; Message.IsVisible = message.Length > 0; }
    private void SetBusy(bool busy) { _busy = busy; PresentItem(); }
    private void PresentItem()
    {
        if (_item is not { } item) { return; }
        StateLabel.Text = !item.IsInitialized ? "Not initialized" : item.IsDirty ? "Initialized · local changes present" : "Initialized · clean";
        if (item.IsInitialized && !string.Equals(item.ExpectedRevision, item.CheckedOutRevision, StringComparison.Ordinal))
        {
            StateLabel.Text += " · checkout differs from recorded revision";
        }
        ExpectedInput.Text = item.ExpectedRevision;
        CheckoutInput.Text = item.CheckedOutRevision ?? "Not checked out";
        RefreshItem.IsEnabled = !_busy;
        InitializeItem.IsEnabled = !_busy && !item.IsInitialized && _panel?.CanMutateRepository == true;
        UpdateItem.IsEnabled = !_busy && _panel?.CanMutateRepository == true;
        SyncItem.IsEnabled = !_busy && _panel?.CanMutateRepository == true;
        OpenButton.IsEnabled = !_busy && item.IsInitialized;
        CompareItem.IsEnabled = !_busy && item.IsInitialized && item.CheckedOutRevision is not null;
        PresentWells();
    }

    /// <summary>Each well says what goes in it while it has nothing to show.</summary>
    private void PresentWells()
    {
        ChangesEmpty.Text = _comparisonRepository is null
            ? "Choose Compare Recorded and Checkout from Actions to list the files that differ."
            : "The recorded and checked-out revisions contain the same files.";
        ChangesEmpty.IsVisible = ChangesList.ItemCount == 0;
        PatchEmpty.IsVisible = string.IsNullOrEmpty(PatchInput.Text);
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await ReloadAsync();
    private async Task ReloadAsync()
    {
        if (_busy || _panel is not { } panel || _item is not { } item || _lifetime.IsCancellationRequested) { return; }
        SetBusy(true);
        try
        {
            var result = await panel.ReadSubmodulesAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) { return; }
            if (result is GitResult<IReadOnlyList<GitSubmoduleItem>>.Failure failure) { ShowMessage(failure.Error.Message); return; }
            var refreshed = ((GitResult<IReadOnlyList<GitSubmoduleItem>>.Success)result).Value.FirstOrDefault(candidate => string.Equals(candidate.Path, item.Path, StringComparison.Ordinal));
            if (refreshed is null) { ShowMessage("This submodule is no longer recorded in the repository."); return; }
            _item = refreshed;
            _comparisonRepository = null;
            _patchGeneration++;
            ChangesList.ItemsSource = null;
            PatchInput.Text = "";
            ShowMessage("");
        }
        finally { if (!_lifetime.IsCancellationRequested) { SetBusy(false); } }
    }

    private async void OnSubmoduleAction(object? sender, RoutedEventArgs e)
    {
        if (_busy || _panel is not { } panel || _item is not { } item || sender is not MenuItem { Tag: string tag }
            || !Enum.TryParse<GitSubmoduleAction>(tag, out var action)) { return; }
        if (!await new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = $"{tag} Submodule “{item.Path}”?",
            Detail = "Recorded revision: " + item.ExpectedRevision,
            Notice = item.IsDirty ? "This submodule has local edits. Git may refuse to move the checkout until they are saved." : "The action applies recursively to this submodule.",
            ConfirmLabel = tag,
            // Git refuses to move a checkout over local edits, so nothing is lost.
            Intent = ConfirmationIntent.Confirm,
        }).ShowDialog<bool>(this)) { return; }
        SetBusy(true);
        var success = await panel.ApplySubmoduleChangeAsync(new(action, item.Path));
        if (_lifetime.IsCancellationRequested) { return; }
        SetBusy(false);
        if (success) { await ReloadAsync(); }
        else { ShowMessage(panel.IssueMessage ?? "The submodule action couldn’t finish. Refresh, then try again."); }
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_busy || _panel is not { } panel || _item is not { } item) { return; }
        SetBusy(true);
        var result = await panel.OpenSubmoduleAsync(item.Path, _lifetime.Token);
        if (_lifetime.IsCancellationRequested) { return; }
        SetBusy(false);
        if (result is GitResult<GitRepositoryHandle>.Success success)
        {
            // The panel switches to the submodule, which this window no
            // longer describes, so it closes once it has handed it over.
            RepositoryOpened?.Invoke(this, success.Value);
            Close();
        }
        else { ShowMessage(((GitResult<GitRepositoryHandle>.Failure)result).Error.Message); }
    }

    private async void OnCompare(object? sender, RoutedEventArgs e)
    {
        if (_busy || _panel is not { } panel || _item is not { CheckedOutRevision: { } target } item) { return; }
        SetBusy(true);
        try
        {
            var token = _lifetime.Token;
            var handle = await panel.OpenSubmoduleAsync(item.Path, token);
            if (_lifetime.IsCancellationRequested) { return; }
            if (handle is GitResult<GitRepositoryHandle>.Failure handleFailure) { ShowMessage(handleFailure.Error.Message); return; }
            var comparison = await panel.ReadSubmoduleComparisonAsync(item.Path, item.ExpectedRevision, target, token);
            if (_lifetime.IsCancellationRequested) { return; }
            if (comparison is GitResult<GitComparison>.Failure failure) { ShowMessage(failure.Error.Message); return; }
            _comparisonRepository = ((GitResult<GitRepositoryHandle>.Success)handle).Value;
            _comparisonBase = item.ExpectedRevision;
            _comparisonTarget = target;
            var changes = ((GitResult<GitComparison>.Success)comparison).Value.Changes;
            ChangesList.ItemsSource = changes.Select(change => new GitChangeItemViewModel(change)).ToArray();
            ChangesList.SelectedIndex = changes.Count > 0 ? 0 : -1;
            ShowMessage(changes.Count == 0 ? "" : "Showing checked-out revision against recorded revision. Local uncommitted edits are shown in the opened submodule panel.");
        }
        finally { if (!_lifetime.IsCancellationRequested) { SetBusy(false); } }
    }

    private async void OnComparisonSelection(object? sender, SelectionChangedEventArgs e)
    {
        var generation = ++_patchGeneration;
        if (_lifetime.IsCancellationRequested || _panel is not { } panel || _comparisonRepository is not { } repository || ChangesList.SelectedItem is not GitChangeItemViewModel change
            || _comparisonBase is null || _comparisonTarget is null) { PatchInput.Text = ""; PresentWells(); return; }
        var result = await panel.ReadSubmoduleDiffAsync(repository, new(GitDiffArea.Commit, change.Path, OriginalPath: change.Change.OriginalPath,
            BaseRevision: _comparisonBase, CommitSha: _comparisonTarget), _lifetime.Token);
        if (_lifetime.IsCancellationRequested || generation != _patchGeneration) { return; }
        PatchInput.Text = result is GitResult<GitDiffDocument>.Success success ? success.Value.RawPatch : ((GitResult<GitDiffDocument>.Failure)result).Error.Message;
        PresentWells();
    }
}
