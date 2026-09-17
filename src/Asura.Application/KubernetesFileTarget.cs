using Asura.Core;

namespace Asura.Application;

/// <summary>A container file view keeps the original pod UID and profile, independently of its resource inspector.</summary>
public sealed record KubernetesFileTarget(KubernetesConnectionProfile Profile, KubernetesResourceReference Pod, string Container)
{
    public override string ToString() => $"Kubernetes files {Profile.Id.Value} {Pod.Uid}";
}
