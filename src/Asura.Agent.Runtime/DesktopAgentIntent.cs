using Asura.Application;

namespace Asura.Agent.Runtime;

/// <summary>
/// A validated <c>desktop.*</c> tool call before it is bound to a trusted
/// target. A model can name an element or a window only by an identifier it
/// was shown; the process, window and element behind that identifier come
/// from the run's observation ledger, never from the model.
/// </summary>
internal abstract record DesktopAgentIntent
{
    private DesktopAgentIntent()
    {
    }

    public abstract string ToolName { get; }

    /// <summary>
    /// A call whose whole target the model may name itself: an observation of
    /// an application by name, or <c>desktop.launch_app</c>, which starts an
    /// application that may not be running yet and so has nothing to observe.
    /// </summary>
    public sealed record Direct(AgentDesktopRequest Request) : DesktopAgentIntent
    {
        public override string ToolName => Request.ToolName;
    }

    /// <summary>
    /// <c>desktop.close_app</c> on one running application the run observed.
    /// The name is only a lookup key: the process, its generation token and
    /// its identity come from the ledger.
    /// </summary>
    public sealed record OnApplication(AgentDesktopApplicationId Application) : DesktopAgentIntent
    {
        public override string ToolName => DesktopAgentToolNames.CloseApp;
    }

    /// <summary>
    /// A call on one observed element. <paramref name="Bind"/> completes the
    /// request once the ledger supplies the element the ref was minted for.
    /// </summary>
    /// <param name="RefusedOnSecureField">
    /// True when the call types into, sets, clears or sends a key to the
    /// element, none of which a secure field accepts from the agent.
    /// </param>
    public sealed record OnElement(
        string Tool,
        AgentDesktopElementRef Reference,
        bool RefusedOnSecureField,
        Func<AgentDesktopElementTarget, AgentDesktopRequest> Bind) : DesktopAgentIntent
    {
        public override string ToolName => Tool;
    }

    /// <summary>A call on one window the run listed.</summary>
    public sealed record OnWindow(string WindowId) : DesktopAgentIntent
    {
        public override string ToolName => DesktopAgentToolNames.FocusWindow;
    }
}

internal abstract record DesktopAgentIntentResult
{
    private DesktopAgentIntentResult()
    {
    }

    public sealed record Parsed(DesktopAgentIntent Intent) : DesktopAgentIntentResult;

    public sealed record Rejected(string StableCode) : DesktopAgentIntentResult;
}
