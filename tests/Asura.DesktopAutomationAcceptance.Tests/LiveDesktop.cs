using System.Text.Json;
using Asura.Agent;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;
using static Asura.DesktopAutomationAcceptance.Tests.ScriptedDesktopProvider;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// One round of the scripted model in a live run. <see cref="Before"/> runs
/// first, for what happens outside Asura between two model turns (a control
/// command to the fixture, a note of where its log stands); then
/// <see cref="Calls"/> chooses the tool calls from the latest snapshot the
/// model was shown, as a model would.
/// </summary>
internal sealed record LiveStep(Func<SnapshotView, ToolCall[]> Calls, Action? Before = null);

/// <summary>
/// Live runs of the governed runtime against the fixture, with no
/// substitution anywhere: the real host answers the access question too.
/// </summary>
internal static class LiveDesktop
{
    public const string SnapshotArguments = """{"app":"Desktop Automation Fixture","interactive_only":false}""";

    public static async Task<LiveTurn> RunAsync(
        GovernedDesktopStack stack,
        AgentPolicy policy,
        Func<GovernedAgentApproval, bool>? decide,
        params LiveStep[] steps)
    {
        var rounds = steps
            .Select(step => (Func<AgentProviderRequest, Task<AgentProviderEvent[]>>)(request =>
            {
                step.Before?.Invoke();
                return Task.FromResult(Batch(step.Calls(SnapshotView.Latest(request))));
            }))
            .Append(Round(Answer()))
            .ToArray();
        var provider = new ScriptedDesktopProvider(rounds);
        var runtime = stack.CreateRuntime(provider, policy, grantedAnswers: 0);
        var turn = await RuntimeDriver.SendAsync(
            runtime,
            GovernedDesktopStack.Prompt("Operate the fixture.", policy),
            decide);
        provider.ThrowIfScriptFailed();
        return new LiveTurn(provider, runtime, turn);
    }

    public static LiveStep Snapshot(string callId, string arguments = SnapshotArguments) =>
        new(_ => [new ToolCall(callId, DesktopAgentToolNames.Snapshot, arguments)]);

    /// <summary>An element tool on the node with this role and name in the latest snapshot.</summary>
    public static LiveStep OnElement(
        string callId,
        string tool,
        string role,
        string name,
        string extraArguments = "",
        Action? before = null) =>
        new(
            snapshot => [new ToolCall(callId, tool, $$"""{"ref":"{{snapshot.Ref(role, name)}}"{{extraArguments}}}""")],
            before);
}

internal sealed record LiveTurn(ScriptedDesktopProvider Provider, GovernedAgentRuntime Runtime, RuntimeTurn Turn)
{
    public AgentToolResult Result(string callId) => Provider.ToolResultOf(callId);
}

/// <summary>The nodes of the latest successful snapshot result the model received.</summary>
internal sealed class SnapshotView
{
    private readonly JsonElement[] _nodes;
    private readonly JsonElement[] _windows;

    private SnapshotView(JsonElement[] nodes, JsonElement[] windows)
    {
        _nodes = nodes;
        _windows = windows;
    }

    public IReadOnlyList<JsonElement> Nodes => _nodes;

    /// <summary>The latest successful snapshot's nodes and the latest window listing.</summary>
    public static SnapshotView Latest(AgentProviderRequest request) =>
        new(LatestList(request, "nodes"), LatestList(request, "windows"));

    /// <summary>The listed window of one process, as <c>desktop.focus_window</c> names it.</summary>
    public string WindowIdOf(uint processId)
    {
        var window = _windows.FirstOrDefault(item => item.GetProperty("pid").GetUInt32() == processId);
        return window.ValueKind == JsonValueKind.Object
            ? window.GetProperty("window_id").GetString()!
            : throw new Xunit.Sdk.XunitException($"No listed window belongs to process {processId}.");
    }

    public string Ref(string role, string name)
    {
        var match = _nodes.FirstOrDefault(node => Is(node, role, name));
        return match.ValueKind == JsonValueKind.Object && match.TryGetProperty("ref", out var reference)
            ? reference.GetString()!
            : throw new Xunit.Sdk.XunitException(
                $"The snapshot shows no {role} named '{name}'. Roles and names seen: "
                + string.Join(", ", _nodes.Select(node => $"{Text(node, "role")}:{Text(node, "name")}")));
    }

    private static JsonElement[] LatestList(AgentProviderRequest request, string listName)
    {
        foreach (var message in request.Messages.Reverse())
        {
            if (message.ToolResult?.Value.Content is not { } content)
            {
                continue;
            }

            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("ok", out var ok)
                && ok.GetBoolean()
                && document.RootElement.TryGetProperty(listName, out var list))
            {
                return [.. list.EnumerateArray().Select(item => item.Clone())];
            }
        }

        return [];
    }

    private static bool Is(JsonElement node, string role, string name) =>
        string.Equals(Text(node, "role"), role, StringComparison.Ordinal)
        && string.Equals(Text(node, "name"), name, StringComparison.Ordinal);

    private static string? Text(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) ? value.GetString() : null;
}
