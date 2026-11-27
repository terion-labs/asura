using System.Text.Json;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Providers;

internal sealed class AiProviderModelDiscovery(
    AiProviderHttpTransport transport,
    AiProviderRuntimeLimits limits,
    Func<CancellationToken, ValueTask<string?>>? readCodexVersion)
{
    public async ValueTask<IReadOnlyList<AiProviderModelDescriptor>> ListAsync(
        AiProviderProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(limits.DiscoveryTimeout);
        try
        {
            var discovery = AiProviderCatalog.Get(profile.Identity).ModelDiscovery;
            using var request = await transport.CreateRequestAsync(
                profile,
                HttpMethod.Get,
                DiscoveryPath(discovery, limits.MaximumModels),
                "application/json",
                body: null,
                operation.Token).ConfigureAwait(false);
            using var response = await transport
                .SendAsync(profile, request, operation.Token)
                .ConfigureAwait(false);
            AiProviderHttpTransport.ValidateContent(
                response,
                "application/json",
                limits.MaximumModelResponseBytes);
            await using var responseStream = await response.Content
                .ReadAsStreamAsync(operation.Token)
                .ConfigureAwait(false);
            await using var limited = new LimitedReadStream(
                responseStream,
                limits.MaximumModelResponseBytes);
            using var document = await ParseAsync(limited, operation.Token).ConfigureAwait(false);
            return ValidateModels(ParseModels(
                document.RootElement,
                discovery,
                limits.MaximumModels), request);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.Timeout,
                innerException: exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ProviderUnavailable,
                innerException: exception);
        }
    }

    public async ValueTask<IReadOnlyList<AiProviderModelDescriptor>> ListOpenAiCodexAsync(
        AiProviderProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(limits.DiscoveryTimeout);
        try
        {
            // Model discovery is versioned by the installed Codex release. Do not
            // claim a fixed client version or reuse Codex credentials or sessions.
            var version = readCodexVersion is null
                ? null
                : await readCodexVersion(operation.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new AiProviderClientException(
                    AiProviderRuntimeErrorCode.InvalidConfiguration,
                    "ai_provider_codex_version_unavailable",
                    "Could not read the installed Codex version. Install Codex and make it available on PATH, then retry.");
            }

            using var request = await transport.CreateRequestAsync(
                profile,
                HttpMethod.Get,
                $"models?client_version={Uri.EscapeDataString(version)}",
                "application/json",
                body: null,
                operation.Token).ConfigureAwait(false);
            using var response = await transport.SendAsync(
                profile,
                request,
                operation.Token).ConfigureAwait(false);
            AiProviderHttpTransport.ValidateContent(
                response,
                "application/json",
                limits.MaximumModelResponseBytes);
            await using var responseStream = await response.Content
                .ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
            await using var limited = new LimitedReadStream(
                responseStream,
                limits.MaximumModelResponseBytes);
            using var document = await ParseAsync(limited, operation.Token)
                .ConfigureAwait(false);
            return ValidateModels(ParseOpenAiCodexModels(document.RootElement, limits.MaximumModels), request);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.Timeout,
                innerException: exception);
        }
    }

    // Provider responses are untrusted even though that provider receives the credential.
    // Reject reflected authentication material before model metadata reaches settings or storage.
    private static IReadOnlyList<AiProviderModelDescriptor> ValidateModels(
        IReadOnlyList<AiProviderModelDescriptor> models, HttpRequestMessage request)
    {
        var credentials = new List<string>();
        if (request.Headers.Authorization?.Parameter is { Length: > 0 } bearer) { credentials.Add(bearer); }
        foreach (var name in new[] { "x-api-key", "x-goog-api-key" })
        {
            if (request.Headers.TryGetValues(name, out var values)) { credentials.AddRange(values); }
        }
        if (models.Any(model => LiteralSecretValidator.ContainsLikelyLiteralSecret(model.Id)
            || LiteralSecretValidator.ContainsLikelyLiteralSecret(model.DisplayName ?? string.Empty)
            || credentials.Any(credential => model.Id.Contains(credential, StringComparison.Ordinal)
                || model.DisplayName?.Contains(credential, StringComparison.Ordinal) == true)))
        {
            throw AiProviderClientException.Create(AiProviderRuntimeErrorCode.ProtocolError);
        }
        return models;
    }

    private static string DiscoveryPath(
        AiProviderModelDiscoveryKind discovery,
        int maximumModels) => discovery switch
        {
            AiProviderModelDiscoveryKind.AnthropicModels =>
                $"models?limit={maximumModels}",
            AiProviderModelDiscoveryKind.OpenAiModels => "models",
            AiProviderModelDiscoveryKind.GoogleModels =>
                $"models?pageSize={maximumModels}",
            _ => throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ModelUnavailable),
        };

    private static async ValueTask<JsonDocument> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(
                stream,
                AiProviderJson.DocumentOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ProtocolError,
                innerException: exception);
        }
    }

    private static IReadOnlyList<AiProviderModelDescriptor> ParseModels(
        JsonElement root,
        AiProviderModelDiscoveryKind discovery,
        int maximumModels)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw AiProviderClientException.Create(AiProviderRuntimeErrorCode.ProtocolError);
        }

        var data = discovery switch
        {
            AiProviderModelDiscoveryKind.AnthropicModels
                or AiProviderModelDiscoveryKind.OpenAiModels =>
                AiProviderJson.RequiredArray(root, "data"),
            AiProviderModelDiscoveryKind.GoogleModels =>
                AiProviderJson.RequiredArray(root, "models"),
            _ => throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ProtocolError),
        };
        var models = new List<AiProviderModelDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ProtocolError);
            }

            var id = ReadModelId(item, discovery);
            if (models.Count == maximumModels)
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ResponseTooLarge);
            }

            var displayName = AiProviderJson.OptionalBoundedString(
                    item,
                    discovery == AiProviderModelDiscoveryKind.GoogleModels
                        ? "displayName"
                        : "display_name",
                    AiProviderProfile.MaximumModelIdLength)
                ?? id;
            if (!ids.Add(id))
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ProtocolError);
            }

            try
            {
                models.Add(new AiProviderModelDescriptor(
                    id,
                    displayName,
                    WithAutomatic(ReportedEfforts(item))));
            }
            catch (ArgumentException exception)
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ProtocolError,
                    innerException: exception);
            }
        }

        if (models.Count == 0)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ModelUnavailable);
        }

        return Array.AsReadOnly(models
            .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Id, StringComparer.Ordinal)
            .ToArray());
    }

    private static string ReadModelId(
        JsonElement item,
        AiProviderModelDiscoveryKind discovery)
    {
        var propertyName = discovery == AiProviderModelDiscoveryKind.GoogleModels
            ? "name"
            : "id";
        var id = AiProviderJson.RequiredBoundedString(
            item,
            propertyName,
            AiProviderProfile.MaximumModelIdLength + "models/".Length);
        if (discovery != AiProviderModelDiscoveryKind.GoogleModels)
        {
            return id;
        }

        const string prefix = "models/";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)
            || id.Length == prefix.Length)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ProtocolError);
        }

        return id[prefix.Length..];
    }

    private static IReadOnlyList<AiProviderModelDescriptor> ParseOpenAiCodexModels(
        JsonElement root,
        int maximumModels)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw AiProviderClientException.Create(AiProviderRuntimeErrorCode.ProtocolError);
        }

        var data = AiProviderJson.RequiredArray(root, "models");
        var models = new List<AiProviderModelDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ProtocolError);
            }

            var visibility = AiProviderJson.OptionalBoundedString(
                item,
                "visibility",
                16);
            if (!string.Equals(visibility, "list", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (models.Count == maximumModels)
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ResponseTooLarge);
            }

            var id = AiProviderJson.RequiredBoundedString(
                item,
                "slug",
                AiProviderProfile.MaximumModelIdLength);
            var displayName = AiProviderJson.OptionalBoundedString(
                    item,
                    "display_name",
                    AiProviderProfile.MaximumModelIdLength)
                ?? id;
            if (!ids.Add(id))
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ProtocolError);
            }

            int? contextWindow = null;
            if (item.TryGetProperty("context_window", out var context)
                && context.ValueKind != JsonValueKind.Null)
            {
                if (context.ValueKind != JsonValueKind.Number || !context.TryGetInt32(out var tokens))
                {
                    throw AiProviderClientException.Create(AiProviderRuntimeErrorCode.ProtocolError);
                }

                contextWindow = tokens;
            }

            try
            {
                models.Add(new AiProviderModelDescriptor(
                    id,
                    displayName,
                    WithAutomatic(CodexReportedEfforts(item)),
                    contextWindowTokens: contextWindow));
            }
            catch (ArgumentException exception)
            {
                throw AiProviderClientException.Create(
                    AiProviderRuntimeErrorCode.ProtocolError,
                    innerException: exception);
            }
        }

        if (models.Count == 0)
        {
            throw AiProviderClientException.Create(
                AiProviderRuntimeErrorCode.ModelUnavailable);
        }

        return Array.AsReadOnly(models.ToArray());
    }

    /// <summary>
    /// The efforts a model list names for one model, where the provider says:
    /// GitHub Copilot's <c>capabilities.supports.reasoning_effort</c>, or
    /// OpenRouter's <c>supported_parameters</c> naming <c>reasoning</c>, which
    /// takes the common three levels. Null where the list says nothing; a
    /// shape this version doesn't know leaves the model as it was rather than
    /// failing the whole list.
    /// </summary>
    private static IReadOnlyList<AgentReasoningEffort>? ReportedEfforts(JsonElement item)
    {
        if (item.TryGetProperty("capabilities", out var capabilities)
            && capabilities.ValueKind == JsonValueKind.Object
            && capabilities.TryGetProperty("supports", out var supports)
            && supports.ValueKind == JsonValueKind.Object
            && supports.TryGetProperty("reasoning_effort", out var efforts)
            && efforts.ValueKind == JsonValueKind.Array)
        {
            return Efforts(efforts.EnumerateArray());
        }

        if (item.TryGetProperty("supported_parameters", out var parameters)
            && parameters.ValueKind == JsonValueKind.Array
            && parameters.EnumerateArray().Any(parameter =>
                parameter.ValueKind == JsonValueKind.String
                && string.Equals(parameter.GetString(), "reasoning", StringComparison.Ordinal)))
        {
            return [AgentReasoningEffort.Low, AgentReasoningEffort.Medium, AgentReasoningEffort.High];
        }

        return null;
    }

    /// <summary>The ChatGPT model list's <c>supported_reasoning_levels</c>, each an object naming its <c>effort</c>.</summary>
    private static IReadOnlyList<AgentReasoningEffort>? CodexReportedEfforts(JsonElement item) =>
        item.TryGetProperty("supported_reasoning_levels", out var levels)
        && levels.ValueKind == JsonValueKind.Array
            ? Efforts(levels.EnumerateArray().Select(level =>
                level.ValueKind == JsonValueKind.Object && level.TryGetProperty("effort", out var effort)
                    ? effort
                    : level))
            : null;

    private static IReadOnlyList<AgentReasoningEffort>? Efforts(IEnumerable<JsonElement> values)
    {
        var efforts = values
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => Effort(value.GetString()))
            .OfType<AgentReasoningEffort>()
            .Distinct()
            .Order()
            .ToArray();
        return efforts.Length == 0 ? null : efforts;
    }

    private static AgentReasoningEffort? Effort(string? wire) => wire?.ToLowerInvariant() switch
    {
        "none" => AgentReasoningEffort.Off,
        "minimal" => AgentReasoningEffort.Minimal,
        "low" => AgentReasoningEffort.Low,
        "medium" => AgentReasoningEffort.Medium,
        "high" => AgentReasoningEffort.High,
        "xhigh" => AgentReasoningEffort.ExtraHigh,
        "max" => AgentReasoningEffort.Max,
        _ => null,
    };

    private static IReadOnlyList<AgentReasoningEffort>? WithAutomatic(IReadOnlyList<AgentReasoningEffort>? reported) =>
        reported is null ? null : [AgentReasoningEffort.Automatic, .. reported];
}
