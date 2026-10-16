using Asura.Agent;

namespace Asura.Agent.Providers;

/// <summary>
/// Token counts as providers report them, made consistent. Usage is
/// bookkeeping: an answer is never discarded because its counts disagree.
/// ChatGPT's Codex backend reports output tokens without the reasoning that
/// came before them, so a turn that reasoned more than it wrote looked
/// impossible and failed as an invalid response.
/// </summary>
internal static class AiProviderUsage
{
    public static AgentTokenUsage Reconcile(long input, long output, long cached, long reasoning)
    {
        if (reasoning > output)
        {
            // The output count left the reasoning out; count both.
            output = Math.Min(AgentTokenUsage.MaximumTokenCount - input, output + reasoning);
            reasoning = Math.Min(reasoning, output);
        }

        return new AgentTokenUsage(input, output, Math.Min(cached, input), reasoning);
    }
}
