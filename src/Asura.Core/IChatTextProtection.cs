namespace Asura.Core;

/// <summary>Pure projection used by the kernel; persistence and reveal belong to the host.</summary>
public interface IChatTextProtection
{
    ProtectedChatText Protect(string text);
    ProtectedChatText Hide(string text);
}
