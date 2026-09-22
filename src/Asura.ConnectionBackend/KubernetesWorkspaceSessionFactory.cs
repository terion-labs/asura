using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;
using Asura.Kubernetes;

namespace Asura.ConnectionBackend;

/// <summary>Authentication placement follows workspace isolation; transport placement follows the route. See ADR 0059.</summary>
internal sealed class KubernetesWorkspaceSessionFactory(
    Func<ConnectionProfile?, CancellationToken, Task<DatabaseWorkspaceOperationLaunch>> launch,
    IDefinitionCatalog catalog,
    ISecretVault vault,
    bool useHostCredentials = false) : IKubernetesPanelSessionFactory
{
    public async ValueTask<IKubernetesClientSession> OpenAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken) =>
        await OpenWorkerAsync(profile, cancellationToken).ConfigureAwait(false);

    public async ValueTask<KubernetesConfigurationReview> ReviewAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken)
    {
        try { return await ReviewWorkerAsync(profile, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsStartupFailure(exception)) { throw StartupFailure(exception); }
    }

    private async Task<KubernetesConfigurationReview> ReviewWorkerAsync(KubernetesConnectionProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var managed = await ResolveManagedAsync(profile, cancellationToken).ConfigureAwait(false);
        if (useHostCredentials)
        {
            return await KubernetesWorkspaceConfiguration.ReviewAsync(new(profile.ContextName, profile.DefaultNamespace,
                profile.KubeconfigPath, managed, profile.TrustedExecFingerprint), cancellationToken).ConfigureAwait(false);
        }
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
        try { return await StartWorkerAsync(profile, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (IsStartupFailure(exception)) { throw StartupFailure(exception); }
    }

    private static bool IsStartupFailure(Exception exception) => exception is
        IOException or HttpRequestException or Win32Exception or TimeoutException or OperationCanceledException or NotSupportedException;

    private static KubernetesRequestException StartupFailure(Exception exception) =>
        new(KubernetesErrorCode.ConnectionFailed, exception is TimeoutException or OperationCanceledException
            ? "The Kubernetes backend timed out while starting. Check the workspace network connection, then retry."
            : "The Kubernetes backend could not start in this workspace. Check the workspace network connection and runtime, then retry.",
            retryable: true);

    private async Task<KubernetesWorkspaceSession> StartWorkerAsync(KubernetesConnectionProfile profile, CancellationToken token)
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
        var configuration = new KubernetesWorkspaceOpen(profile.ContextName, profile.DefaultNamespace,
            profile.KubeconfigPath, managed, profile.TrustedExecFingerprint);
        KubernetesCredentialRefresh? hostCredentials = null;
        if (useHostCredentials)
        {
            var plans = await KubernetesWorkspaceConfiguration.ReadAsync(configuration, token).ConfigureAwait(false);
            var plan = plans.SingleOrDefault(item => string.Equals(item.ContextName, profile.ContextName, StringComparison.Ordinal))
                ?? throw new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The selected Kubernetes context is unavailable.");
            var resolver = new KubernetesCredentialResolver(plan with { Connection = plan.Connection with { Namespace = profile.DefaultNamespace } },
                profile.TrustedExecFingerprint, new PathConnectionExecutableLocator().Find);
            hostCredentials = resolver.ResolveAsync;
        }
        var owned = await launch(hop, token).ConfigureAwait(false);
        KubernetesWorkspaceSession? session = null;
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, owned.Lifetime);
            if (hostCredentials is not null)
            {
                var connection = await hostCredentials(startup.Token).ConfigureAwait(false);
                // Keep host paths and executable authority out of the routed worker.
                configuration = new(profile.ContextName, profile.DefaultNamespace, null, null, null, connection);
            }
            session = new(owned, watchToken => OpenWorkerAsync(profile, watchToken), hostCredentials);
            await session.OpenAsync(configuration, startup.Token).ConfigureAwait(false);
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
