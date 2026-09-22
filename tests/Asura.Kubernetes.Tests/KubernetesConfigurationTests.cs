using System.Text.Json;
using Asura.Application;

namespace Asura.Kubernetes.Tests;

public sealed class KubernetesConfigurationTests
{
    [Fact]
    public void InvalidAuthenticationFailsWithoutEchoingCredentialMaterial()
    {
        KubernetesRequestException error = Assert.Throws<KubernetesRequestException>(() =>
            new KubernetesClientSession(new KubernetesResolvedConnection(new Uri("https://cluster.test"), BearerToken: "private-token\r\n")));
        Assert.DoesNotContain("private-token", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void YamlPreservesUnknownFieldsAndDocumentBoundaries()
    {
        const string yaml = """
            apiVersion: example.test/v1
            kind: Widget
            spec:
              unknown:
                enabled: true
                replicas: 3
                quoted: "03"
                values: [hello, false]
            ---
            apiVersion: v1
            kind: ConfigMap
            data:
              multiline: |
                first line
                second line
            """;
        IReadOnlyList<string> documents = KubernetesYaml.ToJsonDocuments(yaml);
        Assert.Equal(2, documents.Count);
        using JsonDocument first = JsonDocument.Parse(documents[0]);
        JsonElement unknown = first.RootElement.GetProperty("spec").GetProperty("unknown");
        Assert.True(unknown.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, unknown.GetProperty("replicas").GetInt32());
        Assert.Equal("03", unknown.GetProperty("quoted").GetString());
        using JsonDocument second = JsonDocument.Parse(documents[1]);
        Assert.Contains("second line", second.RootElement.GetProperty("data").GetProperty("multiline").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a: 1\na: 2")]
    [InlineData("a: &value hello\nb: *value")]
    [InlineData("a: !!str 123")]
    [InlineData("a: 0123")]
    [InlineData("a: -0123")]
    [InlineData("a: yes")]
    [InlineData("a: on")]
    [InlineData("a: 18446744073709551616")]
    public void YamlRejectsAmbiguousOrUnboundedStructures(string yaml)
    {
        Assert.Throws<KubernetesRequestException>(() => KubernetesYaml.ToJsonDocuments(yaml));
    }

    [Fact]
    public void ReadingKubeconfigPlansDoesNotResolveFilesOrExecuteCommands()
    {
        IReadOnlyList<KubernetesKubeconfigPlan> plans = KubernetesKubeconfigReader.Read(Config("""
            exec:
              apiVersion: client.authentication.k8s.io/v1
              command: definitely-not-an-installed-executable
              interactiveMode: Never
              args: ["argument with spaces"]
            """));
        KubernetesKubeconfigPlan plan = Assert.Single(plans);
        Assert.Equal("demo", plan.ContextName);
        Assert.Equal("testing", plan.Connection.Namespace);
        Assert.Equal("argument with spaces", Assert.Single(plan.Exec!.Arguments));
        Assert.Equal(64, plan.Exec.Fingerprint.Length);
        Assert.DoesNotContain("argument with spaces", plan.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UntrustedExecIsRejectedBeforeStartingAnyExecutable()
    {
        KubernetesKubeconfigPlan plan = Assert.Single(KubernetesKubeconfigReader.Read(Config("""
            exec:
              apiVersion: client.authentication.k8s.io/v1
              command: definitely-not-an-installed-executable
              interactiveMode: Never
            """)));
        var resolver = new KubernetesCredentialResolver(plan);
        KubernetesRequestException error = await Assert.ThrowsAsync<KubernetesRequestException>(async () => await resolver.ResolveAsync(CancellationToken.None));
        Assert.Equal(KubernetesErrorCode.Unauthorized, error.Code);
        Assert.Contains("trust", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrustedCredentialCommandUsesOwningEnvironmentsExecutableLookup()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var directory = Directory.CreateTempSubdirectory("asura-kube-exec-");
        try
        {
            var executable = Path.Combine(directory.FullName, "credential-helper");
            await File.WriteAllTextAsync(executable, """
                #!/bin/sh
                test "$1" = 'argument with spaces' || exit 1
                printf '%s' '{"apiVersion":"client.authentication.k8s.io/v1","kind":"ExecCredential","status":{"token":"fixture-token"}}'
                """);
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var plan = Assert.Single(KubernetesKubeconfigReader.Read(Config("""
                exec:
                  apiVersion: client.authentication.k8s.io/v1
                  command: fixture-credential-helper-not-on-path
                  interactiveMode: Never
                  args: ["argument with spaces"]
                """)));
            var lookups = 0;
            string? Find(string command)
            {
                Assert.Equal(plan.Exec!.Command, command);
                lookups++;
                return executable;
            }
            await Assert.ThrowsAsync<KubernetesRequestException>(() =>
                new KubernetesCredentialResolver(plan, findExecutable: Find).ResolveAsync(CancellationToken.None).AsTask());
            Assert.Equal(0, lookups);
            var resolver = new KubernetesCredentialResolver(plan, plan.Exec!.Fingerprint, Find);
            var connection = await resolver.ResolveAsync(CancellationToken.None);
            Assert.Equal("fixture-token", connection.BearerToken);
            Assert.Equal(1, lookups);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task MissingTrustedHelperExplainsTheExecutionEnvironmentWithoutExposingConfiguration()
    {
        var plan = Assert.Single(KubernetesKubeconfigReader.Read(Config("""
            exec:
              apiVersion: client.authentication.k8s.io/v1
              command: private-helper-path
              interactiveMode: Never
              args: [private-argument]
            """)));
        var resolver = new KubernetesCredentialResolver(plan, plan.Exec!.Fingerprint, _ => null);
        var error = await Assert.ThrowsAsync<KubernetesRequestException>(() => resolver.ResolveAsync(CancellationToken.None).AsTask());
        Assert.Equal(KubernetesErrorCode.InvalidConfiguration, error.Code);
        Assert.Contains("install its CLI", error.Message, StringComparison.Ordinal);
        Assert.Contains("Isolated workspaces", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExecFingerprintChangesWithArgumentsAndEndpoint()
    {
        const string user = """
            exec:
              apiVersion: client.authentication.k8s.io/v1
              command: helper
              interactiveMode: Never
              args: [one]
            """;
        string original = Assert.Single(KubernetesKubeconfigReader.Read(Config(user))).Exec!.Fingerprint;
        string changedArgument = Assert.Single(KubernetesKubeconfigReader.Read(Config(user.Replace("[one]", "[two]", StringComparison.Ordinal)))).Exec!.Fingerprint;
        string changedEndpoint = Assert.Single(KubernetesKubeconfigReader.Read(Config(user).Replace("cluster.test", "other.test", StringComparison.Ordinal))).Exec!.Fingerprint;
        Assert.NotEqual(original, changedArgument, StringComparer.Ordinal);
        Assert.NotEqual(original, changedEndpoint, StringComparer.Ordinal);
    }

    [Fact]
    public void RelativePathsRequireAndPreserveSourceDirectory()
    {
        string yaml = Config("client-certificate: identity/cert.pem\nclient-key: identity/key.pem");
        Assert.Throws<KubernetesRequestException>(() => KubernetesKubeconfigReader.Read(yaml));
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "asura-kube-config"));
        KubernetesKubeconfigPlan plan = Assert.Single(KubernetesKubeconfigReader.Read(yaml, directory));
        Assert.Equal(Path.Combine(directory, "identity", "cert.pem"), plan.ClientCertificatePath);
    }

    private static string Config(string user) => """
        apiVersion: v1
        kind: Config
        clusters:
        - name: cluster
          cluster:
            server: https://cluster.test
        contexts:
        - name: demo
          context:
            cluster: cluster
            user: account
            namespace: testing
        users:
        - name: account
          user:
        """ + "\n" + string.Join('\n', user.Split('\n').Select(static line => "    " + line));
}
