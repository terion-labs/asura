using System.ComponentModel;
using System.Reflection;
using Asura.Application;
using Asura.ConnectionBackend;
using Asura.Core;
using Asura.Infrastructure;

namespace Asura.Architecture.Tests;

public sealed class KubernetesWorkspaceSessionFactoryTests
{
    [Theory]
    [InlineData("io")]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    [InlineData("process")]
    public async Task BackendStartupFailureIsSafeAndRetryableForOpenAndReview(string kind)
    {
        Exception failure = kind switch
        {
            "io" => new IOException("private-runtime-detail"),
            "http" => new HttpRequestException("private-runtime-detail"),
            "timeout" => new TimeoutException("private-runtime-detail"),
            "cancelled" => new OperationCanceledException("private-runtime-detail"),
            _ => new Win32Exception("private-runtime-detail"),
        };
        using var vault = new InMemorySecretVault();
        var factory = new KubernetesWorkspaceSessionFactory((_, _) => Task.FromException<DatabaseWorkspaceOperationLaunch>(failure),
            DispatchProxy.Create<IDefinitionCatalog, UnusedCatalog>(), vault);
        var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context", "default");
        var openError = await Assert.ThrowsAsync<KubernetesRequestException>(() => factory.OpenAsync(profile, CancellationToken.None).AsTask());
        var reviewError = await Assert.ThrowsAsync<KubernetesRequestException>(() => factory.ReviewAsync(profile, CancellationToken.None).AsTask());
        foreach (var error in new[] { openError, reviewError })
        {
            Assert.Equal(KubernetesErrorCode.ConnectionFailed, error.Code);
            Assert.True(error.Retryable);
            Assert.Contains("retry", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private-runtime-detail", error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UserCancellationRemainsCancellation()
    {
        using var vault = new InMemorySecretVault();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var factory = new KubernetesWorkspaceSessionFactory((_, token) => Task.FromCanceled<DatabaseWorkspaceOperationLaunch>(token),
            DispatchProxy.Create<IDefinitionCatalog, UnusedCatalog>(), vault);
        var profile = new KubernetesConnectionProfile(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context", "default");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.OpenAsync(profile, cancelled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.ReviewAsync(profile, cancelled.Token).AsTask());
    }

    public class UnusedCatalog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A profile without an SSH hop must not read the catalog.");
    }
}
