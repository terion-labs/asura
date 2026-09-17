namespace Asura.Application;

/// <summary>Closed terminal mutations that require pod execution authority instead of host command authority.</summary>
public static class KubernetesTerminalTools
{
    public static string ForPod(string toolName) => toolName switch
    {
        BuiltInAgentTools.TerminalSendText => BuiltInAgentTools.KubernetesTerminalSendText,
        BuiltInAgentTools.TerminalPaste => BuiltInAgentTools.KubernetesTerminalPaste,
        BuiltInAgentTools.TerminalSubmitText => BuiltInAgentTools.KubernetesTerminalSubmitText,
        BuiltInAgentTools.TerminalSendKeys => BuiltInAgentTools.KubernetesTerminalSendKeys,
        BuiltInAgentTools.TerminalSendChord => BuiltInAgentTools.KubernetesTerminalSendChord,
        BuiltInAgentTools.TerminalSendMouse => BuiltInAgentTools.KubernetesTerminalSendMouse,
        BuiltInAgentTools.TerminalInterrupt => BuiltInAgentTools.KubernetesTerminalInterrupt,
        BuiltInAgentTools.TerminalResize => BuiltInAgentTools.KubernetesTerminalResize,
        BuiltInAgentTools.TerminalScrollViewport => BuiltInAgentTools.KubernetesTerminalScrollViewport,
        BuiltInAgentTools.TerminalJumpToRenderedHistory => BuiltInAgentTools.KubernetesTerminalJumpToRenderedHistory,
        _ => toolName,
    };

    public static string BaseToolName(string toolName) => toolName switch
    {
        BuiltInAgentTools.KubernetesTerminalSendText => BuiltInAgentTools.TerminalSendText,
        BuiltInAgentTools.KubernetesTerminalPaste => BuiltInAgentTools.TerminalPaste,
        BuiltInAgentTools.KubernetesTerminalSubmitText => BuiltInAgentTools.TerminalSubmitText,
        BuiltInAgentTools.KubernetesTerminalSendKeys => BuiltInAgentTools.TerminalSendKeys,
        BuiltInAgentTools.KubernetesTerminalSendChord => BuiltInAgentTools.TerminalSendChord,
        BuiltInAgentTools.KubernetesTerminalSendMouse => BuiltInAgentTools.TerminalSendMouse,
        BuiltInAgentTools.KubernetesTerminalInterrupt => BuiltInAgentTools.TerminalInterrupt,
        BuiltInAgentTools.KubernetesTerminalResize => BuiltInAgentTools.TerminalResize,
        BuiltInAgentTools.KubernetesTerminalScrollViewport => BuiltInAgentTools.TerminalScrollViewport,
        BuiltInAgentTools.KubernetesTerminalJumpToRenderedHistory => BuiltInAgentTools.TerminalJumpToRenderedHistory,
        _ => toolName,
    };

    public static bool IsPodTool(string toolName) => !string.Equals(toolName, BaseToolName(toolName), StringComparison.Ordinal);
    public static bool IsInputTool(string toolName) => !string.Equals(toolName, ForPod(toolName), StringComparison.Ordinal);
}
