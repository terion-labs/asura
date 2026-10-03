using Asura.Core;

namespace Asura.Core.Tests;

public sealed class LiteralSecretSpanTests
{
    [Theory]
    [InlineData("password=fixture-value", "fixture-value")]
    [InlineData("token='two words'", "two words")]
    [InlineData("password=prefix\" secret words \"suffix", "prefix secret words suffix")]
    [InlineData("password=\"p\\u00e4ss\"", "päss")]
    [InlineData("--api-key 'first second'", "first second")]
    [InlineData("authorization: Bearer fixture-auth-value", "fixture-auth-value")]
    [InlineData("fixture-value", "fixture-value")]
    [InlineData("https://alice:fixture-password@host/path", "https://alice:fixture-password@host/path")]
    [InlineData("before password=fixture-value after", "before password=fixture-value after")]
    public void RevealShowsOnlyCompleteAssignmentValue(string original, string expected) =>
        Assert.Equal(expected, LiteralSecretValidator.GetLiteralSecretDisplayValue(original), StringComparer.Ordinal);

    [Theory]
    [InlineData("password=prefix\" secret words \"suffix")]
    [InlineData("password=\"first\"\"second\"")]
    [InlineData("password=first\\ second\\ third")]
    [InlineData("--api-key 'first second'")]
    [InlineData("api_key:=test-value")]
    [InlineData("Authorization: Bearer fixture-auth-value")]
    [InlineData("Authorization: Basic dXNlcjpwYXNz")]
    [InlineData("ghp_fixtureabcdefghijk")]
    [InlineData("https://alice:fixture-password@host/path")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nfixture\n-----END RSA PRIVATE KEY-----")]
    [InlineData("-----BEGIN EC PRIVATE KEY-----\nfixture\n-----END EC PRIVATE KEY-----")]
    public void EntireCredentialIsHiddenWithoutDiscardingSurroundingText(string expression)
    {
        var text = "before " + expression + " after";
        var span = Assert.Single(LiteralSecretValidator.FindLikelyLiteralSecretSpans(text));
        Assert.Equal(expression, text.Substring(span.Start, span.Length), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("multica login --token\nmultica daemon start\nexit")]
    [InlineData("password=null, token=false, api_key=\"\"")]
    [InlineData("Set an API key in settings")]
    [InlineData("terrariumctl cluster join --token '<token>' --wireguard '<bundle>' --yes")]
    [InlineData("Generated Cockpit root password: `/etc/terrarium/secrets/cockpit_root_password`.\nNext instruction")]
    [InlineData("Choose a password:\n  - Generate one\n  - Store it")]
    [InlineData("Set a random `password:`, and `cert: false`.")]
    [InlineData("https://portal.example.com:8080@auth:admins")]
    [InlineData("https://hermes-dash.example.com:9119@auth")]
    [InlineData("https://*.example.com:3000@auth:admins~auth.example.com")]
    public void OrdinaryInstructionsRemainReadable(string text) => Assert.Empty(LiteralSecretValidator.FindLikelyLiteralSecretSpans(text));

    [Theory]
    [InlineData("https://alice:12345@auth")]
    [InlineData("https://example.com:real-password@auth")]
    [InlineData("https://ordinary.example/path https://alice:fixture-password@host/path")]
    public void RealCredentialUrlsStillProtectTheirValues(string text) =>
        Assert.NotEmpty(LiteralSecretValidator.FindLikelyLiteralSecretSpans(text));

    [Theory]
    [InlineData("password=prefix\" secret words \"suffix", "prefix secret words suffix")]
    [InlineData("password=\"p\\u00e4ss\"", "päss")]
    [InlineData("authorization: Bearer fixture-auth-value", "fixture-auth-value")]
    public void DisclosedValueCandidatesIncludeDecodedCredential(string expression, string expected) =>
        Assert.Contains(expected, LiteralSecretValidator.FindLiteralSecretValueCandidates(expression), StringComparer.Ordinal);

    [Fact]
    public void SpacedSourceConcatenationHidesTheWholeExpression()
    {
        const string text = "var password = \"first\" + \"second\";";
        var span = Assert.Single(LiteralSecretValidator.FindLikelyLiteralSecretSpans(text));
        Assert.Equal(text, text.Substring(span.Start, span.Length), StringComparer.Ordinal);
    }

    [Fact]
    public void MultipleCredentialsNeverOverlap()
    {
        const string text = "api_key=first-value; password='second value'; ghp_fixtureabcdefghijk";
        var spans = LiteralSecretValidator.FindLikelyLiteralSecretSpans(text);
        Assert.Equal(3, spans.Count);
        Assert.All(spans.Zip(spans.Skip(1)), pair => Assert.True(pair.First.Start + pair.First.Length <= pair.Second.Start));
    }
}
