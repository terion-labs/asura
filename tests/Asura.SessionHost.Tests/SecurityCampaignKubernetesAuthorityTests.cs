using Asura.Application;

namespace Asura.SessionHost.Tests;

public sealed class SecurityCampaignKubernetesAuthorityTests
{
    [Fact(DisplayName = "authority.kubernetes.terminal.send_text broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.send_text")]
    public Task SendTextAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalSendText,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalSendText));

    [Fact(DisplayName = "authority.kubernetes.terminal.paste broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.paste")]
    public Task PasteAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalPaste,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalPaste));

    [Fact(DisplayName = "authority.kubernetes.terminal.submit_text broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.submit_text")]
    public Task SubmitTextAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalSubmitText,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalSubmitText));

    [Fact(DisplayName = "authority.kubernetes.terminal.send_keys broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.send_keys")]
    public Task SendKeysAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalSendKeys,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalSendKeys));

    [Fact(DisplayName = "authority.kubernetes.terminal.send_chord broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.send_chord")]
    public Task SendChordAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalSendChord,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalSendChord));

    [Fact(DisplayName = "authority.kubernetes.terminal.send_mouse broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.send_mouse")]
    public Task SendMouseAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalSendMouse,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalSendMouse));

    [Fact(DisplayName = "authority.kubernetes.terminal.interrupt broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.interrupt")]
    public Task InterruptAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalInterrupt,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalInterrupt));

    [Fact(DisplayName = "authority.kubernetes.terminal.resize broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.resize")]
    public Task ResizeAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalResize,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalResize));

    [Fact(DisplayName = "authority.kubernetes.terminal.scroll_viewport broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.scroll_viewport")]
    public Task ScrollViewportAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalScrollViewport,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalScrollViewport));

    [Fact(DisplayName = "authority.kubernetes.terminal.jump_to_rendered_history broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.terminal.jump_to_rendered_history")]
    public Task JumpToRenderedHistoryAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesTerminalJumpToRenderedHistory,
        static () => new AgentTerminalSessionHostTests().Action_with_a_different_material_binding_is_denied_before_engine_dispatch(),
        static () => new AgentTerminalSessionHostTests().SecurityCampaignPodActionAsync(BuiltInAgentTools.KubernetesTerminalJumpToRenderedHistory));

    [Fact(DisplayName = "authority.kubernetes.preview broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.preview")]
    public Task PreviewAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesPreview,
        static () => new AgentKubernetesSessionHostTests().StaleVersionRejectsCommitAndUncertainDispatchCannotReplay(false),
        static () => new AgentKubernetesSessionHostTests().MutationCommitRequiresSeparateExactApprovalAndConsumesPreview());

    [Fact(DisplayName = "authority.kubernetes.commit broker host and sink")]
    [Trait("SecurityCampaignCase", "authority.kubernetes.commit")]
    public Task CommitAsync() => SecurityCampaignAuthorityTests.RunAsync(
        BuiltInAgentTools.KubernetesCommit,
        static () => new AgentKubernetesSessionHostTests().StaleVersionRejectsCommitAndUncertainDispatchCannotReplay(false),
        static () => new AgentKubernetesSessionHostTests().MutationCommitRequiresSeparateExactApprovalAndConsumesPreview());

}
