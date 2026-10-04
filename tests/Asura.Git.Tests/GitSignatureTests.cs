using Asura.Application;
using Asura.Core;

namespace Asura.Git.Tests;

public sealed class GitSignatureTests
{
    [Theory]
    [InlineData("G\nAlice λ\nkey-123\nfingerprint-456\n", "Signature valid", "Alice λ", "key-123", "fingerprint-456")]
    [InlineData("E\n\n\n\n", "Signature could not be verified", "", "", "")]
    [InlineData("N\n\n\n\n", "Not signed", "", "", "")]
    public async Task SignatureSeparatesVerificationStatusAndSignerMetadata(string output, string status, string signer, string key, string fingerprint)
    {
        var executor = new Executor(output);
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var result = Assert.IsType<GitResult<GitSignature>.Success>(await client.ReadSignatureAsync(
            new(BuiltInConnections.Local, "/repo"), "HEAD", CancellationToken.None)).Value;

        Assert.Equal(new GitSignature(status, signer, key, fingerprint), result);
        Assert.Equal(signer.Length == 0 ? status : status + " · " + signer + " · " + key, result.Summary);
        var command = Assert.Single(executor.Commands);
        Assert.Contains("HEAD", command.Arguments, StringComparer.Ordinal);
        Assert.Contains("--no-patch", command.Arguments, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SigningKeyListAssociatesOnlyPrimaryFingerprintsWithTheirIdentities()
    {
        var executor = new Executor(
            "sec:::::::::\n" +
            "fpr:::::::::primary-one:\n" +
            "uid:::::::::Alice λ <alice@example.test>:\n" +
            "ssb:::::::::\n" +
            "fpr:::::::::subkey-one:\n" +
            "uid:::::::::Subkey identity must not become another choice:\n" +
            "sec:::::::::\n" +
            "fpr:::::::::primary-two:\n" +
            "uid:::::::::Bob <bob@example.test>:\n");
        var client = new GitRepositoryClient(executor, TimeProvider.System);

        var keys = Assert.IsType<GitResult<IReadOnlyList<GitSigningKey>>.Success>(await client.ReadSigningKeysAsync(
            new(BuiltInConnections.Local, "/repo with space"), CancellationToken.None)).Value;

        Assert.Equal([new("primary-one", "Alice λ <alice@example.test>"), new GitSigningKey("primary-two", "Bob <bob@example.test>")], keys);
        var command = Assert.Single(executor.Commands);
        Assert.Contains("/repo with space", command.Arguments, StringComparer.Ordinal);
        Assert.Contains("gpg", command.Arguments, StringComparer.Ordinal);
        Assert.Contains("--list-secret-keys", command.Arguments, StringComparer.Ordinal);
    }

    private sealed class Executor(string output) : IConnectionCommandExecutor
    {
        public List<ConnectionCommand> Commands { get; } = [];

        public ValueTask<ConnectionCommandResult> ExecuteAsync(ConnectionCommand request, CancellationToken cancellationToken)
        {
            Commands.Add(request);
            return ValueTask.FromResult(new ConnectionCommandResult(ConnectionCommandOutcome.Exited, 0, output));
        }

        public ValueTask<ConnectionBinaryCommandResult> ExecuteBinaryAsync(ConnectionBinaryCommand request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ConnectionStreamingCommandResult<T>> ExecuteStreamingAsync<T>(ConnectionBinaryCommand request,
            Func<Stream, CancellationToken, ValueTask<T>> consumeOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
