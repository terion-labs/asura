using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Asura.Agent;
using Asura.Core;

namespace Asura.Agent.Tests;

public sealed partial class NativeAgentSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolHistoryKeepsReadableContentAroundSecretsAcrossCheckpoint(bool structured)
    {
        const string text = "First instruction. password=fixture-sensitive Last instruction.";
        const string json = """{"ok":true,"content_origin":"untrusted_web","body":"First instruction. password=fixture-sensitive Last instruction.","items":[{"password":"nested-sensitive","count":7}],"token":false,"secret":null}""";
        var session = CreateSession();
        session.ChatTextProtection = new TestChatProtection();
        var tools = ImmutableArray.Create(Tool("terminal.read_screen"));
        var turn = await session.RunTurnAsync("Read", tools, ToolProvider("terminal.read_screen", "{}"), default);
        var proposal = Assert.Single(turn.ToolProposals);
        var value = structured ? AgentToolResultValue.FromJson(Encoding.UTF8.GetBytes(json)) : AgentToolResultValue.FromText(text);
        var result = new AgentToolResult(proposal, AgentToolResultStatus.Succeeded, "ok", value);
        Assert.True((await session.SubmitToolResultsAsync(proposal.Generation, [result], tools, TextProvider("Done"), default)).Succeeded);

        var checkpoint = Assert.IsType<AgentSessionCheckpoint>(session.CaptureCheckpoint().Checkpoint);
        Assert.DoesNotContain("fixture-sensitive", checkpoint.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-sensitive", checkpoint.PayloadJson, StringComparison.Ordinal);
        var restored = Assert.IsType<NativeAgentSession>(NativeAgentSession.RestoreCheckpoint(checkpoint).Session);
        var message = Assert.Single(restored.Snapshot().Transcript, entry => entry.ToolResult is not null);
        Assert.Contains("First instruction.", message.Content, StringComparison.Ordinal);
        Assert.Contains("Last instruction.", message.Content, StringComparison.Ordinal);
        Assert.NotEmpty(message.HiddenReferences);
        Assert.Equal(value.Content, Assert.Single(session.Snapshot().Transcript, entry => entry.ToolResult is not null).Content);
        restored.ChatTextProtection = session.ChatTextProtection;
        var reprojected = NativeAgentSession.ProjectProtectedMessage(message, restored.ChatTextProtection);
        Assert.Equal(message.HiddenReferences.ToArray(), reprojected.HiddenReferences.ToArray());
        if (structured)
        {
            using var parsed = JsonDocument.Parse(message.Content);
            using var repeated = JsonDocument.Parse(reprojected.Content);
            Assert.True(JsonElement.DeepEquals(parsed.RootElement, repeated.RootElement));
            Assert.True(parsed.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("untrusted_web", parsed.RootElement.GetProperty("content_origin").GetString());
            Assert.Equal(7, parsed.RootElement.GetProperty("items")[0].GetProperty("count").GetInt32());
            Assert.False(parsed.RootElement.GetProperty("token").GetBoolean());
            Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("secret").ValueKind);
        }
        else
        {
            Assert.Equal(message.Content, reprojected.Content);
        }
    }

    [Fact]
    public void PublicDocumentationResultRemainsReadableWithoutHiddenReferences()
    {
        const string json = """{"ok":true,"body":"Use --token '<token>'. Generated password: `/etc/terrarium/secrets/cockpit_root_password`. Route https://portal.example.com:8080@auth:admins","panel_id":"01a102491ac77ec9ab919d33370daf35","wait_outcome":"elapsed"}""";
        using var arguments = JsonDocument.Parse("{}");
        var proposal = new AgentToolProposal("proposal", 1, "call", "terminal.read_screen", arguments.RootElement);
        var message = AgentMessage.FromToolResult(new AgentToolResult(proposal, AgentToolResultStatus.Succeeded, "ok",
            AgentToolResultValue.FromJson(Encoding.UTF8.GetBytes(json))));
        var projected = NativeAgentSession.ProjectProtectedMessage(message, new TestChatProtection());
        Assert.Empty(projected.HiddenReferences);
        Assert.Same(message.ToolResult!.Value, projected.ToolResult!.Value);
        using var original = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(projected.Content);
        Assert.True(JsonElement.DeepEquals(original.RootElement, actual.RootElement));
    }
}
