using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Providers;

/// <summary>
/// Creates native, request-scoped provider adapters. The returned adapters have no terminal or
/// session-host authority; they can only translate bounded model streams into inert agent events.
/// </summary>
public sealed class AiProviderFactory : IDisposable
{
    private readonly AiProviderRuntimeLimits _limits;
    private readonly AiProviderHttpTransport _transport;
    private readonly AiProviderModelDiscovery _modelDiscovery;
    private bool _disposed;

    public AiProviderFactory(
        ISecretVault secretVault,
        AiProviderRuntimeLimits? limits = null,
        AiProviderOAuthOptions? oauthOptions = null,
        Func<CancellationToken, ValueTask<string?>>? readCodexVersion = null)
        : this(secretVault, handler: null, limits, oauthOptions, readCodexVersion: readCodexVersion)
    {
    }

    internal AiProviderFactory(
        ISecretVault secretVault,
        HttpMessageHandler? handler,
        AiProviderRuntimeLimits? limits = null,
        AiProviderOAuthOptions? oauthOptions = null,
        TimeProvider? timeProvider = null,
        Func<CancellationToken, ValueTask<string?>>? readCodexVersion = null)
    {
        _limits = limits ?? AiProviderRuntimeLimits.Default;
        _transport = new AiProviderHttpTransport(
            secretVault ?? throw new ArgumentNullException(nameof(secretVault)),
            handler,
            oauthOptions,
            oauthHandler: handler,
            timeProvider);
        _modelDiscovery = new AiProviderModelDiscovery(_transport, _limits, readCodexVersion);
    }

    public IAgentProvider Create(
        AiProviderProfile profile,
        string? model = null,
        AgentServiceTier serviceTier = AgentServiceTier.Automatic)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!profile.IsEnabled)
        {
            throw new ArgumentException(
                "The AI-provider profile is disabled.",
                nameof(profile));
        }

        EnsureRuntimeSupported(profile);
        var selectedModel = ValidateModel(model ?? profile.DefaultModel);
        AiProviderServiceTierPolicy.EnsureSupported(
            profile,
            selectedModel,
            serviceTier);
        if (profile.Authentication is AiProviderAuthentication.AwsCredentialChain)
        {
            // AWS remains typed but fail-closed until the credential-chain
            // boundary can sign each Bedrock request with SigV4.
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.InvalidConfiguration);
        }

        return profile.Protocol switch
        {
            AiProviderProtocol.AnthropicMessages => new AnthropicAgentProvider(
                profile,
                selectedModel,
                _transport,
                _limits),
            AiProviderProtocol.OpenAiResponses => new OpenAiResponsesAgentProvider(
                profile,
                selectedModel,
                _transport,
                _limits,
                serviceTier),
            AiProviderProtocol.GitHubCopilot =>
                CreateGitHubCopilotProvider(profile, selectedModel, serviceTier),
            AiProviderProtocol.OpenAiChatCompletions =>
                new OpenAiCompatibleAgentProvider(
                    profile,
                    selectedModel,
                    _transport,
                    _limits,
                    serviceTier),
            _ => throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.InvalidConfiguration),
        };
    }

    public ValueTask<IReadOnlyList<AiProviderModelDescriptor>> ListModelsAsync(
        AiProviderProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureRuntimeSupported(profile);
        if (profile.Identity == AiProviderKind.OpenAi
            && profile.Authentication is AiProviderAuthentication.OAuth)
        {
            return ValueTask.FromException<IReadOnlyList<AiProviderModelDescriptor>>(
                AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ModelUnavailable));
        }

        return _modelDiscovery.ListAsync(profile, cancellationToken);
    }

    internal ValueTask<IReadOnlyList<AiProviderModelDescriptor>> ListOpenAiCodexModelsAsync(
        AiProviderProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (profile.Identity != AiProviderKind.OpenAi
            || profile.Authentication is not AiProviderAuthentication.OAuth)
        {
            return ValueTask.FromException<IReadOnlyList<AiProviderModelDescriptor>>(
                AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.InvalidConfiguration));
        }

        return _modelDiscovery.ListOpenAiCodexAsync(profile, cancellationToken);
    }

    internal async ValueTask ValidateAuthenticationAsync(
        AiProviderProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureRuntimeSupported(profile);
        _ = ValidateModel(profile.DefaultModel);
        using var request = await _transport.CreateRequestAsync(
            profile,
            HttpMethod.Post,
            "responses",
            "application/json",
            body: null,
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.Dispose();
    }

    private static string ValidateModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var normalized = model.Trim();
        if (normalized.Length > AiProviderProfile.MaximumModelIdLength
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The model identifier must be a bounded printable value.",
                nameof(model));
        }

        return normalized;
    }

    private IAgentProvider CreateGitHubCopilotProvider(
        AiProviderProfile profile,
        string selectedModel,
        AgentServiceTier serviceTier) =>
        UsesGitHubCopilotResponses(selectedModel)
            ? new OpenAiResponsesAgentProvider(
                profile,
                selectedModel,
                _transport,
                _limits,
                serviceTier)
            : new OpenAiCompatibleAgentProvider(
                profile,
                selectedModel,
                _transport,
                _limits,
                serviceTier);

    internal static bool UsesGitHubCopilotResponses(string modelId) =>
        modelId.Contains("-codex", StringComparison.OrdinalIgnoreCase);

    private static void EnsureRuntimeSupported(AiProviderProfile profile)
    {
        if (!AiProviderCatalog.Get(profile.Identity).IsRuntimeSupported)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.InvalidConfiguration);
        }
    }
}
