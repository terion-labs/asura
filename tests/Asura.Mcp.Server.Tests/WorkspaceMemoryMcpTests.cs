using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;
using Asura.Infrastructure;
using Asura.Infrastructure.Tests;

namespace Asura.Mcp.Server.Tests;

public sealed class WorkspaceMemoryMcpTests
{
    [Fact]
    public async Task NativeAndMcpSharePersistenceRevisionsPermissionsAndIsolationWithoutAnOperator()
    {
        await using var temp = TemporaryDatabase.Create();
        var store = new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System);
        var registry = new WorkspaceMemoryRegistry(store);
        var memory = registry.Bind(new("first"), new("owner"), "First");
        var other = registry.Bind(new("second"), new("other"), "Second");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        await using var server = new WorkspaceMcpServer(registry);
        const string token = "memory-test-token-with-thirty-two-characters";
        await server.StartAsync(port, token, CancellationToken.None);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2026-07-28");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        var write = """{"request_id":"unique-write","generation":1,"kind":"Tip","title":"Migration route","body":"Use direct staging connection","tags":[],"applicability":"","source":"Observed staging run"}""";
        using var arguments = JsonDocument.Parse(write);
        var native = await WorkspaceMemoryTools.CallAsync(memory, "memory.save", arguments.RootElement, "Built-in test agent", CancellationToken.None);
        Assert.Equal("memory_saved", native.StableCode);
        using var page = await Call(client, "memory.search", """{"workspace_id":"first","query":"Migration"}""");
        var note = Assert.Single(page.RootElement.GetProperty("notes").EnumerateArray());
        var id = note.GetProperty("id").GetString()!;
        Assert.Equal("Built-in test agent", note.GetProperty("author").GetString());
        using var isolated = await Call(client, "memory.read", new JsonObject { ["workspace_id"] = "second", ["id"] = id }.ToJsonString());
        Assert.Empty(isolated.RootElement.GetProperty("notes").EnumerateArray());
        using var duplicate = await Call(client, "memory.save", write[..^1] + ",\"workspace_id\":\"first\"}");
        Assert.Equal("memory_saved", duplicate.RootElement.GetProperty("code").GetString());
        using var retry = await Call(client, "memory.save", write[..^1] + ",\"workspace_id\":\"first\"}");
        Assert.Equal(duplicate.RootElement.GetProperty("note").GetProperty("id").GetString(), retry.RootElement.GetProperty("note").GetProperty("id").GetString());
        Assert.Equal(2, (await memory.QueryAsync(new(), CancellationToken.None)).Notes.Length);
        var disabled = await memory.ChangeAsync(new(WorkspaceMemoryChange.Disable, 1), new("User", true), CancellationToken.None);
        using var rejected = await Call(client, "memory.search", """{"workspace_id":"first","query":"Migration"}""", "memory_disabled");
        using var stale = await Call(client, "memory.save", write[..^1] + ",\"workspace_id\":\"first\"}");
        Assert.Equal("memory_generation_changed", stale.RootElement.GetProperty("code").GetString());
        Assert.False(disabled.State.Enabled);
        Assert.Empty((await other.QueryAsync(new(), CancellationToken.None)).Notes);
    }

    [Fact]
    public async Task SharedProtectionRejectsDisclosedSecretsAndPaginationReachesOlderNotes()
    {
        await using var temp = TemporaryDatabase.Create();
        var scope = new AgentConversationScopeId("owner");
        var access = new WorkspaceMemoryAccess(new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System), scope);
        using var vault = new InMemorySecretVault();
        using var protection = new WorkspaceChatSecrets(vault, scope);
        var reference = Assert.Single(protection.Hide("orchid private phrase").References);
        Assert.IsType<SecretVaultResult<string>.Success>(await protection.ResolveTextAsync(reference, SecretUseKind.ChatModelDisclosure, CancellationToken.None));
        access.AttachProtection(protection);
        using var secret = JsonDocument.Parse("""{"request_id":"orchid private phrase","generation":1,"kind":"Tip","title":"Benign","body":"Benign content","tags":[],"applicability":"","source":"Reported"}""");
        var denied = await WorkspaceMemoryTools.CallAsync(access, "memory.save", secret.RootElement, "MCP caller", CancellationToken.None);
        Assert.Contains("memory_secret_rejected", denied.Value.Content, StringComparison.Ordinal);
        Assert.Empty((await access.QueryAsync(new(), CancellationToken.None)).Notes);
        for (var i = 0; i < 7; i++)
        {
            await access.SaveAsync(new(Guid.NewGuid().ToString("N"), 1, null, null, WorkspaceMemoryKind.Tip, "Matching " + i, "Useful evidence", [], "", "Reported"), new("Native"), CancellationToken.None);
        }
        using var search = JsonDocument.Parse("""{"query":"Matching","offset":6}""");
        var last = await WorkspaceMemoryTools.CallAsync(access, "memory.search", search.RootElement, "MCP caller", CancellationToken.None);
        using var page = JsonDocument.Parse(last.Value.Content);
        Assert.Single(page.RootElement.GetProperty("notes").EnumerateArray());
        Assert.False(page.RootElement.GetProperty("has_more").GetBoolean());
    }

    [Fact]
    public async Task SearchBoundsEscapedUnicodeWithoutLosingPagination()
    {
        await using var temp = TemporaryDatabase.Create();
        var access = new WorkspaceMemoryAccess(new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System), new("owner"));
        for (var i = 0; i < 6; i++)
        {
            var saved = await access.SaveAsync(new(Guid.NewGuid().ToString("N"), 1, null, null, WorkspaceMemoryKind.Fact,
                new string('界', 120), new string('界', 2000), [new string('界', 80)], "", new string('界', 500)), new("Native"), CancellationToken.None);
            Assert.True(saved.Succeeded);
        }
        using var search = JsonDocument.Parse("{\"query\":\"\"}");
        var result = await WorkspaceMemoryTools.CallAsync(access, "memory.search", search.RootElement, "MCP caller", CancellationToken.None);
        Assert.True(Encoding.UTF8.GetByteCount(result.Value.Content) <= 8000);
        using var page = JsonDocument.Parse(result.Value.Content);
        var notes = page.RootElement.GetProperty("notes").GetArrayLength();
        Assert.InRange(notes, 1, 5);
        Assert.True(page.RootElement.GetProperty("has_more").GetBoolean());
        using var next = JsonDocument.Parse(new JsonObject { ["query"] = "", ["offset"] = notes }.ToJsonString());
        var nextResult = await WorkspaceMemoryTools.CallAsync(access, "memory.search", next.RootElement, "MCP caller", CancellationToken.None);
        Assert.True(Encoding.UTF8.GetByteCount(nextResult.Value.Content) <= 8000);
        using var nextPage = JsonDocument.Parse(nextResult.Value.Content);
        Assert.NotEqual(page.RootElement.GetProperty("notes")[0].GetProperty("id").GetString(),
            nextPage.RootElement.GetProperty("notes")[0].GetProperty("id").GetString(), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("memory.search", "{\"query\":null}")]
    [InlineData("memory.read", "{\"id\":null}")]
    [InlineData("memory.search", "{\"query\":\"test\",\"scope\":\"other\"}")]
    [InlineData("memory.save", "{\"title\":\"Missing required fields\"}")]
    [InlineData("memory.brief", "{\"extra\":1}")]
    public async Task MalformedArgumentsAreRejectedBeforeStorage(string tool, string json)
    {
        await using var temp = TemporaryDatabase.Create();
        var access = new WorkspaceMemoryAccess(new SqliteWorkspaceMemoryStore(temp.Database, TimeProvider.System), new("owner"));
        using var document = JsonDocument.Parse(json);
        var result = await WorkspaceMemoryTools.CallAsync(access, tool, document.RootElement, "MCP test", CancellationToken.None);
        Assert.Equal("memory_invalid_arguments", result.StableCode);
        Assert.Empty((await access.QueryAsync(new(), CancellationToken.None)).Notes);
    }

    private static async Task<JsonDocument> Call(HttpClient client, string name, string arguments, string? expectedText = null)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = name,
                ["arguments"] = JsonNode.Parse(arguments),
                ["_meta"] = new JsonObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = "memory-test", ["version"] = "1" },
                    ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
                },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "mcp");
        message.Headers.Add("Mcp-Method", "tools/call"); message.Headers.Add("Mcp-Name", name);
        message.Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(message);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        var json = content.StartsWith('{') ? content : content.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
        using var envelope = JsonDocument.Parse(json);
        var text = envelope.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        if (expectedText is not null) { Assert.Equal(expectedText, text); return JsonDocument.Parse("{}"); }
        return JsonDocument.Parse(text);
    }
}
