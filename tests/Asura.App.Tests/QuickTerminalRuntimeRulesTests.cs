using Asura.App;

namespace Asura.App.Tests;

public sealed class QuickTerminalRuntimeRulesTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void FocusLossHonorsVisibilityAndTheDurableDismissSetting(
        bool isVisible,
        bool hideOnFocusLoss,
        bool expected) =>
        Assert.Equal(
            expected,
            QuickTerminalRuntimeRules.ShouldDismissForFocusLoss(
                isVisible,
                hideOnFocusLoss));

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, true, false, 1)]
    [InlineData(true, true, true, 1)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, false, true, 2)]
    public void EscapeReachesTheTerminalUnlessAsuraPromptsOrTheSettingHides(
        bool isVisible,
        bool pendingInteractionCancelled,
        bool hideOnEscape,
        int expected) =>
        Assert.Equal(
            (QuickTerminalEscapeAction)expected,
            QuickTerminalRuntimeRules.ResolveEscape(
                isVisible,
                pendingInteractionCancelled,
                hideOnEscape));

    [Fact]
    public void HiddenSessionIsReusedOnlyWhenRestoreIsEnabled()
    {
        Assert.False(QuickTerminalRuntimeRules.ShouldResetAfterHide(restoreLastSession: true));
        Assert.True(QuickTerminalRuntimeRules.ShouldResetAfterHide(restoreLastSession: false));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, false, false, false)]
    public void DefinitionAndRestorePolicyChangesResetAtTheSafeLifecyclePoint(
        bool definitionsChanged,
        bool previousRestore,
        bool nextRestore,
        bool isVisible,
        bool expected) =>
        Assert.Equal(
            expected,
            QuickTerminalRuntimeRules.ShouldResetForDefinitionOrPolicyChange(
                definitionsChanged,
                previousRestore,
                nextRestore,
                isVisible));
}
