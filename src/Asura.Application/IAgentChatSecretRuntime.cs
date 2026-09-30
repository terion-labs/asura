using Asura.Core;

namespace Asura.Application;

/// <summary>Trusted local presentation access, separate from the agent's tool manifest.</summary>
public interface IAgentChatSecretRuntime
{
    ProtectedChatText ProtectDraft(string text);
    ValueTask<SecretVaultResult<string>> RevealChatSecretAsync(ChatHiddenReference reference, CancellationToken cancellationToken);
}
