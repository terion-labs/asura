namespace Asura.Application.Tests;

/// <summary>
/// Tool titles are what an approval card and a run's record show, so they
/// are written in the reader's words rather than the host's limits.
/// </summary>
public sealed class AgentToolCatalogCopyTests
{
    [Fact]
    public void The_browser_script_tool_is_named_for_what_it_does()
    {
        Assert.True(BuiltInAgentTools.Catalog.TryGet(BuiltInAgentTools.BrowserEvaluate, out var tool));
        Assert.Equal("Run script in browser page", tool!.Title);
    }

    [Fact]
    public void No_tool_title_names_an_implementation_limit()
    {
        Assert.DoesNotContain(
            BuiltInAgentTools.Catalog.Tools,
            tool => tool.Title.Contains("bounded", StringComparison.OrdinalIgnoreCase));
    }
}
