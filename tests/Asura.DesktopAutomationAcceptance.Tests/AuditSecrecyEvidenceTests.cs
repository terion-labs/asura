using System.Text;
using Asura.Application;
using Asura.Core;
using Asura.SecurityCampaign.Tests;
using static Asura.DesktopAutomationAcceptance.Tests.ScriptedDesktopProvider;

namespace Asura.DesktopAutomationAcceptance.Tests;

/// <summary>
/// ADR 0061 evidence A5: audit details "never contain tree content, element
/// names, window titles, typed text, or application names". Canaries are
/// planted in every such field the test controls, the actions run through the
/// real stack to their real ends (refusal before authority, approval, the
/// library's own refusal), and then every byte the SQLite audit store wrote,
/// database and write-ahead log alike, is searched for them.
/// </summary>
[Collection(DesktopEvidenceCollection.Name)]
public sealed class AuditSecrecyEvidenceTests
{
    private static readonly string Named = SecurityCampaignCanaries.ApplicationManaged;
    private static readonly string Typed = SecurityCampaignCanaries.VaultResolved;

    [NativeEvidenceFact(EvidenceFlavor.Release)]
    public async Task No_planted_name_title_or_typed_text_reaches_the_audit_store()
    {
        await using var stack = await GovernedDesktopStack.CreateAsync(EvidenceFlavor.Release);
        using var foreign = EvidenceProcess.StartDetached(EvidenceProcess.ForeignHelper);
        var run = await stack.RegisterRunAsync(
            GovernedDesktopStack.Policy(AgentPermission.Ask, AgentPermission.Ask, AgentPermission.Ask));
        var element = EvidenceTargets.Element(
            foreign.ProcessId,
            foreign.Executable,
            application: Named,
            name: Named,
            windowTitle: Named);
        var ownElement = EvidenceTargets.Element(
            (uint)Environment.ProcessId,
            Environment.ProcessPath!,
            application: Named,
            name: Named,
            windowTitle: Named);
        AgentDesktopRequest[] requests =
        [
            new AgentDesktopRequest.Click(element),
            new AgentDesktopRequest.TypeText(element, new AgentDesktopInputText(Typed)),
            new AgentDesktopRequest.SetValue(element, new AgentDesktopInputText(Typed)),
            new AgentDesktopRequest.Select(element, new AgentDesktopInputText(Typed)),
            new AgentDesktopRequest.FocusWindow(EvidenceTargets.Window(foreign.ProcessId, foreign.Executable, Named)),
            new AgentDesktopRequest.Click(ownElement),
            new AgentDesktopRequest.Snapshot(new AgentDesktopApplicationId(Named)),
            new AgentDesktopRequest.Wait(
                new AgentDesktopWaitCondition.Text(new AgentDesktopApplicationId(Named), Typed),
                1_000),
            new AgentDesktopRequest.LaunchApp(new AgentDesktopApplicationId(Named)),
            new AgentDesktopRequest.CloseApp(new AgentDesktopApplicationTarget(
                AgentDesktopDisplayText.FromUntrusted(Named, "unknown"),
                new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, foreign.Executable),
                foreign.ProcessId,
                processInstance: null,
                DateTimeOffset.UtcNow)),
        ];

        foreach (var request in requests)
        {
            var outcome = await run.ProposeAsync(request, approve: true);

            // The canary really entered the stack: it is in the action the
            // host was handed.
            Assert.Contains(Named, Describe(outcome.Action.Request), StringComparison.Ordinal);
        }

        // Through the runtime as well, from model-supplied arguments.
        var policy = GovernedDesktopStack.Policy(AgentPermission.Auto, AgentPermission.Ask, AgentPermission.Ask);
        var provider = Rounds(
            Batch(
                new ToolCall("snapshot", DesktopAgentToolNames.Snapshot, $$"""{"app":"{{Named}}"}"""),
                new ToolCall(
                    "wait",
                    DesktopAgentToolNames.Wait,
                    $$"""{"app":"{{Named}}","condition":"window_title","value":"{{Named}}","timeout_ms":1000}"""),
                new ToolCall("launch", DesktopAgentToolNames.LaunchApp, $$"""{"app":"{{Named}}"}""")),
            Answer());
        var runtime = stack.CreateRuntime(
            provider,
            policy,
            EvidenceLibraries.ReleaseAccess() == AgentDesktopAccessState.Granted ? 0 : int.MaxValue);
        var turn = await RuntimeDriver.SendAsync(runtime, GovernedDesktopStack.Prompt("Open it.", policy), _ => true);
        Assert.True(turn.Result.IsSuccess, turn.Result.Code);

        var hostEntries = await stack.ReadDesktopAuditAsync(run.RunId);
        var runtimeEntries = await stack.ReadDesktopAuditAsync(runtime.Snapshot.RunId!.Value);
        Assert.Equal(requests.Length, hostEntries.Count);
        Assert.Equal(3, runtimeEntries.Count);
        Assert.Contains(hostEntries, entry => entry.ResultCode == AgentDesktopStableCodes.SelfTarget);
        Assert.Contains(hostEntries, entry => entry.LatestOutcome == AuditOutcome.Failed);

        var stored = StoredBytes(stack.DirectoryPath);
        Assert.True(stored.ContainsKey("audit.db"), "The audit database was not found.");
        // The search reads the records: what the audit is meant to hold is found.
        Assert.Contains(
            stored.Values,
            bytes => bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(AgentDesktopStableCodes.SelfTarget)) >= 0);
        foreach (var canary in new[] { Named, Typed })
        {
            foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
            {
                var needle = encoding.GetBytes(canary);
                Assert.True(
                    stored.All(file => file.Value.AsSpan().IndexOf(needle) < 0),
                    $"{canary} ({encoding.WebName}) was found in the audit store.");
            }
        }
    }

    private static string Describe(AgentDesktopRequest request) => request switch
    {
        AgentDesktopRequest.ElementAction action =>
            $"{action.Target.Application.Text} {action.Target.Name?.Text} {action.Target.WindowTitle?.Text}",
        AgentDesktopRequest.FocusWindow focus => focus.Window.Title.Text,
        AgentDesktopRequest.Snapshot snapshot => snapshot.Application.Value,
        AgentDesktopRequest.Wait wait => wait.Condition.Application.Value,
        AgentDesktopRequest.LaunchApp launch => launch.Application.Value,
        AgentDesktopRequest.CloseApp close => close.Target.Application.Text,
        _ => string.Empty,
    };

    /// <summary>Every file SQLite keeps for the audit database, with its bytes.</summary>
    private static Dictionary<string, byte[]> StoredBytes(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            // The store's process lock is held exclusively and holds no records.
            if (path.EndsWith(".lock", StringComparison.Ordinal))
            {
                continue;
            }

            // The database is open; share the read like SQLite's own readers do.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            files[Path.GetFileName(path)] = copy.ToArray();
        }

        return files;
    }
}
