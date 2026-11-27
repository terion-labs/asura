using System.Collections.Immutable;
using Asura.Core;

namespace Asura.Agent.Providers;

/// <summary>
/// The effort levels the current adapters can translate without silently
/// reinterpreting a user choice. Provider capability flags are only ceilings;
/// this policy narrows them for the selected model and wire route.
/// </summary>
internal static class AiProviderReasoningPolicy
{
    private static readonly ImmutableArray<AgentReasoningEffort> AutomaticOnly =
        [AgentReasoningEffort.Automatic];
    private static readonly ImmutableArray<AgentReasoningEffort> Responses =
    [
        AgentReasoningEffort.Automatic,
        AgentReasoningEffort.Off,
        AgentReasoningEffort.Low,
        AgentReasoningEffort.Medium,
        AgentReasoningEffort.High,
    ];
    private static readonly ImmutableArray<AgentReasoningEffort> ResponsesExtraHigh =
        [.. Responses, AgentReasoningEffort.ExtraHigh];
    private static readonly ImmutableArray<AgentReasoningEffort> Responses56 =
        [.. Responses, AgentReasoningEffort.ExtraHigh, AgentReasoningEffort.Max];
    private static readonly ImmutableArray<AgentReasoningEffort> ResponsesPro =
    [
        AgentReasoningEffort.Automatic,
        AgentReasoningEffort.Medium,
        AgentReasoningEffort.High,
        AgentReasoningEffort.ExtraHigh,
    ];
    private static readonly ImmutableArray<AgentReasoningEffort> AnthropicAdaptive =
    [
        AgentReasoningEffort.Automatic,
        AgentReasoningEffort.Off,
        AgentReasoningEffort.Low,
        AgentReasoningEffort.Medium,
        AgentReasoningEffort.High,
    ];
    private static readonly ImmutableArray<AgentReasoningEffort> AnthropicAlwaysThinking =
    [
        AgentReasoningEffort.Automatic,
        AgentReasoningEffort.Low,
        AgentReasoningEffort.Medium,
        AgentReasoningEffort.High,
    ];
    private static readonly ImmutableArray<AgentReasoningEffort> AnthropicExtraHigh =
        [.. AnthropicAdaptive, AgentReasoningEffort.ExtraHigh];
    private static readonly ImmutableArray<AgentReasoningEffort> AnthropicMaximum =
        [.. AnthropicAdaptive, AgentReasoningEffort.ExtraHigh, AgentReasoningEffort.Max];
    private static readonly ImmutableArray<AgentReasoningEffort> AnthropicAlwaysThinkingMaximum =
        [.. AnthropicAlwaysThinking, AgentReasoningEffort.ExtraHigh, AgentReasoningEffort.Max];

    // The levels each wire format can carry: Responses' reasoning.effort,
    // Chat Completions' reasoning_effort (OpenRouter's reasoning.effort).
    private static readonly ImmutableArray<AgentReasoningEffort> WireEfforts =
    [
        AgentReasoningEffort.Off,
        AgentReasoningEffort.Minimal,
        AgentReasoningEffort.Low,
        AgentReasoningEffort.Medium,
        AgentReasoningEffort.High,
        AgentReasoningEffort.ExtraHigh,
        AgentReasoningEffort.Max,
    ];

    public static ImmutableArray<AgentReasoningEffort> SupportedEfforts(
        AiProviderProfile profile) => SupportedEfforts(profile, profile.DefaultModel);

    /// <summary>
    /// The efforts a model takes. A list the provider reported for the model
    /// with its model list wins; the model-name table below only stands in
    /// for providers that don't say.
    /// </summary>
    public static ImmutableArray<AgentReasoningEffort> SupportedEfforts(
        AiProviderProfile profile,
        string modelId,
        IReadOnlyList<AgentReasoningEffort>? reported = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (!profile.IsEnabled
            || !AiProviderCatalog.Get(profile.Identity).IsRuntimeSupported)
        {
            return AutomaticOnly;
        }

        if (reported is { Count: > 0 } && CarriesReportedEfforts(profile))
        {
            return
            [
                AgentReasoningEffort.Automatic,
                .. WireEfforts.Where(reported.Contains),
            ];
        }

        if (!profile.Capabilities.SupportsReasoning)
        {
            return AutomaticOnly;
        }

        return profile.Protocol switch
        {
            AiProviderProtocol.OpenAiResponses => ResponsesEfforts(modelId),
            AiProviderProtocol.GitHubCopilot
                when AiProviderFactory.UsesGitHubCopilotResponses(modelId) =>
                ResponsesEfforts(modelId),
            AiProviderProtocol.AnthropicMessages
                when RejectsDisabledThinking(modelId) =>
                AnthropicAlwaysThinkingMaximum,
            AiProviderProtocol.AnthropicMessages
                when SupportsNativeExtraHighThinking(modelId) =>
                AnthropicMaximum,
            AiProviderProtocol.AnthropicMessages
                when SupportsAdaptiveThinking(modelId) =>
                AnthropicExtraHigh,
            _ => AutomaticOnly,
        };
    }

