using Asura.Application;
using Avalonia.Controls;

namespace Asura.App.Views;

internal sealed record ShellClosePresentation(
    Func<Task<bool>> ConfirmLayoutDiscardAsync,
    Func<string, string, Task<bool>> ConfirmDiscardAsync,
    Func<CloseScopeResult.ConfirmationRequired, Task<bool>> ConfirmScopeAsync,
    Func<string, Task> ShowErrorAsync,
    Action RestoreFocus,
    Action FocusCurrentRoute,
    Action CloseWindow)
{
    public ShellClosePresentation WithPrivacyGuard(Func<bool> isLocked) => this with
    {
        ConfirmLayoutDiscardAsync = () => isLocked()
            ? ConfirmDiscardAsync("Discard Unsaved Changes?", "Unsaved changes will be lost.")
            : ConfirmLayoutDiscardAsync(),
        ConfirmDiscardAsync = (title, detail) => isLocked()
            ? ConfirmDiscardAsync("Discard Unsaved Changes?", "Unsaved changes will be lost.")
            : ConfirmDiscardAsync(title, detail),
        ConfirmScopeAsync = confirmation => ConfirmScopeAsync(isLocked()
            ? confirmation with { TargetId = string.Empty, Sessions = [] }
            : confirmation),
        ShowErrorAsync = message => ShowErrorAsync(isLocked()
            ? "Unlock Asura to see why."
            : message),
    };

    public static ShellClosePresentation ForWindow(
        Window owner,
        ShellFocusNavigator focus,
        CloseConfirmationPreference askBeforeClosing) => new(
        () => Confirmations.DiscardChanges().ShowDialog<bool>(owner),
        (title, detail) => Confirmations.DiscardChanges(title, detail)
            .ShowDialog<bool>(owner),
        confirmation => askBeforeClosing.ConfirmAsync(
            () => Confirmations.CloseScope(confirmation),
            owner),
        message => Confirmations.OperationError(message).ShowDialog(owner),
        focus.RestoreAfterCancelledClose,
        focus.FocusCurrentRoute,
        owner.Close);
}

/// <summary>
/// The "Ask before closing" setting as the running-session prompts see it.
/// When it is off they answer yes without asking; when a prompt's "Don't ask
/// again" is ticked and the close goes ahead, it turns itself off. Unsaved
/// edits are a different question and are always asked.
/// </summary>
internal sealed record CloseConfirmationPreference(
    Func<bool> IsAsking,
    Func<Task> StopAskingAsync)
{
    public static CloseConfirmationPreference Always { get; } =
        new(() => true, () => Task.CompletedTask);

    public Task<bool> ConfirmAsync(
        Func<ConfirmationDialog> createDialog,
        Window? owner)
    {
        ArgumentNullException.ThrowIfNull(createDialog);
        return ConfirmAsync(async () =>
        {
            var dialog = createDialog();
            var confirmed = await dialog.ShowAsync(owner);
            return (confirmed, dialog.IsSuppressionChecked);
        });
    }

    /// <summary>
    /// Asks only while the setting says to, and turns the setting off only
    /// when the close was confirmed with "Don't ask again" ticked — ticking it
    /// and cancelling changes nothing.
    /// </summary>
    internal async Task<bool> ConfirmAsync(
        Func<Task<(bool Confirmed, bool DontAskAgain)>> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        if (!IsAsking())
        {
            return true;
        }

        var (confirmed, dontAskAgain) = await ask();
        if (confirmed && dontAskAgain)
        {
            await StopAskingAsync();
        }

        return confirmed;
    }
}
