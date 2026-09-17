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
