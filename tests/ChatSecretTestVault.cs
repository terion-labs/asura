using Asura.Application;
using Asura.Core;

namespace Asura.Tests;

/// <summary>Deterministic persistent-vault contract fake; never uses the developer's OS vault.</summary>
internal sealed class ChatSecretTestVault : ISecretVault
{
    private readonly Dictionary<SecretRef, (SecretMetadata Metadata, SecretMaterial Material)> _entries = [];
    public bool FailCreate { get; set; }
    public bool FailResolve { get; set; }
    public int Count => _entries.Count;
    public TaskCompletionSource? CreateEntered { get; set; }
    public TaskCompletionSource? ReleaseCreate { get; set; }
    public TaskCompletionSource? ResolveEntered { get; set; }
    public TaskCompletionSource? ReleaseResolve { get; set; }
    public SecretVaultAvailability Availability { get; } = new(SecretVaultAvailabilityState.Available,
        SecretVaultPersistenceKind.OsProtectedPersistent, SecretVaultCapabilities.All, "test", "test", "Test vault");

    public async ValueTask<SecretVaultResult<SecretMetadata>> CreateAsync(CreateSecretRequest request, SecretMaterial material, CancellationToken cancellationToken)
    {
        if (CreateEntered is { } entered && ReleaseCreate is { } release)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
        if (!Allowed(SecretVaultOperation.Create, request.Scope, request.Purpose))
        {
            return Fail<SecretMetadata>(SecretVaultErrorCode.AccessDenied);
        }
        if (FailCreate)
        {
            return Fail<SecretMetadata>(SecretVaultErrorCode.Unavailable);
        }
        if (_entries.ContainsKey(request.Reference))
        {
            return Fail<SecretMetadata>(SecretVaultErrorCode.AlreadyExists);
        }
        var metadata = new SecretMetadata(request.Reference, request.Label, request.Kind, request.Scope,
            SecretVaultPersistenceKind.OsProtectedPersistent, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _entries.Add(request.Reference, (metadata, material.Clone()));
        return SecretVaultResult<SecretMetadata>.Succeed(metadata);
    }

    public async ValueTask<SecretVaultResult<SecretMaterial>> ResolveAsync(ResolveSecretRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ResolveEntered is { } entered && ReleaseResolve is { } release)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
        var result = !Allowed(SecretVaultOperation.Resolve, request.Scope, request.Purpose)
            ? Fail<SecretMaterial>(SecretVaultErrorCode.AccessDenied)
            : FailResolve ? Fail<SecretMaterial>(SecretVaultErrorCode.Unavailable)
            : _entries.TryGetValue(request.Reference, out var entry) && entry.Metadata.Scope == request.Scope
                ? SecretVaultResult<SecretMaterial>.Succeed(entry.Material.Clone()) : Fail<SecretMaterial>(SecretVaultErrorCode.NotFound);
        return result;
    }

    public ValueTask<SecretVaultResult<SecretMetadata>> GetMetadataAsync(GetSecretMetadataRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Allowed(SecretVaultOperation.GetMetadata, request.Scope, request.Purpose)
            && _entries.TryGetValue(request.Reference, out var entry) && entry.Metadata.Scope == request.Scope
                ? SecretVaultResult<SecretMetadata>.Succeed(entry.Metadata) : Fail<SecretMetadata>(SecretVaultErrorCode.NotFound));

    public ValueTask<SecretVaultResult<IReadOnlyList<SecretMetadata>>> ListMetadataAsync(ListSecretMetadataRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Allowed(SecretVaultOperation.ListMetadata, request.Scope, request.Purpose)
            ? SecretVaultResult<IReadOnlyList<SecretMetadata>>.Succeed([.. _entries.Values.Where(entry => request.Scope is null || entry.Metadata.Scope == request.Scope).Select(entry => entry.Metadata)])
            : Fail<IReadOnlyList<SecretMetadata>>(SecretVaultErrorCode.AccessDenied));

    public ValueTask<SecretVaultResult<Unit>> DeleteAsync(DeleteSecretRequest request, CancellationToken cancellationToken)
    {
        if (!Allowed(SecretVaultOperation.Delete, request.Scope, request.Purpose)
            || !_entries.TryGetValue(request.Reference, out var entry) || entry.Metadata.Scope != request.Scope)
        {
            return ValueTask.FromResult(Fail<Unit>(SecretVaultErrorCode.AccessDenied));
        }
        entry.Material.Dispose();
        _entries.Remove(request.Reference);
        return ValueTask.FromResult(SecretVaultResult<Unit>.Succeed(Unit.Value));
    }

    public ValueTask<SecretVaultResult<SecretMetadata>> ReplaceAsync(ReplaceSecretRequest request, SecretMaterial material, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<SecretVaultResult<SecretMetadata>> RelabelAsync(RelabelSecretRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    private static bool Allowed(SecretVaultOperation operation, SecretScope? scope, SecretUsePurpose purpose) => SecretScopeAccessPolicy.Default.IsAllowed(operation, scope, purpose);
    private static SecretVaultResult<T> Fail<T>(SecretVaultErrorCode code) => SecretVaultResult<T>.Fail(SecretVaultError.Create(code));
    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Material.Dispose();
        }
        _entries.Clear();
    }
}
