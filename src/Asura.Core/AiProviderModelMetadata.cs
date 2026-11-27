namespace Asura.Core;

/// <summary>Non-secret model details retained with a tested provider profile.</summary>
public sealed record AiProviderModelMetadata
{
    public AiProviderModelMetadata(
        string id,
        string displayName,
        int? contextWindowTokens = null,
        IReadOnlyList<AgentReasoningEffort>? reasoningEfforts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (id.Length > AiProviderProfile.MaximumModelIdLength || id.Any(char.IsControl)
            || displayName.Length > AiProviderProfile.MaximumModelIdLength || displayName.Any(char.IsControl))
        {
            throw new ArgumentException("Model metadata must contain bounded printable text.");
        }
        if (contextWindowTokens is <= 0 or > 10_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(contextWindowTokens));
        }

        if (reasoningEfforts is not null
            && (reasoningEfforts.Any(effort => !Enum.IsDefined(effort))
                || reasoningEfforts.Distinct().Count() != reasoningEfforts.Count))
        {
            throw new ArgumentException("Reasoning efforts must be unique and valid.", nameof(reasoningEfforts));
        }

        Id = id;
        DisplayName = displayName;
        ContextWindowTokens = contextWindowTokens;
        ReasoningEfforts = reasoningEfforts is null ? null : Array.AsReadOnly(reasoningEfforts.ToArray());
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int? ContextWindowTokens { get; }

    /// <summary>
    /// The reasoning efforts the provider listed for this model, or null when
    /// its model list doesn't say.
    /// </summary>
    public IReadOnlyList<AgentReasoningEffort>? ReasoningEfforts { get; }
}
