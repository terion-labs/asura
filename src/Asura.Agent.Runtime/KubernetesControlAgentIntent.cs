using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

internal abstract record KubernetesControlAgentIntentResult
{
    private KubernetesControlAgentIntentResult()
    {
    }

    public sealed record Parsed(
        PanelInstanceId PanelId,
        AgentKubernetesControlIntent Request) : KubernetesControlAgentIntentResult;

    public sealed record Rejected(string StableCode, string Message)
        : KubernetesControlAgentIntentResult;
}
