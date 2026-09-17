namespace Asura.Application;

/// <summary>Optional native authority to expose an owned local relay through one workspace route.</summary>
public interface IWorkspacePrivateEndpointRegistrar
{
    /// <summary>
    /// Register only after opening the relay. Capture the expected route before opening it;
    /// a changed or blocked route rejects registration. The alias is live, never durable.
    /// </summary>
    IWorkspacePrivateEndpointLease RegisterLoopbackEndpoint(int localPort, CancellationToken expectedRouteLifetime);
}

public interface IWorkspacePrivateEndpointLease : IAsyncDisposable
{
    string Host { get; }
    int Port { get; }
    CancellationToken Lifetime { get; }
}

/// <summary>Reserved transient aliases must never be treated as durable navigation targets.</summary>
public static class WorkspacePrivateEndpointAddress
{
    public const string Suffix = ".asura-forward.invalid";

    public static bool IsReservedHost(string host) =>
        host.TrimEnd('.').EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

    public static BrowserAddress ForPersistence(BrowserAddress address) =>
        IsReservedHost(address.Value.Host) ? BrowserAddress.Blank : address;
}
