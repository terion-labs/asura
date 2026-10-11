using Asura.App.Controls;
using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// What the window says when one of its own actions is refused: opening an
/// editor, switching a panel's connection, queuing a transfer, running a
/// command, or closing sessions.
///
/// These sites used to show the refusing exception's message, which is
/// whatever the lowest layer that threw happened to write ("Sequence contains
/// no matching element", "The operation deadline has elapsed."). A message
/// Asura wrote for people — a refusal marked readable — is still shown as
/// written; anything else is traced to secret-safe diagnostics and replaced
/// by a sentence that says what didn't happen and what to do next.
/// </summary>
internal static class ShellFailureText
{
    /// <summary>Opening the connection editor, or saving and applying what it returned.</summary>
    public static string ConnectionEditor(Exception exception) =>
        FailureReason.Refusal(
            "shell.connection-editor.refused",
            exception,
            "Asura couldn’t finish editing the connection. Check its settings, then try again.");

    /// <summary>Pointing a panel at another connection, or at a new one.</summary>
    public static string PanelConnection(Exception exception) =>
        FailureReason.Refusal(
            "shell.panel-connection.refused",
            exception,
            "Asura couldn’t switch the panel to that connection. Choose the connection again from the panel’s connection menu.");

    /// <summary>A browser panel's back, forward, reload, stop, or address navigation.</summary>
    public static string BrowserNavigation(Exception exception) =>
        FailureReason.Refusal(
            "shell.browser.navigation.refused",
            exception,
            "The browser couldn’t do that right now. Wait for the page to finish loading, then try again.");

    /// <summary>Queuing a dropped, uploaded, or edited file transfer.</summary>
    public static string FileTransfer(Exception exception) =>
        FailureReason.Refusal(
            "shell.file-transfer.refused",
            exception,
            "Asura couldn’t queue the transfer. Check the destination, then try again.");

    /// <summary>Opening or saving an AI provider from Settings.</summary>
    public static string AiProviderEditor(Exception exception) =>
        FailureReason.Refusal(
            "shell.ai-provider-editor.refused",
            exception,
            "Asura couldn’t open or save the AI provider. Try again.");

    /// <summary>Opening or saving an MCP server from Settings.</summary>
    public static string McpServerEditor(Exception exception) =>
        FailureReason.Refusal(
            "shell.mcp-server-editor.refused",
            exception,
            "Asura couldn’t open or save the MCP server. Try again.");

    /// <summary>Opening the saved-screen editor.</summary>
    public static string SavedScreenEditor(Exception exception) =>
        FailureReason.Refusal(
            "shell.saved-screen-editor.refused",
            exception,
            "Asura couldn’t open the screen. Try again.");

    /// <summary>Saving the workspace editor.</summary>
    public static string WorkspaceSave(Exception exception) =>
        FailureReason.Refusal(
            "shell.workspace.save.refused",
            exception,
            "Asura couldn’t save the workspace. Check its settings, then save again.");

    /// <summary>A command from a menu, the command palette, or a shortcut.</summary>
    public static string Command(Exception exception) =>
        FailureReason.Refusal(
            "shell-command.refused",
            exception,
            "Asura couldn’t finish that command. Try again.");

    /// <summary>
    /// The session host couldn't close the sessions of a window, tab, or
    /// panel. Its message is written for the host's own log, so the code
    /// chooses the sentence.
    /// </summary>
    public static string CloseFailed(HostError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            HostErrorCode.DeadlineExceeded =>
                "A session didn’t close in time, so it’s still open. Try closing it again.",
            HostErrorCode.ResynchronizationRequired =>
                "Asura couldn’t confirm that every session closed. Try closing again.",
            _ => "Asura couldn’t close the sessions, so they’re still open. Try closing them again.",
        };
    }

    /// <summary>
    /// A session that answered a close with something other than closing. The
    /// host's detail ("The terminal engine failed while closing.") is for its
    /// log; the outcome chooses the sentence.
    /// </summary>
    public static string SessionCloseFailed(SessionCloseOutcome outcome) => outcome switch
    {
        SessionCloseOutcome.ConfirmationRequired =>
            "A session still has work running, so it wasn’t closed. Close it again to confirm.",
        _ => "A session couldn’t be closed, so it’s still open. Try closing it again.",
    };
}
