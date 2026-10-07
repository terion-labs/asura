using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Asura.App.Views;

/// <summary>
/// The one editor for every saved connection family: terminal endpoints, file
/// providers, and database connections. Which family a definition belongs to
/// is part of the grouped connection-type selection.
///
/// Everything it needs is done inside it. A new credential is written in a
/// draft under the field that needs it, and the repository folder browser
/// opens under the Repository path field — neither is a second modal stacked
/// on this one. Only the SSH host-key review, an alert, sits on top.
/// </summary>
public sealed partial class ConnectionEditorDialog : DialogWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConnectionEditorDialogPurpose _purpose;
    private ConnectionCredentialDraftView? _openDraft;
    private Action<SecretMetadata>? _applyDraft;

    public ConnectionEditorDialog()
    {
        InitializeComponent();
    }

    public ConnectionEditorDialog(UnifiedConnectionEditorViewModel viewModel)
        : this(viewModel, ConnectionEditorDialogPurpose.Save)
    {
    }

    public ConnectionEditorDialog(
        UnifiedConnectionEditorViewModel viewModel,
        ConnectionEditorDialogPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _purpose = purpose;
        InitializeComponent();
        DataContext = viewModel;
        if (purpose == ConnectionEditorDialogPurpose.Connect)
        {
            if (this.FindControl<Border>("ConnectPurposeRow") is { } connectRow)
            {
                connectRow.IsVisible = true;
            }

            if (this.FindControl<Button>("SubmitButton") is { } submit)
            {
                submit.Content = "Connect";
            }
        }
    }

    private UnifiedConnectionEditorViewModel ViewModel =>
        DataContext as UnifiedConnectionEditorViewModel
        ?? throw new InvalidOperationException("The connection editor view model is unavailable.");

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Finish a vault write before the draft loses its owner.
        e.Cancel = _openDraft?.IsSaving == true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// Escape and ⌘. put away whatever is open in place first — a credential
    /// draft, the folder browser — and cancel the editor only when nothing is.
    /// </summary>
    protected override void Cancel()
    {
        if (_openDraft is { } draft)
        {
            _ = draft.TryCancel();
            return;
        }

        if (this.FindControl<Control>("RepositoryBrowser") is { IsVisible: true })
        {
            CloseRepositoryBrowser();
            return;
        }

        base.Cancel();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            await ViewModel.TestAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Closing the dialog owns cancellation of its in-flight test.
        }
    }

    private async void OnBrowseRepositoryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.Terminal.UsesSystemFolderPicker
            && StorageProvider is { CanPickFolder: true } storage)
        {
            if (await PickLocalFolderAsync(storage, ViewModel.Terminal.RepositoryPath) is { } chosen)
            {
                ViewModel.Terminal.RepositoryPath = chosen;
            }

            return;
        }

        GitRepositoryPickerViewModel picker;
        try
        {
            // The picker connects with the endpoint typed so far, so an SSH
            // repository browses on its host. Incomplete endpoint fields
            // surface the same way a save would show them.
            picker = ViewModel.Terminal.CreateRepositoryPicker();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            ShowValidationError(exception);
            return;
        }

        var browser = this.FindControl<GitRepositoryBrowserView>("RepositoryBrowserView")!;
        browser.DataContext = picker;
        this.FindControl<Control>("RepositoryBrowser")!.IsVisible = true;
        UpdateDefaultButton();
        browser.BringIntoView();
        await picker.LoadInitialAsync();
    }

    /// <summary>
    /// One folder from the system's Open panel, starting at
    /// <paramref name="startPath"/> when it names a folder that exists.
    /// </summary>
    private static async Task<string?> PickLocalFolderAsync(IStorageProvider storage, string? startPath)
    {
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
        {
            start = await storage.TryGetFolderFromPathAsync(startPath);
        }

        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose Repository",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    private void OnRepositoryChosen(object? sender, string path)
    {
        _ = sender;
        ViewModel.Terminal.RepositoryPath = path;
        CloseRepositoryBrowser();
    }

    private void OnUseRepositoryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (this.FindControl<GitRepositoryBrowserView>("RepositoryBrowserView")?.DataContext
                is GitRepositoryPickerViewModel picker
            && picker.ResolveSelection() is { } path)
        {
            OnRepositoryChosen(this, path);
        }
    }

    private void OnCloseRepositoryBrowserClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseRepositoryBrowser();
    }

    private void CloseRepositoryBrowser()
    {
        this.FindControl<Control>("RepositoryBrowser")!.IsVisible = false;
        this.FindControl<GitRepositoryBrowserView>("RepositoryBrowserView")!.DataContext = null;
        UpdateDefaultButton();
    }

    /// <summary>
    /// Return belongs to whatever is open in place: a credential draft stores
    /// the credential, the folder browser uses the selected folder, and only
    /// when neither is open does it save or connect.
    /// </summary>
    private void UpdateDefaultButton()
    {
        var browserOpen = this.FindControl<Control>("RepositoryBrowser")?.IsVisible == true;
        if (_openDraft is { } draft)
        {
            draft.IsSaveDefault = true;
        }

        this.FindControl<Button>("UseRepositoryButton")!.IsDefault = _openDraft is null && browserOpen;
        this.FindControl<Button>("SubmitButton")!.IsDefault = _openDraft is null && !browserOpen;
    }

    private async void OnTrustHostKeyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.Terminal.HostKeyReview is not { } review)
        {
            return;
        }

        var confirmed = await new SshHostKeyReviewDialog(review).ShowDialog<bool>(this);
        if (confirmed)
        {
            await ViewModel.Terminal.TrustHostKeyAsync(_lifetime.Token);
        }
    }

    private async void OnTrustDatabaseHostKeyClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Database is not { HostKeyReview: { } review } database)
        {
            return;
        }
        var confirmed = await new SshHostKeyReviewDialog(review).ShowDialog<bool>(this);
        if (confirmed)
        {
            await database.TrustHostKeyAsync(review.Id, _lifetime.Token);
        }
    }

    private async void OnTrustFileHostKeyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.Files is not { HostKeyReview: { } review } files)
        {
            return;
        }

        var confirmed = await new SshHostKeyReviewDialog(review).ShowDialog<bool>(this);
        if (confirmed)
        {
            await files.TrustHostKeyAsync(review.Id, _lifetime.Token);
        }
    }

    private void OnAddCredentialClick(object? sender, RoutedEventArgs e)
    {
        var kind = ViewModel.Terminal.Authentication == ConnectionAuthenticationChoice.PrivateKey
            ? SecretKind.PrivateKey : SecretKind.Password;
        OpenCredentialDraft(
            "TerminalCredentialDraft",
            kind,
            new SecretScope(SecretScopeKind.Connection, ViewModel.Terminal.Id.Value),
            ViewModel.Terminal.SelectCreatedCredential);
    }

    private void OnAddPassphraseClick(object? sender, RoutedEventArgs e) =>
        OpenCredentialDraft(
            "TerminalCredentialDraft",
            SecretKind.Passphrase,
            new SecretScope(SecretScopeKind.Connection, ViewModel.Terminal.Id.Value),
            ViewModel.Terminal.SelectCreatedCredential);

    private void OnAddFileCredentialClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Files is not { } files)
        {
            return;
        }

        OpenCredentialDraft(
            "FileCredentialDraft",
            files.IsS3 ? SecretKind.Other : SecretKind.Password,
            new SecretScope(SecretScopeKind.FileProvider, files.ProfileId),
            files.SelectCreatedCredential);
    }

    /// <summary>
    /// Opens a credential draft in place, under the field it is for. One at a
    /// time: opening another puts the first away. While it is open, Return
    /// stores the credential rather than saving the whole connection.
    /// </summary>
    private void OpenCredentialDraft(
        string host,
        SecretKind kind,
        SecretScope scope,
        Action<SecretMetadata> apply)
    {
        if (_openDraft is { } open && !open.TryCancel())
        {
            return;
        }

        ConnectionSecretEditorViewModel draft;
        try
        {
            draft = ViewModel.CreateSecretEditor(kind, scope);
        }
        catch (InvalidOperationException exception)
        {
            ShowValidationError(exception);
            return;
        }

        var view = this.FindControl<ConnectionCredentialDraftView>(host)!;
        view.DataContext = draft;
        view.Lifetime = _lifetime.Token;
        view.IsVisible = true;
        _openDraft = view;
        _applyDraft = apply;
        UpdateDefaultButton();
        view.BringIntoView();
        view.FocusFirstField();
    }

    private void OnCredentialDraftCreated(object? sender, SecretMetadata credential)
    {
        var apply = _applyDraft;
        CloseCredentialDraft();
        apply?.Invoke(credential);
    }

    private void OnCredentialDraftCancelled(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        CloseCredentialDraft();
    }

    private void CloseCredentialDraft()
    {
        if (_openDraft is not { } view)
        {
            return;
        }

        view.IsSaveDefault = false;
        view.IsVisible = false;
        view.DataContext = null;
        _openDraft = null;
        _applyDraft = null;
        UpdateDefaultButton();
    }

    private void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            var saveConnection = _purpose != ConnectionEditorDialogPurpose.Connect
            || !ViewModel.SupportsUnsavedConnect
            || this.FindControl<CheckBox>("SaveConnectionCheckBox")?.IsChecked == true;
            Close(ViewModel.CreateSaveResult(saveConnection));
        }
        catch (Exception exception) when (exception
            is ArgumentException or OverflowException or UriFormatException or FormatException)
        {
            ShowValidationError(exception);
        }
    }

    /// <summary>
    /// Under the field the failure names, focused; or, when it names none, in
    /// the error list at the top of the form, scrolled into view. The dialog
    /// stays open with everything typed so far.
    /// </summary>
    private void ShowValidationError(Exception exception)
    {
        if (FieldValidation.Present(this, exception))
        {
            return;
        }

        ViewModel.ErrorNotices.Report(exception.Message);
        this.FindControl<Control>("FormErrors")?.BringIntoView();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

public enum ConnectionEditorDialogPurpose
{
    Save,
    Connect,
}
