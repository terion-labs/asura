using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Asura.Agent;
using Asura.Application;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// The model, scripted. Each round answers one provider request in order and
/// may read the request first, so a later round can act on a ref an earlier
/// snapshot returned, as a model would.
/// </summary>
internal sealed class ScriptedDesktopProvider(params Func<AgentProviderRequest, Task<AgentProviderEvent[]>>[] rounds)
    : IAgentProvider
{
    private int _callCount;

    public ConcurrentQueue<AgentProviderRequest> Requests { get; } = [];

    /// <summary>
    /// Why a scripted round could not answer, such as a control the snapshot
    /// did not show. The runtime reports any provider exception only as
    /// <c>provider_failure</c>, so the reason is kept here for the test.
    /// </summary>
    public ConcurrentQueue<Exception> RoundFailures { get; } = [];

    /// <summary>Fails the test with the first scripted round's own reason, when one failed.</summary>
    public void ThrowIfScriptFailed()
    {
        if (RoundFailures.TryPeek(out var failure))
        {
            // The results the model had received say which earlier tool call
            // left the script without what it needed.
            var received = Requests.LastOrDefault()?.Messages
                .Where(message => message.ToolResult is not null)
                .Select(message => $"{message.ToolResult!.ProviderCallId}={message.ToolResult.StableCode}")
                ?? [];
            throw new Xunit.Sdk.XunitException(
                "The scripted model could not continue: " + failure.Message
                + " Tool results it had received: " + string.Join(", ", received) + ".");
        }
    }

    public static ScriptedDesktopProvider Rounds(params AgentProviderEvent[][] rounds) =>
        new([.. rounds.Select(round => (Func<AgentProviderRequest, Task<AgentProviderEvent[]>>)(_ => Task.FromResult(round)))]);

    public static Func<AgentProviderRequest, Task<AgentProviderEvent[]>> Round(AgentProviderEvent[] events) =>
        _ => Task.FromResult(events);

    public static AgentProviderEvent[] Batch(params ToolCall[] calls)
    {
        var events = new List<AgentProviderEvent> { new AgentProviderEvent.ResponseStarted() };
        for (var index = 0; index < calls.Length; index++)
        {
            events.Add(new AgentProviderEvent.ToolCallStarted(
                index,
                calls[index].ProviderCallId,
                ProviderName(calls[index].ToolName)));
            events.Add(new AgentProviderEvent.ToolCallArgumentsDelta(index, calls[index].Arguments));
            events.Add(new AgentProviderEvent.ToolCallCompleted(index));
        }

        events.Add(new AgentProviderEvent.ResponseCompleted(AgentProviderStopReason.ToolUse));
        return [.. events];
    }

    public static AgentProviderEvent[] Answer() =>
    [
        new AgentProviderEvent.ResponseStarted(),
        new AgentProviderEvent.TextDelta("Done."),
        new AgentProviderEvent.ResponseCompleted(AgentProviderStopReason.EndTurn),
    ];

    public static string ProviderName(string toolName) =>
        new AgentToolDefinition(toolName, "Evidence tool.", "{\"type\":\"object\"}"u8.ToArray()).ProviderName;

    /// <summary>The desktop tools the first request offered the model.</summary>
    public string[] AdvertisedDesktopTools(int request = 0) =>
        [.. Requests.ToArray()[request].Tools
            .Select(tool => tool.Name)
            .Where(DesktopAgentToolNames.Owns)];

    /// <summary>The result the runtime gave the model for one call, from the latest request that carries it.</summary>
    public AgentToolResult ToolResultOf(string providerCallId) =>
        Requests.Reverse()
            .Select(request => request.Messages.FirstOrDefault(message => string.Equals(
                message.ToolResult?.ProviderCallId,
                providerCallId,
                StringComparison.Ordinal)))
            .FirstOrDefault(message => message is not null)
            ?.ToolResult
        ?? throw new Xunit.Sdk.XunitException($"No tool result reached the model for '{providerCallId}'.");

    public async IAsyncEnumerable<AgentProviderEvent> StreamAsync(
        AgentProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var call = Interlocked.Increment(ref _callCount);
        if (call > rounds.Length)
        {
            throw new InvalidOperationException("The scripted model received an unexpected round.");
        }

        AgentProviderEvent[] events;
        try
        {
            events = await rounds[call - 1](request);
        }
        catch (Exception exception)
        {
            RoundFailures.Enqueue(exception);
            throw;
        }

        foreach (var providerEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return providerEvent;
            await Task.Yield();
        }
    }
}

internal sealed record ToolCall(string ProviderCallId, string ToolName, string Arguments);
