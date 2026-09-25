namespace Asura.Application;

/// <summary>
/// A terminal input guard failed before authorization. The message describes
/// the guard, never the submitted text or matched credential material.
/// </summary>
public sealed class AgentTerminalInputRejectedException : ArgumentException
{
    public AgentTerminalInputRejectedException()
        : base("The terminal input resembles inline credentials. No input was sent. "
            + "This is an input-content check, not a terminal ownership or busy error. "
            + "The check also conservatively rejects assignments to credential-named variables, "
            + "even when their values are read from a file. Use an already-authenticated CLI "
            + "or an existing script that keeps credentials out of the submitted input.")
    {
    }
}
