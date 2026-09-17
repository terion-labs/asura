namespace Asura.Application;

public sealed record KubernetesExecRequest(
    KubernetesResourceReference Pod,
    string Container,
    IReadOnlyList<string> Command,
    bool Tty = true)
{
    public override string ToString() => "Kubernetes exec request [command redacted]";
}

/// <summary>Worker-owned terminal channels. Read output/error concurrently; only asynchronous stream I/O is supported.</summary>
public interface IKubernetesExecSession : IAsyncDisposable
{
    Stream StandardInput { get; }

    Stream StandardOutput { get; }

    Stream StandardError { get; }

    ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken);

    ValueTask CompleteInputAsync(CancellationToken cancellationToken);

    ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken);
}

public sealed record KubernetesPortForwardRequest(
    KubernetesResourceReference Pod,
    int RemotePort,
    int LocalPort = 0);

/// <summary>Loopback listener in the owning worker's environment, not necessarily on the desktop host.</summary>
public interface IKubernetesPortForward : IAsyncDisposable
{
    int LocalPort { get; }

    int RemotePort { get; }

    Task Completion { get; }
}
