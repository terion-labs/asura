using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// A failed Kubernetes request reads as what happened and what to do next,
/// never as the client layer's own text: HTTP codes, raw error-code names, or
/// internal limits.
/// </summary>
public sealed class KubernetesErrorTextTests
{
    public static TheoryData<KubernetesErrorCode> Codes()
    {
        var data = new TheoryData<KubernetesErrorCode>();
        foreach (var code in Enum.GetValues<KubernetesErrorCode>())
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_failure_is_a_sentence_without_the_client_layers_words(KubernetesErrorCode code)
    {
        var failure = new KubernetesRequestException(
            code,
            $"The Kubernetes API returned HTTP 503. Kubernetes request failed: {code}.");

        foreach (var activity in Enum.GetValues<KubernetesActivity>())
        {
            var text = KubernetesErrorText.Describe(failure, activity);

            Assert.DoesNotContain("HTTP", text, StringComparison.Ordinal);
            Assert.DoesNotContain("request failed", text, StringComparison.Ordinal);
            if (code.ToString().Length > 8)
            {
                // Short code names ("Conflict") are ordinary words; long ones are identifiers.
                Assert.DoesNotContain(code.ToString(), text, StringComparison.Ordinal);
            }

            Assert.EndsWith(".", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(KubernetesErrorCode.Unauthorized, "Sign in again")]
    [InlineData(KubernetesErrorCode.Forbidden, "allowed namespace")]
    [InlineData(KubernetesErrorCode.ConnectionFailed, "couldn’t reach the cluster")]
    [InlineData(KubernetesErrorCode.Conflict, "Refresh, review it again")]
    [InlineData(KubernetesErrorCode.ResponseTooLarge, "label selector")]
    [InlineData(KubernetesErrorCode.InvalidConfiguration, "Check the namespace and label selector")]
    public void A_request_failure_names_the_next_step_for_its_code(KubernetesErrorCode code, string expected)
    {
        var text = KubernetesErrorText.Describe(new KubernetesRequestException(code, "x"));

        Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Helm_browsing_without_Helm_says_to_install_it()
    {
        var text = KubernetesErrorText.Describe(
            new KubernetesRequestException(KubernetesErrorCode.Unsupported, "Helm release browsing is unavailable."),
            KubernetesActivity.Helm);

        Assert.Contains("Install Helm", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forward_that_ends_says_to_start_it_again()
    {
        var text = KubernetesErrorText.Describe(
            new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "The Kubernetes port-forward listener failed."),
            KubernetesActivity.PortForward);

        Assert.StartsWith("The forward stopped", text, StringComparison.Ordinal);
        Assert.Contains("Start it again", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_kubeconfig_says_to_check_the_path()
    {
        var text = KubernetesErrorText.Describe(
            new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "A Kubernetes credential file exceeds its size limit."),
            KubernetesActivity.ConfigurationReview);

        Assert.Contains("kubeconfig", text, StringComparison.Ordinal);
        Assert.DoesNotContain("size limit", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_settings_that_cant_be_used_say_to_edit_the_connection()
    {
        var text = KubernetesErrorText.Describe(
            new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "The selected Kubernetes context is unavailable."),
            KubernetesActivity.Connect);

        Assert.Contains("Edit the connection", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Quote numeric-looking strings with leading zeroes to preserve their meaning.")]
    [InlineData("Explicit YAML tags and anchors are not supported.")]
    [InlineData("The manifest does not match the selected resource identity.")]
    public void A_manifest_the_cluster_layer_refuses_points_at_the_manifest(string refusal)
    {
        var failure = new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, refusal);

        var text = KubernetesErrorText.Describe(failure, KubernetesActivity.ManifestEdit);

        Assert.StartsWith("Asura can’t apply this manifest.", text, StringComparison.Ordinal);
        Assert.Contains("Quote values", text, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(KubernetesErrorText.IsRefusedChange(failure, KubernetesActivity.ManifestEdit));
    }

    [Theory]
    [InlineData("Upgrade requires an exact chart version.")]
    [InlineData("Helm values must be a mapping.")]
    [InlineData("Upgrade requires an OCI chart reference pinned with @sha256 and its complete 64-character digest.")]
    public void Helm_change_input_the_cluster_layer_refuses_points_at_the_input(string refusal)
    {
        var failure = new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, refusal);

        var text = KubernetesErrorText.Describe(failure, KubernetesActivity.HelmChange);

        Assert.Contains("chart version", text, StringComparison.Ordinal);
        Assert.Contains("YAML or JSON mapping", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Edit the connection", text, StringComparison.Ordinal);
        Assert.DoesNotContain(refusal, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_connecting_blames_the_connection_settings()
    {
        var failure = new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "x");
        foreach (var activity in Enum.GetValues<KubernetesActivity>())
        {
            var text = KubernetesErrorText.Describe(failure, activity);

            Assert.Equal(activity == KubernetesActivity.Connect, text.Contains("Edit the connection", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_refused_metrics_service_says_to_choose_another()
    {
        var text = KubernetesErrorText.Describe(
            new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "VictoriaMetrics requires a numeric account or account:project tenant."),
            KubernetesActivity.Metrics);

        Assert.Contains("Choose another metrics service", text, StringComparison.Ordinal);
        Assert.DoesNotContain("VictoriaMetrics", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_refused_change_leaves_the_table_status_alone()
    {
        var invalid = new KubernetesRequestException(KubernetesErrorCode.InvalidConfiguration, "x");
        var unreachable = new KubernetesRequestException(KubernetesErrorCode.ConnectionFailed, "x");

        Assert.True(KubernetesErrorText.IsRefusedChange(invalid, KubernetesActivity.HelmChange));
        Assert.True(KubernetesErrorText.IsRefusedChange(invalid, KubernetesActivity.NodeMaintenance));
        Assert.False(KubernetesErrorText.IsRefusedChange(invalid, KubernetesActivity.Request));
        Assert.False(KubernetesErrorText.IsRefusedChange(unreachable, KubernetesActivity.ManifestEdit));
    }
}