    /// <summary>
    /// Whether the profile's request writer sends a reported effort: the
    /// Responses and Chat Completions adapters do; Anthropic's thinking
    /// budgets have no reported list to follow.
    /// </summary>
    private static bool CarriesReportedEfforts(AiProviderProfile profile) =>
        profile.Protocol is AiProviderProtocol.OpenAiResponses
            or AiProviderProtocol.GitHubCopilot
            or AiProviderProtocol.OpenAiChatCompletions;

    private static ImmutableArray<AgentReasoningEffort> ResponsesEfforts(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        var version = GptVersion(normalized);
        if (version >= new Version(5, 6))
        {
            return Responses56;
        }

        if (normalized.Contains("gpt-5.2-pro", StringComparison.Ordinal)
            || normalized.Contains("gpt-5.5-pro", StringComparison.Ordinal))
        {
            return ResponsesPro;
        }

        if (normalized.Contains("gpt-5-pro", StringComparison.Ordinal))
        {
            return [AgentReasoningEffort.Automatic, AgentReasoningEffort.High];
        }

        if (version >= new Version(5, 0))
        {
            return ResponsesExtraHigh;
        }

        return Responses;
    }

    /// <summary>
    /// The GPT generation a model id names ("gpt-5.6-terra" is 5.6,
    /// "gpt-6" is 6.0), or null for another family. A newer generation keeps
    /// the levels of the one before it rather than losing them to an exact
    /// name match.
    /// </summary>
    private static Version? GptVersion(string normalizedModelId)
    {
        const string prefix = "gpt-";
        var start = normalizedModelId.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        var end = start;
        while (end < normalizedModelId.Length
               && (char.IsAsciiDigit(normalizedModelId[end]) || normalizedModelId[end] == '.'))
        {
            end++;
        }

        var parts = normalizedModelId[start..end].Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0
            && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
            ? new Version(major, parts.Length > 1
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minor)
                    ? minor
                    : 0)
            : null;
    }

    public static bool SupportsAdaptiveThinking(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        return normalized.Contains("claude-opus-4-6", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-4-7", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-4-8", StringComparison.Ordinal)
            || normalized.Contains("claude-sonnet-4-6", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-5", StringComparison.Ordinal)
            || normalized.Contains("claude-sonnet-5", StringComparison.Ordinal)
            || normalized.Contains("claude-fable-5", StringComparison.Ordinal)
            || normalized.Contains("claude-mythos", StringComparison.Ordinal);
    }

    public static bool SupportsSummarizedThinking(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        return normalized.Contains("claude-opus-4-7", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-4-8", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-5", StringComparison.Ordinal)
            || normalized.Contains("claude-sonnet-5", StringComparison.Ordinal)
            || normalized.Contains("claude-fable-5", StringComparison.Ordinal)
            || normalized.Contains("claude-mythos", StringComparison.Ordinal);
    }

    public static bool SupportsNativeExtraHighThinking(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        return normalized.Contains("claude-opus-4-7", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-4-8", StringComparison.Ordinal)
            || normalized.Contains("claude-opus-5", StringComparison.Ordinal)
            || normalized.Contains("claude-sonnet-5", StringComparison.Ordinal);
    }

    public static bool RejectsDisabledThinking(string modelId)
    {
        var normalized = modelId.ToLowerInvariant();
        return normalized.Contains("claude-fable-", StringComparison.Ordinal)
            || normalized.Contains("claude-mythos-", StringComparison.Ordinal);
    }
}
