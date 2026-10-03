namespace Asura.Core;

/// <summary>
/// Shared context-budget defaults used by automatic conversation compaction and
/// its presentation. Values follow the PI agent defaults.
/// </summary>
public static class AgentContextWindowPolicy
{
    // A local working budget when discovery supplies no model capacity. This
    // is not a claim about the provider's actual context window.
    public const int FallbackContextWindowTokens = 128 * 1024;

    public const int MaximumHistoryBytes = 4 * 1024 * 1024;

    public const int KeepRecentHistoryBytes = 1024 * 1024;

    public const int DefaultReserveTokens = 16 * 1024;

    public const int DefaultKeepRecentTokens = 20_000;

    public static int EffectiveLimit(int contextWindowTokens) =>
        Math.Max(1, contextWindowTokens - DefaultReserveTokens);
}
