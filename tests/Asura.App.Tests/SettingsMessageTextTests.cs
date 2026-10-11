using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// Settings pages speak in words people recognize: a credential's kind and
/// owner rather than enum values and identifiers, the saved appearance's
/// platform by its name, and storage failures as what happened and what to
/// do next rather than the store's own row names.
/// </summary>
public sealed class SettingsMessageTextTests
{
    [Fact]
    public void Credential_kinds_and_owners_are_words()
    {
        foreach (var kind in Enum.GetValues<SecretKind>())
        {
            var label = SecretSettingsViewModel.KindLabel(kind);
            Assert.DoesNotMatch("[a-z][A-Z]", label);
        }

        foreach (var scope in Enum.GetValues<SecretScopeKind>())
        {
            var label = SecretSettingsViewModel.ScopeLabel(scope);
            Assert.DoesNotMatch("[a-z][A-Z]", label);
        }

        Assert.Equal("API key", SecretSettingsViewModel.KindLabel(SecretKind.ApiKey));
        Assert.Equal("For an AI provider", SecretSettingsViewModel.ScopeLabel(SecretScopeKind.AiProvider));
    }

    [Fact]
    public void The_saved_appearance_names_its_mode_and_platform()
    {
        Assert.Equal("Match system", AppearanceSettingsViewModel.ModeLabel(AppearanceMode.System));
        Assert.Equal("macOS Liquid Glass", AppearanceSettingsViewModel.ProfileLabel(PlatformProfile.MacOsLiquidGlass));
        Assert.Equal("Windows 11", AppearanceSettingsViewModel.ProfileLabel(PlatformProfile.Windows11));
        foreach (var profile in Enum.GetValues<PlatformProfile>())
        {
            Assert.DoesNotContain(
                AppearanceSettingsViewModel.ProfileLabel(profile),
                ["MacOsClassic", "MacOsLiquidGlass", "Windows11", "Gnome", "Kde"],
                StringComparer.Ordinal);
        }
    }

    [Theory]
    [InlineData(ApplicationRunErrorCode.StorageFailure)]
    [InlineData(ApplicationRunErrorCode.StorageUnavailable)]
    public void An_unreadable_default_AI_setting_says_how_to_turn_agents_back_on(ApplicationRunErrorCode code)
    {
        var text = DefaultAgentPolicySettingsViewModel.LoadFailureText(
            new ApplicationRunError(code, "The stored default agent policy is unreadable."));

        Assert.DoesNotContain("stored default agent policy", text, StringComparison.Ordinal);
        Assert.Contains("save them to turn agent capabilities back on", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApplicationRunErrorCode.Cancelled, "was canceled")]
    [InlineData(ApplicationRunErrorCode.StorageUnavailable, "Try again in a moment")]
    [InlineData(ApplicationRunErrorCode.StorageFailure, "Try again")]
    public void A_failed_default_AI_save_is_described_by_its_code(ApplicationRunErrorCode code, string expected)
    {
        var text = DefaultAgentPolicySettingsViewModel.SaveFailureText(
            new ApplicationRunError(code, "The default agent policy could not be saved."));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("agent policy", text, StringComparison.Ordinal);
    }
}
