using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>Plans a typed pod transport; it never resolves a local executable.</summary>
internal sealed class KubernetesTerminalConnectionRuntime(KubernetesTerminalTarget target) : IConnectionRuntime
{
    public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(
        ConnectionProfile profile, IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launch = new TerminalLaunchRequest(workingDirectory: null,
            connectionMetadata: new("Kubernetes", null), kubernetesTarget: target);
        return ValueTask.FromResult(ConnectionRuntimeResult<ConnectionOpenPlan>.Succeed(new(
            profile.Id, profile.ConnectionKind, launch, ConnectionAuthenticationMode.None,
            SshHostKeyPolicy.NotApplicable, ConnectionReconnectMode.Manual)));
    }
    public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(
        ConnectionProfile profile, IProgress<ConnectionProgress>? progress, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ConnectionRuntimeResult<ConnectionTestReport>.Fail(
            ConnectionRuntimeError.Create(ConnectionRuntimeErrorCode.AdapterUnavailable)));
}
