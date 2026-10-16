using System.Collections.Immutable;
using Asura.Core;

namespace Asura.Agent;

public sealed record AgentCompactionRequest(
    AgentRunId RunId,
    long Generation,
    ImmutableArray<AgentMessage> Messages,
    ImmutableArray<AgentMessage> TurnPrefixMessages = default)
{
    public bool IsSplitTurn => !TurnPrefixMessages.IsDefaultOrEmpty;
}

public sealed record AgentCompactionSettings
{
    public AgentCompactionSettings(
        bool enabled = true,
        int reserveTokens = AgentContextWindowPolicy.DefaultReserveTokens,
        int keepRecentTokens = AgentContextWindowPolicy.DefaultKeepRecentTokens)
    {
        if (reserveTokens <= 0 || keepRecentTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserveTokens),
                "Compaction token budgets must be positive.");
        }

        Enabled = enabled;
        ReserveTokens = reserveTokens;
        KeepRecentTokens = keepRecentTokens;
    }

    public bool Enabled { get; }

    public int ReserveTokens { get; }

    public int KeepRecentTokens { get; }
}

public sealed record AgentContextUsage(long EstimatedTokens, bool UsesProviderReportedUsage, long EstimatedBytes = 0)
{
    public bool RequiresCompaction(int contextWindowTokens, AgentCompactionSettings settings) =>
        EstimatedTokens > contextWindowTokens - settings.ReserveTokens
        || EstimatedBytes > AgentContextWindowPolicy.MaximumHistoryBytes;
}

public interface IAgentConversationCompactor
{
    ValueTask<AgentMessage> CompactAsync(
        AgentCompactionRequest request,
        CancellationToken cancellationToken);
}

public enum AgentCompactionErrorCode
{
    NothingToCompact,
    Busy,
    Cancelled,
    CompactorFailure,
    InvalidSummary,
    LimitExceeded,
    ConversationConflict,

    /// <summary>
    /// The history chosen for compaction was itself too large for the
    /// compaction model; a smaller, older part may still fit.
    /// </summary>
    InputTooLarge,
}

/// <summary>
/// Thrown by a compactor when the history it was given does not fit the model
/// that summarizes it — the provider refused it as too large, or it exceeded
/// the local request bound. The session reports it as
/// <see cref="AgentCompactionErrorCode.InputTooLarge"/> so the caller can
/// compact a smaller, older part instead.
/// </summary>
public sealed class AgentCompactionInputTooLargeException : Exception
{
    public AgentCompactionInputTooLargeException()
        : base("The history to compact is too large for the compaction model.")
    {
    }

    public AgentCompactionInputTooLargeException(string message)
        : base(message)
    {
    }

    public AgentCompactionInputTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record AgentCompactionResult
{
    private AgentCompactionResult(bool succeeded, AgentCompactionErrorCode? errorCode)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }

    public AgentCompactionErrorCode? ErrorCode { get; }

    internal static AgentCompactionResult Success() => new(true, null);

    internal static AgentCompactionResult Failure(AgentCompactionErrorCode errorCode) =>
        new(false, errorCode);
}
