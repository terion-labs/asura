using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Asura.DesignQa;

/// <summary>
/// The shortcut recorder, the SSH host-key review, and the credential editor,
/// each in the states a person meets them in. Every factory checks the state it
/// claims before the capture, so a route cannot quietly draw a different one.
/// </summary>
internal static class QaReviewDialogs
{
    /// <summary>The recorder as it opens: nothing recorded yet.</summary>
    public static Window ShortcutRecorderIdle() => new ShortcutRecorderDialog();

    /// <summary>
    /// The recorder after ⌘Space reached its capture area through the real
    /// key handler. macOS answers that chord with Spotlight, so on a Mac the
    /// recorder warns that Asura never receives it.
    /// </summary>
    public static Window ShortcutRecorderReserved()
    {
        var dialog = new ShortcutRecorderDialog();
        var recorded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Tag = recorded.Task;
        // The dialog's own Opened handler focuses the capture area first, as
        // it does for a person; a key raised at an unfocused area proves nothing.
        dialog.Opened += (_, _) =>
        {
            try
            {
                var captureArea = dialog.FindControl<SurfaceCard>("CaptureArea")
                    ?? throw new InvalidOperationException("The shortcut capture area is missing.");
                dialog.UpdateLayout();
                if (!captureArea.IsKeyboardFocusWithin)
                {
                    throw new InvalidOperationException("The shortcut capture area did not take focus.");
                }

                captureArea.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Space,
                    KeyModifiers = Avalonia.Input.KeyModifiers.Meta,
                    KeySymbol = " ",
                    Source = captureArea,
                });

                var recordedText = dialog.FindControl<TextBlock>("RecordedShortcutText")!.Text;
                if (string.Equals(recordedText, "No keys recorded", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The shortcut recorder did not record ⌘Space.");
                }

                var warning = dialog.FindControl<TextBlock>("ReservedShortcutText")!;
                if (HostPlatform.Current == HostOperatingSystem.MacOS && !warning.IsVisible)
                {
                    throw new InvalidOperationException(
                        "The shortcut recorder did not warn that macOS reserves ⌘Space.");
                }

                recorded.SetResult();
            }
            catch (Exception exception)
            {
                recorded.SetException(exception);
            }
        };
        return dialog;
    }

    /// <summary>
    /// The first host key Asura has seen for a saved connection: the ordinary
    /// case, so Trust Host Key is the accent default that answers Return.
    /// </summary>
    public static Window SshHostKeyFirstSeen() => RequireConfirm(
        new SshHostKeyReviewDialog(Review(SshHostKeyDisposition.Unknown, trusted: null)),
        destructive: false);

    /// <summary>
    /// A key that differs from the trusted one: possible interception, so
    /// Replace Trusted Key is destructive and nothing answers Return.
    /// </summary>
    public static Window SshHostKeyChanged() => RequireConfirm(
        new SshHostKeyReviewDialog(Review(
            SshHostKeyDisposition.Changed,
            new SshHostKeyIdentity("ssh-ed25519", TrustedFingerprint))),
        destructive: true);

    /// <summary>
    /// A credential that was just stored: nothing uses it yet and it was never
    /// read, with its label as entered.
    /// </summary>
    public static Window SecretEditorNew() => new SecretEditorDialog(new SecretEditorViewModel(
        new SecretMetadataViewModel(
            new SecretRef("qa-deploy-token"),
            "Deploy token",
            nameof(SecretKind.Token),
            "Global",
            Timestamp(QaData.Now),
            "Never",
            new SecretScope(SecretScopeKind.Global),
            "Not used by any saved item",
            0)));

    /// <summary>
    /// A long-standing password two saved connections use, with a replacement
    /// value typed in and ready to rotate.
    /// </summary>
    public static Window SecretEditorExisting()
    {
        var editor = new SecretEditorViewModel(new SecretMetadataViewModel(
            new SecretRef("qa-production-ssh-password"),
            "Production SSH password",
            nameof(SecretKind.Password),
            "Global",
            Timestamp(QaData.Now.AddDays(-146)),
            Timestamp(QaData.Now.AddMinutes(-25)),
            new SecretScope(SecretScopeKind.Global),
            $"Used by: connection {QaData.Connections[5].Value.Name}, connection {QaData.Connections[0].Value.Name}",
            2))
        {
            ReplacementValue = "qa-replacement-value",
        };
        return new SecretEditorDialog(editor);
    }

    // Deterministic SHA-256 fingerprints of fixed sample strings, not real keys.
    private const string PresentedFingerprint = "SHA256:x3Gy7vJWZBNCvezyBYkvqTN07vcbObxZgphaoEEfaiY";
    private const string TrustedFingerprint = "SHA256:Nk7c0PEmjS0FyefzVX4523x4XjLMiRiAsuDTikBZg2k";

    // The settings list's own timestamp format; the editor does not show it.
    private static string Timestamp(DateTimeOffset value) =>
        value.ToString("g", System.Globalization.CultureInfo.InvariantCulture);

    private static SshHostKeyReview Review(
        SshHostKeyDisposition disposition,
        SshHostKeyIdentity? trusted)
    {
        var connection = QaData.Connections[0].Value;
        var endpoint = (ConnectionEndpoint.Ssh)connection.Endpoint;
        return new SshHostKeyReview(
            new SshHostKeyReviewId("qa-host-key-review"),
            connection.Id,
            $"{endpoint.Host}:{endpoint.Port}",
            disposition,
            new SshHostKeyIdentity("ssh-ed25519", PresentedFingerprint),
            trusted,
            QaData.Now.AddMinutes(5));
    }

    /// <summary>
    /// Checks the shown dialog's buttons: a first-seen key's Trust is the
    /// accent default; a changed key's Replace is destructive and no button
    /// answers Return. The window's tag is that check.
    /// </summary>
    private static SshHostKeyReviewDialog RequireConfirm(
        SshHostKeyReviewDialog dialog,
        bool destructive)
    {
        var checkedButtons = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Tag = checkedButtons.Task;
        dialog.Opened += (_, _) =>
        {
            dialog.UpdateLayout();
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            var confirm = buttons.SingleOrDefault(button => string.Equals(button.Name, "ConfirmButton", StringComparison.Ordinal));
            var defaults = buttons.Where(button => button.IsDefault).ToArray();
            var valid = confirm is not null && (destructive
                ? confirm.Classes.Contains("DestructiveButton")
                    && !confirm.Classes.Contains("PrimaryButton")
                    && defaults.Length == 0
                : confirm.Classes.Contains("PrimaryButton")
                    && !confirm.Classes.Contains("DestructiveButton")
                    && defaults.SequenceEqual([confirm]));
            if (valid)
            {
                checkedButtons.SetResult();
            }
            else
            {
                checkedButtons.SetException(new InvalidOperationException(destructive
                    ? "A changed host key must offer a destructive Replace button and no default button."
                    : "A first-seen host key must offer Trust as the only default button."));
            }
        };
        return dialog;
    }
}
