namespace Asura.Application;

/// <summary>
/// The attachments kept for a workspace's agent conversations have used all
/// the storage they're allowed. Retrying can't succeed until older
/// conversations, and the attachments only they use, are deleted.
/// </summary>
public sealed class AgentAttachmentStorageLimitException : InvalidOperationException
{
    public AgentAttachmentStorageLimitException()
        : base("This workspace has reached its 1 GiB attachment storage limit.")
    {
    }
}
