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
    [InlineData(KubernetesErrorCode.InvalidConfiguration, "Edit the connection")]
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
}
