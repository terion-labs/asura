using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// What a workspace-isolation failure says in the window, the terminal, and
/// the connection test. The default message for each code is worded for the
/// isolation layer ("The persistent workspace isolate could not be
/// prepared."), so it is replaced with the words the workspace settings use
/// and the step that gets past it. A message an isolation provider wrote for
/// one case ("The connection service couldn't start, and Asura couldn't
/// remove its private files.") is kept as written.
/// </summary>
internal static class WorkspaceIsolationText
{
    public static string Message(WorkspaceIsolationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return IsDefaultMessage(error) ? Curated(error.Code) : error.Message;
    }

    private static bool IsDefaultMessage(WorkspaceIsolationError error)
    {
        // Codes such as None have no default message to compare with.
        try
        {
            return string.Equals(
                error.Message,
                WorkspaceIsolationError.Create(error.Code).Message,
                StringComparison.Ordinal);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// The failure without its "Try again." for surfaces that add a retry step
    /// of their own (the connection test and the terminal overlay), so the step
    /// is said once. Null when the message names a different step.
    /// </summary>
    public static string? ReasonBeforeRetry(WorkspaceIsolationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return IsDefaultMessage(error) && Parts(error.Code) is (var reason, TryAgain) ? reason : null;
    }

    internal static string Curated(WorkspaceIsolationErrorCode code)
    {
        var (reason, step) = Parts(code);
        return step.Length == 0 ? reason : $"{reason} {step}";
    }

    private const string TryAgain = "Try again.";

    private static (string Reason, string Step) Parts(WorkspaceIsolationErrorCode code) => code switch
    {
        WorkspaceIsolationErrorCode.RuntimeMissing =>
            ("Isolated workspaces need a runtime that isn’t installed.", "Install it from the workspace’s settings, then try again."),
        WorkspaceIsolationErrorCode.RuntimeVersionTooOld =>
            ("The runtime for isolated workspaces is out of date.", "Update it from the workspace’s settings, then try again."),
        WorkspaceIsolationErrorCode.RuntimeUnavailable =>
            ("The runtime for isolated workspaces isn’t running.", "Start it, then try again."),
        WorkspaceIsolationErrorCode.PrepareFailed =>
            ("Asura couldn’t prepare the workspace environment.", TryAgain),
        WorkspaceIsolationErrorCode.StopFailed =>
            ("Asura couldn’t stop the workspace environment.", TryAgain),
        WorkspaceIsolationErrorCode.WorkingDirectoryNotMounted =>
            ("This folder isn’t shared with the workspace environment.", "Choose a shared folder, or add this one under Shared folders in the workspace’s settings."),
        WorkspaceIsolationErrorCode.UnsupportedConnectionKind =>
            ("This type of connection can’t run in an isolated workspace.", "To use it, turn off isolation for this workspace."),
        WorkspaceIsolationErrorCode.HostCredentialBrokerUnavailable =>
            ("This connection signs in with a helper on this computer that the workspace environment can’t use.", "To use it, turn off isolation for this workspace."),
        WorkspaceIsolationErrorCode.Cancelled =>
            ("Starting the workspace environment was canceled.", string.Empty),
        WorkspaceIsolationErrorCode.Timeout =>
            ("The workspace environment didn’t respond in time.", TryAgain),
        WorkspaceIsolationErrorCode.ExecutableMappingUnavailable =>
            ("This program isn’t available in the workspace environment.", "To run it, turn off isolation for this workspace."),
        WorkspaceIsolationErrorCode.PersistentEnvironmentResetRequired =>
            ("The workspace environment was made with older settings.", "Choose Recreate Environment in the workspace’s settings. Recreating removes packages and files installed in the environment, but not folders shared from this computer."),
        WorkspaceIsolationErrorCode.SshHostKeyTrustUnavailable =>
            ("SSH connections that check the server’s host key can’t run in an isolated workspace yet.", "To use this connection, turn off isolation for this workspace."),
        WorkspaceIsolationErrorCode.ImageNotBootable =>
            ("The chosen image can’t start as a workspace environment.", "Choose an image that boots a full Linux system."),
        _ => ("Asura couldn’t start the workspace environment.", TryAgain),
    };
}
