using System.Text.Json;
using Asura.Agent;
using Asura.Application;
using Asura.Core;

namespace Asura.Agent.Runtime;

internal static class KubernetesControlAgentToolParser
{
    public static KubernetesControlAgentIntentResult Parse(
        AgentToolProposal proposal,
        AgentContextPanel panel)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(panel);
        var capability = KubernetesAgentToolSet.RequiredCapability(proposal.ToolName);
        if (capability is null || proposal.ToolName is not (BuiltInAgentTools.KubernetesPreview or BuiltInAgentTools.KubernetesCommit))
        {
            return UnknownTool();
        }

        if (!TryReadProperties(proposal, out var properties, out var rejection))
        {
            return rejection;
        }

        return KubernetesAgentToolSet.Supports(panel, capability)
            ? ParseRequest(proposal.ToolName, panel.PanelId, properties)
            : UnavailableTool();
    }

    public static KubernetesControlAgentIntentResult Parse(
        AgentToolProposal proposal,
        IReadOnlyList<AgentContextPanel> panels)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var capability = KubernetesAgentToolSet.RequiredCapability(proposal.ToolName);
        if (capability is null || proposal.ToolName is not (BuiltInAgentTools.KubernetesPreview or BuiltInAgentTools.KubernetesCommit))
        {
            return UnknownTool();
        }

        if (!TryReadProperties(proposal, out var properties, out var rejection))
        {
            return rejection;
        }

        var eligible = KubernetesAgentToolSet.ActiveKubernetesPanels(panels)
            .Where(panel => KubernetesAgentToolSet.Supports(panel, capability))
            .ToArray();
        if (eligible.Length == 0)
        {
            return UnavailableTool();
        }

        if (!properties.Remove("panel_id", out var panelElement)
            || !TryGetString(panelElement, out var panelId))
        {
            return Invalid("A broad Kubernetes tool requires one exact panel_id.");
        }

        var selected = eligible.FirstOrDefault(panel => string.Equals(
            panel.PanelId.Value,
            panelId,
            StringComparison.Ordinal));
        return selected is null
            ? Invalid("The selected panel_id is unavailable for this Kubernetes tool.")
            : ParseRequest(proposal.ToolName, selected.PanelId, properties);
    }

    private static KubernetesControlAgentIntentResult ParseRequest(
        string toolName,
        PanelInstanceId panelId,
        Dictionary<string, JsonElement> properties)
    {
        try
        {
            var operation = string.Equals(toolName, BuiltInAgentTools.KubernetesCommit, StringComparison.Ordinal)
                ? AgentKubernetesControlOperation.Commit : ReadRequiredString(properties, "operation") switch
                {
                    "apply" => AgentKubernetesControlOperation.Apply,
                    "scale" => AgentKubernetesControlOperation.Scale,
                    "restart" => AgentKubernetesControlOperation.Restart,
                    "delete" => AgentKubernetesControlOperation.Delete,
                    _ => throw new ArgumentException("Unknown mutation operation."),
                };
            var reference = ReadRequiredString(properties, "reference");
            var manifest = operation == AgentKubernetesControlOperation.Apply ? ReadRequiredString(properties, "manifest_json") : null;
            int? replicas = operation == AgentKubernetesControlOperation.Scale ? ReadOptionalInteger(properties, "replicas", -1) : null;
            var request = new AgentKubernetesControlIntent(panelId, operation, reference, manifest, replicas);
            RequireNoProperties(properties);
            return new KubernetesControlAgentIntentResult.Parsed(panelId, request);
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or OverflowException or JsonException)
        {
            return Invalid("Kubernetes tool arguments do not match the closed schema.");
        }
    }

    private static bool TryReadProperties(
        AgentToolProposal proposal,
        out Dictionary<string, JsonElement> properties,
        out KubernetesControlAgentIntentResult rejection)
    {
        if (proposal.Arguments.ValueKind != JsonValueKind.Object)
        {
            properties = [];
            rejection = Invalid("Kubernetes tool arguments must be one object.");
            return false;
        }

        try
        {
            properties = ReadObject(proposal.Arguments);
            rejection = null!;
            return true;
        }
        catch (ArgumentException exception)
        {
            properties = [];
            rejection = Invalid(exception.Message);
            return false;
        }
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement element)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new ArgumentException("Kubernetes tool arguments contain a duplicate property.");
            }
        }

        return properties;
    }

    private static string ReadRequiredString(
        Dictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.Remove(name, out var element)
            || !TryGetString(element, out var value))
        {
            throw new ArgumentException($"Kubernetes tool argument '{name}' is required.");
        }

        return value;
    }

    private static string? ReadOptionalString(
        Dictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.Remove(name, out var element))
        {
            return null;
        }

        if (!TryGetString(element, out var value))
        {
            throw new ArgumentException($"Kubernetes tool argument '{name}' must be a string.");
        }

        return value;
    }

    private static int ReadOptionalInteger(
        Dictionary<string, JsonElement> properties,
        string name,
        int defaultValue)
    {
        if (!properties.Remove(name, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value))
        {
            throw new ArgumentException($"Kubernetes tool argument '{name}' must be an integer.");
        }

        return value;
    }

    private static bool TryGetString(JsonElement element, out string value)
    {
        if (element.ValueKind == JsonValueKind.String
            && element.GetString() is { } text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void RequireNoProperties(
        Dictionary<string, JsonElement> properties)
    {
        if (properties.Count != 0)
        {
            throw new ArgumentException("Kubernetes tool arguments contain unknown properties.");
        }
    }

    private static KubernetesControlAgentIntentResult UnknownTool() =>
        new KubernetesControlAgentIntentResult.Rejected(
            "tool_not_available",
            "The requested Kubernetes tool is not available.");

    private static KubernetesControlAgentIntentResult UnavailableTool() =>
        new KubernetesControlAgentIntentResult.Rejected(
            "tool_not_available",
            "The live Kubernetes session does not advertise this capability.");

    private static KubernetesControlAgentIntentResult Invalid(string message) =>
        new KubernetesControlAgentIntentResult.Rejected("tool_arguments_invalid", message);
}
