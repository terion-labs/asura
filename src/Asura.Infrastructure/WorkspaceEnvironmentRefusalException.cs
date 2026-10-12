namespace Asura.Infrastructure;

/// <summary>
/// A workspace environment that can't start for a reason Asura wrote for the
/// person using it, with the step that gets past it ("The persistent workspace
/// disk is missing. Restore it from a backup, or …"). The SDK provider shows
/// this text as written; any other exception's text, which can carry paths or
/// a process's own output, stays in diagnostics.
/// </summary>
internal sealed class WorkspaceEnvironmentRefusalException : IOException
{
    public WorkspaceEnvironmentRefusalException(string message)
        : base(message)
    {
    }

    public WorkspaceEnvironmentRefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
