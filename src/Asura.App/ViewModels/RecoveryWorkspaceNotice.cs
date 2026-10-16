using System.Windows.Input;

namespace Asura.App.ViewModels;

/// <summary>
/// What a run in a recovery workspace tells the person, in every window, for
/// as long as it lasts.
///
/// When the person's own profile cannot be opened, Asura opens a separate,
/// empty workspace instead. It used to say so only in the window title, which
/// the window's own title bar hides, and only the first time that workspace
/// was made — so a later fall-back looked exactly like every workspace having
/// been deleted. The notice says the workspaces are safe, why they did not
/// open, and offers to open them.
/// </summary>
public sealed class RecoveryWorkspaceNotice
{
    public RecoveryWorkspaceNotice(string? reason, Action openProfile)
    {
        ArgumentNullException.ThrowIfNull(openProfile);
        Reason = string.IsNullOrWhiteSpace(reason)
            ? "This recovery workspace was opened on purpose."
            : reason;
        OpenProfileCommand = new AsyncActionCommand(
            () =>
            {
                openProfile();
                return Task.CompletedTask;
            },
            static () => true);
    }

    public string Title => "You’re in a Recovery Workspace";

    public string Message =>
        "Your workspaces are safe in your profile, which Asura couldn’t open just now. "
        + "What you do here is kept separately.";

    /// <summary>Why the profile did not open, in the person's terms.</summary>
    public string Reason { get; }

    public string OpenProfileLabel => "Open My Workspaces";

    /// <summary>Quits this run and starts Asura again on the person's own profile.</summary>
    public ICommand OpenProfileCommand { get; }
}
