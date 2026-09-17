using System.Security.Cryptography;
using System.Text;
using Asura.Application;
using Asura.Core;

namespace Asura.ConnectionBackend;

internal sealed class KubernetesWorkspaceSessionFactory(
    Func<ConnectionProfile?, CancellationToken, Task<DatabaseWorkspaceOperationLaunch>> launch,
    IDefinitionCatalog catalog,
    ISecretVault vault) : IKubernetesPanelSessionFactory
{
    public async ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
        await OpenWorkerAsync(profile, cancellationToken).ConfigureAwait(false);

    public async ValueTask<KubernetesConfigurationReview> ReviewAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var managed = await ResolveManagedAsync(profile, cancellationToken).ConfigureAwait(false);
        ConnectionProfile? hop = null;
        if (profile.TunnelConnectionId is { } hopId)
        {
            hop = catalog.Snapshot.Connections.FirstOrDefault(item => item.Value.Id == hopId)?.Value
                ?? throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The SSH hop is unavailable.");
        }
        var owned = await launch(hop, cancellationToken).ConfigureAwait(false);
        KubernetesWorkspaceSession? session = null;
        try
        {
            session = new(owned, watchToken => OpenWorkerAsync(profile, watchToken));
            return await session.ReviewAsync(new(profile.ContextName, profile.DefaultNamespace, profile.KubeconfigPath,
                managed, profile.TrustedExecFingerprint), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (session is null) { await owned.CleanupAsync().ConfigureAwait(false); }
            else { await session.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private async Task<KubernetesWorkspaceSession> OpenWorkerAsync(KubernetesConnectionProfile profile, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsEnabled || !profile.Validate().IsValid)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration,
                "Complete and enable the Kubernetes profile before connecting.");
        }
        ConnectionProfile? hop = null;
        if (profile.TunnelConnectionId is { } hopId)
        {
            hop = catalog.Snapshot.Connections.FirstOrDefault(item => item.Value.Id == hopId)?.Value
                ?? throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration,
                    "The Kubernetes profile's SSH connection is unavailable.");
        }
        var managed = await ResolveManagedAsync(profile, token).ConfigureAwait(false);
        var owned = await launch(hop, token).ConfigureAwait(false);
        KubernetesWorkspaceSession? session = null;
        try
        {
            session = new(owned, watchToken => OpenWorkerAsync(profile, watchToken));
            await session.OpenAsync(new(profile.ContextName, profile.DefaultNamespace, profile.KubeconfigPath,
                managed, profile.TrustedExecFingerprint), token).ConfigureAwait(false);
            return session;
        }
        catch
        {
            if (session is null) { await owned.CleanupAsync().ConfigureAwait(false); }
            else { await session.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private async Task<string?> ResolveManagedAsync(KubernetesConnectionProfile profile, CancellationToken token)
    {
        if (profile.ManagedKubeconfigSecret is not { } reference) { return null; }
        var result = await vault.ResolveAsync(new(reference,
            new(SecretScopeKind.KubernetesConnection, profile.Id.Value),
            new(SecretUseKind.KubernetesConnectionAuthentication, profile.Id.Value)), token).ConfigureAwait(false);
        if (result is not SecretVaultResult<SecretMaterial>.Success success)
        {
            throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration,
                "Unlock or repair the Kubernetes configuration in the secret vault.");
        }
        using var material = success.Value;
        var bytes = new byte[material.Length];
        try
        {
            material.CopyTo(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
