using System.Text;
using System.Text.Json;
using Asura.Docking;

namespace Asura.App.Tests;

public sealed class DockLayoutPayloadCodecTests
{
    private const string EmptyLayout = """
        {"kind":"root","id":"root","title":"Root","proportion":null,"minWidth":null,"minHeight":null,"orientation":null,"children":[],"windows":[]}
        """;

    [Theory]
    [InlineData("\"children\":[]", "\"children\":null")]
    [InlineData("\"children\":[]", "\"children\":[null]")]
    [InlineData("\"windows\":[]", "\"windows\":null")]
    [InlineData("\"windows\":[]", "\"windows\":[null]")]
    [InlineData("\"kind\":\"root\"", "\"kind\":\"invalid\"")]
    public void Malformed_layout_graphs_fail_as_format_errors_before_restoration(string original, string replacement)
    {
        var json = EmptyLayout.Replace(original, replacement, StringComparison.Ordinal);
        var serializer = DockLayoutSerializer.Create();
        Assert.Throws<JsonException>(() => DockLayoutSerializer.Validate(json));
        Assert.Throws<JsonException>(() => serializer.Deserialize<object>(json));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<JsonException>(() => serializer.Load<object>(stream));
    }

    [Fact]
    public void Layout_graphs_have_a_total_node_budget()
    {
        var child = EmptyLayout.Replace("\"root\"", "\"document\"", StringComparison.Ordinal);
        var json = EmptyLayout.Replace("\"children\":[]",
            "\"children\":[" + string.Join(',', Enumerable.Repeat(child, 2048)) + "]", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => DockLayoutSerializer.Validate(json));
    }

    [Fact]
    public void Legacy_payloads_are_limited_by_utf8_bytes_before_deserialization()
    {
        var payload = new string(
            '\u00e9',
            (DockLayoutPayloadCodec.MaximumDecodedBytes / 2) + 1);

        var error = Assert.Throws<InvalidDataException>(
            () => DockLayoutPayloadCodec.Decode(payload));

        Assert.Contains("size", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            Encoding.UTF8.GetByteCount(payload)
            > DockLayoutPayloadCodec.MaximumDecodedBytes);
    }

    [Fact]
    public void A_legacy_payload_at_the_utf8_byte_limit_remains_readable()
    {
        var payload = new string('x', DockLayoutPayloadCodec.MaximumDecodedBytes);

        Assert.Same(payload, DockLayoutPayloadCodec.Decode(payload));
    }
}
