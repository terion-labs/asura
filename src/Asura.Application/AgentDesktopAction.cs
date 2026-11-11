namespace Asura.Application;

/// <summary>
/// A desktop request bound to its authorization proposal. Only the composer
/// can create one, so a request can never reach SessionHost with a proposal
/// it was not prepared with.
/// </summary>
public sealed class AgentDesktopAction
{
    internal AgentDesktopAction(AgentDesktopRequest request, AgentActionProposal proposal)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
    }

    public AgentDesktopRequest Request { get; }

    public AgentActionProposal Proposal { get; }
}
