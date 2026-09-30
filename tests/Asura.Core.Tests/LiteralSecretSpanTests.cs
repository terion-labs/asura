using Asura.Core;

namespace Asura.Core.Tests;

public sealed class LiteralSecretSpanTests
{
    [Theory]
    [InlineData("password=prefix\" secret words \"suffix")]
    [InlineData("password=\"first\"\"second\"")]
    [InlineData("password=first\\ second\\ third")]
    [InlineData("--api-key 'first second'")]
    [InlineData("api_key:=test-value")]
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
    public void OrdinaryInstructionsRemainReadable(string text) => Assert.Empty(LiteralSecretValidator.FindLikelyLiteralSecretSpans(text));

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
