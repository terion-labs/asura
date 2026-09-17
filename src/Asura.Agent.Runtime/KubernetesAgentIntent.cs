using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

internal abstract record KubernetesAgentIntentResult
{
    private KubernetesAgentIntentResult()
    {
    }

    public sealed record Parsed(
        PanelInstanceId PanelId,
        AgentKubernetesReadRequest Request) : KubernetesAgentIntentResult;

    public sealed record Rejected(string StableCode, string Message)
        : KubernetesAgentIntentResult;
}
