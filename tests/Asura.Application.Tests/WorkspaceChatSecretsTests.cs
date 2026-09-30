using Asura.Application;
using Asura.Core;
using Asura.Tests;

namespace Asura.Application.Tests;

public sealed class WorkspaceChatSecretsTests
{
    [Fact]
    public async Task InFlightDisclosureCannotReviveDisposedWorkspaceCache()
    {
        using var vault = new ChatSecretTestVault();
        var secrets = new WorkspaceChatSecrets(vault, new("disposed-workspace"));
        var reference = Assert.Single(secrets.Protect("password=dispose-fixture").References);
        Assert.True(await secrets.FlushAsync(reference.Id, default));
        vault.ResolveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vault.ReleaseResolve = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolving = secrets.ResolveTextAsync(reference, SecretUseKind.ChatModelDisclosure, default).AsTask();
        await vault.ResolveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        secrets.Dispose();
        vault.ReleaseResolve.TrySetResult();
        Assert.IsType<SecretVaultResult<string>.Failure>(await resolving.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task InFlightVaultWriteOwnsItsBufferDuringWorkspaceDisposal()
    {
        using var vault = new ChatSecretTestVault
        {
            CreateEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseCreate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var scope = new AgentConversationScopeId("race-workspace");
        var secrets = new WorkspaceChatSecrets(vault, scope);
        var reference = Assert.Single(secrets.Protect("password=race-fixture-value").References);
        var flushing = secrets.FlushAsync(reference.Id, default).AsTask();
        await vault.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        secrets.Dispose();
        vault.ReleaseCreate.TrySetResult();
        Assert.True(await flushing.WaitAsync(TimeSpan.FromSeconds(5)));
        using var restored = new WorkspaceChatSecrets(vault, scope);
        using var material = Assert.IsType<SecretVaultResult<SecretMaterial>.Success>(await restored.ResolveAsync(reference, SecretUseKind.ChatLocalReveal, default)).Value;
        Assert.Equal(27, material.Length);
    }

    [Fact]
    public async Task DisclosedMultiwordValuesAreHiddenAcrossStreamingAndInsideLongerWords()
    {
        using var vault = new ChatSecretTestVault();
        using var secrets = new WorkspaceChatSecrets(vault, new("stream-workspace"));
        var reference = Assert.Single(secrets.Protect("password='first second'").References);
        Assert.True(await secrets.CanResolveAsync(reference, default));
        Assert.DoesNotContain("first", secrets.MaskStreaming("Reply first "), StringComparison.Ordinal);
        Assert.DoesNotContain("first second", secrets.Protect("prefixfirst secondsuffix").Text, StringComparison.Ordinal);
        var encoded = System.Text.Json.JsonEncodedText.Encode(reference.Placeholder).ToString();
        Assert.Equal(encoded, secrets.Protect(encoded).Text, StringComparer.Ordinal);
    }

    [Fact]
    public async Task InternalEntriesCannotBeManagedAsUserCredentialsOrResolvedAcrossWorkspaces()
    {
        using var vault = new ChatSecretTestVault();
        using var first = new WorkspaceChatSecrets(vault, new("first-workspace"));
        var reference = Assert.Single(first.Protect("token=fixture-original").References);
        Assert.True(await first.FlushAsync(reference.Id, default));
        using var second = new WorkspaceChatSecrets(vault, new("second-workspace"));
        Assert.IsType<SecretVaultResult<SecretMaterial>.Failure>(await second.ResolveAsync(reference, SecretUseKind.ChatLocalReveal, default));
        Assert.IsType<SecretVaultResult<Unit>.Failure>(await vault.DeleteAsync(new(new SecretRef(reference.Id),
            new SecretScope(SecretScopeKind.WorkspaceChat, "first-workspace"), new SecretUsePurpose(SecretUseKind.UserManagement, "first-workspace")), default));
    }
}
