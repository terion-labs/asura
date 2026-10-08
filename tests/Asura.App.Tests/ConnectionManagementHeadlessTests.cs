using System.Reflection;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class ConnectionManagementHeadlessTests
{
    [Theory]
    [InlineData(SecretKind.Password)]
    [InlineData(SecretKind.PrivateKey)]
    [InlineData(SecretKind.Passphrase)]
    [InlineData(SecretKind.Other)]
    public async Task New_connection_creates_and_selects_its_own_credential(SecretKind kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var vault = DispatchProxy.Create<ISecretVault, VaultProxy>();
            var recorded = (VaultProxy)(object)vault;
            var terminal = new ConnectionEditorViewModel(DispatchProxy.Create<IConnectionRuntime, UnusedRuntimeProxy>());
            var files = new FileProviderProfileEditorViewModel(
                DispatchProxy.Create<IFileProviderProfileRuntime, UnusedRuntimeProxy>(), [], []);
            var editor = new UnifiedConnectionEditorViewModel(terminal, files, null, secretVault: vault);
            var fileCredential = kind == SecretKind.Other;
            editor.SelectedType = editor.TypeOptions.Single(option => fileCredential
                ? option.FileKind == FileProviderKind.S3
                : option.TerminalKind == ConnectionKind.Ssh && !option.GitRepository);
            editor.Name = "New connection";
            terminal.Host = "example.test";
            terminal.Authentication = kind == SecretKind.Password
                ? ConnectionAuthenticationChoice.Password : ConnectionAuthenticationChoice.PrivateKey;
            files.BucketName = "test-bucket";
            var dialog = new ConnectionEditorDialog(editor);
            try
            {
                dialog.Show();
                await Idle();
                var buttonName = kind switch
                {
                    SecretKind.Passphrase => "Add private key passphrase",
                    SecretKind.Other => "Add file connection credential",
                    _ => "Add connection credential",
                };
                var add = FindButton(dialog, buttonName);
                Assert.True(add.IsEffectivelyVisible);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();
                // The credential is written in place, under its field; no
                // second window opens over the editor.
                Assert.Empty(dialog.OwnedWindows);
                var draftView = Assert.Single(
                    dialog.GetVisualDescendants().OfType<ConnectionCredentialDraftView>(),
                    candidate => candidate.IsEffectivelyVisible);
                var draft = Assert.IsType<ConnectionSecretEditorViewModel>(draftView.DataContext);
                draft.Value = kind == SecretKind.Other
                    ? "{\"accessKeyId\":\"test-key\",\"secretAccessKey\":\"test-secret\"}"
                    : "secret-canary";
                var save = draftView.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, PlatformTerms.SaveInSecretStore));
                Assert.True(save.IsDefault);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();

                Assert.False(draftView.IsVisible);
                Assert.Empty(draft.Value);
                Assert.True(recorded.Material!.IsDisposed);
                var request = Assert.Single(recorded.CreateRequests);
                Assert.Equal(kind, request.Kind);
                Assert.Equal(new SecretScope(fileCredential ? SecretScopeKind.FileProvider : SecretScopeKind.Connection,
                    fileCredential ? files.ProfileId : terminal.Id.Value), request.Scope);
                Assert.Equal(SecretUseKind.UserManagement, request.Purpose.Kind);
                Assert.Equal(request.Scope.OwnerId, request.Purpose.TargetId);
                if (fileCredential)
                {
                    Assert.Equal(request.Reference, files.SelectedCredential!.Reference);
                    Assert.Contains(files.SelectedCredential, files.SecretOptions);
                    var profile = files.CreateSaveRequest().Profile;
                    Assert.Equal(request.Scope.OwnerId, profile.Id.Value);
                    Assert.Equal(request.Reference, Assert.IsType<FileProviderConfiguration.S3>(profile.Configuration).CredentialsSecret);
                }
                else
                {
                    Assert.Equal(request.Reference.Value, kind == SecretKind.Passphrase
                        ? terminal.PassphraseSecretReference : terminal.SecretReference);
                    var picker = dialog.GetVisualDescendants().OfType<ComboBox>()
                        .Single(control => string.Equals(AutomationProperties.GetName(control),
                            kind == SecretKind.Passphrase ? "Private key passphrase" : "Connection credential", StringComparison.Ordinal));
                    var selected = Assert.IsType<ConnectionCredentialOption>(picker.SelectedItem);
                    Assert.Equal(request.Reference, selected.Reference);
                    Assert.Equal(request.Label, selected.DisplayName);
                    if (kind == SecretKind.Passphrase)
                    {
                        terminal.SecretReference = "existing-private-key";
                    }
                    Assert.Equal(request.Scope.OwnerId, terminal.CreateSaveRequest().Profile.Id.Value);
                }
            }
            finally
            {
                dialog.Close();
            }
            return true;
        }, timeout.Token);
    }

    [Fact]
    public async Task Credential_selector_loads_saved_names_and_updates_the_saved_reference()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var runtime = DispatchProxy.Create<IConnectionRuntime, UnusedRuntimeProxy>();
            var original = new ConnectionEditorViewModel(runtime)
            {
                Name = "SSH",
                Kind = ConnectionKind.Ssh,
                Host = "example.test",
                Authentication = ConnectionAuthenticationChoice.Password,
                SecretReference = "first",
            }.CreateSaveRequest().Profile;
            var scope = new SecretScope(SecretScopeKind.Connection, original.Id.Value);
            var terminal = new ConnectionEditorViewModel(runtime, original, secrets:
            [
                new(new SecretRef("first"), "First password", "Password", "", "", "", scope, "", 0),
                new(new SecretRef("second"), "Second password", "Password", "", "", "", scope, "", 0),
            ]);
            var dialog = new ConnectionEditorDialog(new UnifiedConnectionEditorViewModel(terminal, null, null));
            try
            {
                dialog.Show();
                await Idle();
                var picker = dialog.GetVisualDescendants().OfType<ComboBox>()
                    .Single(control => string.Equals(AutomationProperties.GetName(control), "Connection credential", StringComparison.Ordinal));
                Assert.True(picker.IsEffectivelyVisible);
                Assert.Equal("First password", Assert.IsType<ConnectionCredentialOption>(picker.SelectedItem).DisplayName);
                picker.SelectedItem = terminal.CredentialOptions.Single(option => string.Equals(option.DisplayName, "Second password", StringComparison.Ordinal));
                await Idle();
                Assert.Equal(new SecretRef("second"),
                    Assert.IsType<ConnectionAuthentication.Password>(terminal.CreateSaveRequest().Profile.Authentication).PasswordSecret);
                terminal.Authentication = ConnectionAuthenticationChoice.PrivateKey;
                await Idle();
                Assert.Null(Assert.IsType<ConnectionCredentialOption>(picker.SelectedItem).Reference);
                Assert.DoesNotContain(terminal.CredentialOptions, option => string.Equals(option.DisplayName, "Second password", StringComparison.Ordinal));
            }
            finally
            {
                dialog.Close();
            }
            return true;
        }, timeout.Token);
    }

    [Fact]
    public async Task Cancelling_secret_creation_clears_input_without_writing_to_the_vault()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var vault = DispatchProxy.Create<ISecretVault, VaultProxy>();
            var terminal = new ConnectionEditorViewModel(DispatchProxy.Create<IConnectionRuntime, UnusedRuntimeProxy>());
            var editor = new UnifiedConnectionEditorViewModel(terminal, null, null, secretVault: vault);
            editor.SelectedType = editor.TypeOptions.Single(option =>
                option.TerminalKind == ConnectionKind.Ssh && !option.GitRepository);
            terminal.Authentication = ConnectionAuthenticationChoice.Password;
            var dialog = new ConnectionEditorDialog(editor);
            try
            {
                dialog.Show();
                await Idle();
                FindButton(dialog, "Add connection credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();
                var draftView = Assert.Single(
                    dialog.GetVisualDescendants().OfType<ConnectionCredentialDraftView>(),
                    candidate => candidate.IsEffectivelyVisible);
                var draft = Assert.IsType<ConnectionSecretEditorViewModel>(draftView.DataContext);
                draft.Value = "secret-canary";
                FindButton(draftView, "Cancel the new credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();

                // Folded away, nothing typed left behind, nothing written, and
                // the editor itself still open with Return back on its Save.
                Assert.False(draftView.IsVisible);
                Assert.Empty(draft.Value);
                Assert.Empty(((VaultProxy)(object)vault).CreateRequests);
                Assert.True(dialog.IsVisible);
                Assert.True(dialog.FindControl<Button>("SubmitButton")!.IsDefault);
            }
            finally
            {
                dialog.Close();
            }

            return true;
        }, timeout.Token);
    }

    [Fact]
    public async Task Return_in_the_folder_browser_chooses_the_folder_instead_of_saving()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var terminal = new ConnectionEditorViewModel(
                DispatchProxy.Create<IConnectionRuntime, UnusedRuntimeProxy>(),
                gitClient: new FakeGitRepositoryClient());
            var editor = new UnifiedConnectionEditorViewModel(terminal, null, null);
            editor.SelectedType = editor.TypeOptions.Single(option =>
                option.TerminalKind == ConnectionKind.Local && option.GitRepository);
            editor.Name = "Asura repo";
            var dialog = new ConnectionEditorDialog(editor);
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            try
            {
                dialog.Show();
                await Idle();
                dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content is "Browse…")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();

                var submit = dialog.FindControl<Button>("SubmitButton")!;
                var use = dialog.FindControl<Button>("UseRepositoryButton")!;
                Assert.False(submit.IsDefault);
                Assert.True(use.IsDefault);

                var folders = dialog.FindControl<GitRepositoryBrowserView>("RepositoryBrowserView")!
                    .GetVisualDescendants().OfType<ListBox>().Single();
                var picker = Assert.IsType<GitRepositoryPickerViewModel>(folders.DataContext);
                picker.SelectedDirectory = picker.Directories.Single(entry => entry.IsRepository);
                folders.Focus();
                await Idle();

                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Idle();

                // Return used the folder; it did not save or close the editor,
                // and with the browser put away Return is the editor's again.
                Assert.False(closed);
                Assert.EndsWith("asura", terminal.RepositoryPath, StringComparison.Ordinal);
                Assert.False(dialog.FindControl<Control>("RepositoryBrowser")!.IsVisible);
                Assert.True(submit.IsDefault);
                Assert.False(use.IsDefault);
            }
            finally
            {
                dialog.Close();
            }

            return true;
        }, timeout.Token);
    }

    [Fact]
    public async Task Unavailable_connection_has_a_visible_working_edit_action()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var target = new PanelConnectionOptionViewModel.Target.FileProvider(new FileProviderProfileId("missing"));
            var shortcut = new SavedConnectionShortcutViewModel(target, "Unavailable files", "S3", "bucket", false,
                new SavedConnectionLaunchViewModel(target, PanelKind.FileViewer, "Open files", FluentIcons.Common.Symbol.Folder), []);
            var view = new SavedConnectionShortcutView { DataContext = shortcut };
            SavedConnectionShortcutViewModel? edited = null;
            view.EditRequested += (_, value) => edited = value;
            var window = new Window { Content = view, Width = 600, Height = 200 };
            try
            {
                window.Show();
                await Idle();
                var edit = FindButton(view, "Edit Unavailable files");
                Assert.True(edit.IsEffectivelyVisible);
                Assert.True(edit.IsEffectivelyEnabled);
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(shortcut, edited);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, timeout.Token);
    }

    [Fact]
    public async Task Vault_failure_clears_material_and_does_not_expose_exception_values()
    {
        var vault = DispatchProxy.Create<ISecretVault, VaultProxy>();
        var proxy = (VaultProxy)(object)vault;
        proxy.Fail = true;
        var draft = new ConnectionSecretEditorViewModel(vault,
            new SecretScope(SecretScopeKind.Connection, "draft"), SecretKind.Password, "Password")
        { Value = "secret-canary" };
        Assert.Null(await draft.SaveAsync(CancellationToken.None));
        Assert.Empty(draft.Value);
        Assert.True(proxy.Material!.IsDisposed);
        Assert.DoesNotContain("secret-canary", draft.Error, StringComparison.Ordinal);
        Assert.False(draft.IsSaving);
    }

    private static Button FindButton(Control root, string name) => root.GetVisualDescendants().OfType<Button>()
        .Single(button => string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal));

    private static Task Idle() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();

    public class UnusedRuntimeProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }

    public class VaultProxy : NetworkSettingsViewModelTests.RecordingVaultProxy
    {
        public SecretMaterial? Material { get; private set; }
        public bool Fail { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (args is [CreateSecretRequest, SecretMaterial material, CancellationToken])
            {
                Material = material;
                if (Fail)
                {
                    throw new InvalidOperationException("secret-canary");
                }
            }
            return base.Invoke(targetMethod, args);
        }
    }
}
