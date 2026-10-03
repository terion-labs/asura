using Asura.Agent;
using Asura.Agent.Providers;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;

namespace Asura.Desktop;

internal sealed class CatalogAgentProviderResolver(
    CatalogAiProviderRuntime providers,
    Uri? networkProxy = null)
    : IAgentProviderResolver
{
    private readonly CatalogAiProviderRuntime _providers =
        providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly Uri? _networkProxy = networkProxy;

    public IAgentProviderBinding PinProvider(AiProviderProfileId profileId)
    {
        var profile = _providers.Profiles.SingleOrDefault(
            candidate => candidate.Id == profileId);
        if (profile is null || !profile.IsEnabled)
        {
            throw new KeyNotFoundException(
                "The requested enabled AI-provider profile is unavailable.");
        }

        return new Binding(_providers.PinProvider(profileId), _networkProxy);
    }

    private sealed class Binding(
        CatalogAiProviderBinding value,
        Uri? networkProxy)
        : IAgentProviderBinding
    {
        private readonly CatalogAiProviderBinding _value =
            value ?? throw new ArgumentNullException(nameof(value));

        public AiProviderProfileId ProfileId => _value.ProfileId;

        public long Revision => _value.Revision;

        public string DefaultModel => _value.DefaultModel;

        public bool IsCurrent => _value.IsCurrent;

        public int? ContextWindowTokens(string model) => _value.ContextWindowTokens(model);

        public IAgentProvider CreateProvider(string model) => networkProxy is null
            ? _value.CreateProvider(model)
            : _value.CreateProvider(model, networkProxy);

        public IAgentProvider CreateProvider(
            string model,
            AgentServiceTier serviceTier) =>
            networkProxy is null
                ? _value.CreateProvider(model, serviceTier)
                : _value.CreateProvider(model, serviceTier, networkProxy);
    }
}
