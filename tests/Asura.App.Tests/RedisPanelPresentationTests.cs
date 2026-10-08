using System.Net.Sockets;
using Asura.App.ViewModels;

namespace Asura.App.Tests;

/// <summary>
/// What the Redis panel shows for a key and a failure: only the columns that
/// say something, no rows beside a JSON document, a form heading of its own,
/// and a failure in words with a next step rather than the client's text.
/// </summary>
public sealed class RedisPanelPresentationTests
{
    [Theory]
    [InlineData("hash", "New fields")]
    [InlineData("list", "New values")]
    [InlineData("zset", "New members")]
    [InlineData("stream", "New entry")]
    [InlineData("timeseries", "New sample")]
    public void The_add_form_has_a_heading_of_its_own(string type, string heading)
    {
        var form = RedisKeyForm.For(type);

        Assert.Equal(heading, form.AddFormHeading);
        Assert.NotEqual(form.ActionLabel, form.AddFormHeading, StringComparer.Ordinal);
    }

    [Fact]
    public void A_JSON_document_is_shown_in_its_editor_alone()
    {
        Assert.False(RedisKeyForm.For("json").ShowsValueGrid);
        Assert.True(RedisKeyForm.For("hash").ShowsValueGrid);
    }

    [Fact]
    public async Task A_hash_field_is_not_repeated_as_its_identity()
    {
        using var panel = RedisPanelFixtures.Panel(
            new RedisPanelFixtures.StubSession(null, type: "hash"),
            new RedisPanelFixtures.StoppedClock(DateTimeOffset.UnixEpoch));
        await panel.Initialization;
        panel.SelectedKey = panel.Keys[0];

        Assert.False(panel.ShowsIdentityColumn);
        Assert.True(panel.ShowsFieldColumn);
        Assert.False(panel.ShowsScoreColumn);
    }

    [Fact]
    public async Task A_list_position_keeps_its_column()
    {
        using var panel = RedisPanelFixtures.Panel(
            new RedisPanelFixtures.StubSession(null, type: "list"),
            new RedisPanelFixtures.StoppedClock(DateTimeOffset.UnixEpoch));
        await panel.Initialization;
        panel.SelectedKey = panel.Keys[0];

        Assert.True(panel.ShowsIdentityColumn);
        Assert.False(panel.ShowsFieldColumn);
    }

    [Fact]
    public void An_unreachable_server_reads_as_a_next_step_not_the_client_text()
    {
        var failure = new InvalidOperationException(
            "It was not possible to connect to the redis server(s). ConnectTimeout",
            new SocketException((int)SocketError.ConnectionRefused));

        var message = RedisErrorText.ForConnection(failure);

        Assert.DoesNotContain("ConnectTimeout", message, StringComparison.Ordinal);
        Assert.Contains("Check that it’s running", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_refusal_keeps_the_server_reason_after_the_curated_sentence()
    {
        var message = RedisErrorText.ForCommand(new InvalidOperationException("NOPERM this user has no permissions to run the 'ft.search' command"));

        Assert.StartsWith("This Redis user isn’t allowed to run that command.", message, StringComparison.Ordinal);
        Assert.EndsWith("Redis said: NOPERM this user has no permissions to run the 'ft.search' command", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unexpected_failure_has_a_next_step()
    {
        var message = RedisErrorText.ForCommand(new FormatException("Input string was not in a correct format."));

        Assert.DoesNotContain("Input string", message, StringComparison.Ordinal);
        Assert.Contains("Try again", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_s_own_words_are_shown_as_written()
    {
        var message = RedisErrorText.ForCommand(RedisErrorText.Curated(new ArgumentException("Enter a field name.")));

        Assert.Equal("Enter a field name.", message);
    }
}
