using System.Reflection;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
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
                var secretDialog = Assert.Single(dialog.OwnedWindows.OfType<ConnectionSecretEditorDialog>());
                var draft = Assert.IsType<ConnectionSecretEditorViewModel>(secretDialog.DataContext);
                draft.Value = kind == SecretKind.Other
                    ? "{\"accessKeyId\":\"test-key\",\"secretAccessKey\":\"test-secret\"}"
                    : "secret-canary";
                var save = secretDialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Save credential"));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();

                Assert.False(secretDialog.IsVisible);
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
    public async Task Cancelling_secret_creation_clears_input_without_writing_to_the_vault()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            var vault = DispatchProxy.Create<ISecretVault, VaultProxy>();
            var draft = new ConnectionSecretEditorViewModel(vault,
                new SecretScope(SecretScopeKind.Connection, "draft"), SecretKind.Password, "Password")
            { Value = "secret-canary" };
            var dialog = new ConnectionSecretEditorDialog(draft);
            dialog.Show();
            await Idle();
            dialog.Close();
            Assert.Empty(draft.Value);
            Assert.Empty(((VaultProxy)(object)vault).CreateRequests);
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
