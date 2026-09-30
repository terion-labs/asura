using System.Collections.Immutable;

namespace Asura.Core;

/// <summary>Safe text accompanied by locally minted references. Text alone cannot reveal anything.</summary>
public sealed record ProtectedChatText(string Text, ImmutableArray<ChatHiddenReference> References);
