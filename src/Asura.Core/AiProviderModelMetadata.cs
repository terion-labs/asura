namespace Asura.Core;

/// <summary>Non-secret model details retained with a tested provider profile.</summary>
public sealed record AiProviderModelMetadata
{
    public AiProviderModelMetadata(string id, string displayName, int? contextWindowTokens = null)
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

        Id = id;
        DisplayName = displayName;
        ContextWindowTokens = contextWindowTokens;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public int? ContextWindowTokens { get; }
}
