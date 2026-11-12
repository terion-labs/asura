using System.Text.Json;

namespace Asura.DesktopAutomation.Tests;

/// <summary>
/// ad_wait takes a name and looks it up on every poll, so a satisfied wait
/// can describe a same-named process that started after Asura's listing. A
/// window-title wait reports the window it matched, and that window must be
/// the resolved process's; the session then checks that the name still
/// selects exactly that process.
/// </summary>
public sealed class AgentDesktopWaitAttributionTests
{
    private const uint Resolved = 700;

    [Theory]
    [InlineData("""{"found":true,"window":{"id":"w-1","title":"Done","pid":700}}""")]
    [InlineData("""{"found":true,"elapsed_ms":12}""")]
    public void A_match_in_the_resolved_process_or_one_that_names_no_window_is_attributed(string answer)
    {
        Assert.True(AgentDesktopRunSession.IsMatchedWindowOf(Parse(answer), Resolved));
    }

    [Theory]
    [InlineData("""{"found":true,"window":{"id":"w-1","title":"Done","pid":701}}""")]
    [InlineData("""{"found":true,"window":{"id":"w-1","title":"Done"}}""")]
    [InlineData("""{"found":true,"window":{"id":"w-1","pid":"700"}}""")]
    [InlineData("""{"found":true,"window":{"id":"w-1","pid":-700}}""")]
    [InlineData("""{"found":true,"window":{"id":"w-1","pid":700.5}}""")]
    [InlineData("""{"found":true,"window":"w-1"}""")]
    [InlineData("""{"found":true,"window":null}""")]
    [InlineData("""[{"found":true}]""")]
    public void A_match_in_another_process_or_in_none_that_can_be_read_is_not(string answer)
    {
        Assert.False(AgentDesktopRunSession.IsMatchedWindowOf(Parse(answer), Resolved));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
