using System.Security.Cryptography;
using System.Text;
using Asura.Core;

namespace Asura.Application;

/// <summary>Explicit remote execution target; never interpretable as a host process launch.</summary>
public sealed class KubernetesTerminalTarget
{
    public KubernetesTerminalTarget(KubernetesConnectionProfile profile, KubernetesExecRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Tty || request.Command.Count is < 1 or > 128
            || request.Command.Any(value => value is null || value.Length > 8192 || value.Contains('\0', StringComparison.Ordinal)))
        {
            throw new ArgumentException("A Kubernetes terminal requires a bounded TTY command.", nameof(request));
        }
        Profile = profile;
        Request = request with { Command = Array.AsReadOnly(request.Command.ToArray()) };
        var identity = string.Join('\0', new[]
        {
            profile.Id.Value, profile.ContextName, profile.KubeconfigPath ?? "", profile.ManagedKubeconfigSecret?.ToString() ?? "",
            profile.TrustedExecFingerprint ?? "", profile.TunnelConnectionId?.Value ?? "",
            request.Pod.Namespace ?? "", request.Pod.Name, request.Pod.Uid, request.Container,
        }.Concat(Request.Command));
        BindingFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public KubernetesConnectionProfile Profile { get; }
    public KubernetesExecRequest Request { get; }
    public string BindingFingerprint { get; }
    public override string ToString() => "Kubernetes terminal target [command redacted]";
}
