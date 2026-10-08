using Asura.App.Controls;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitConflictDialog : DialogWindow
{
    private readonly string _path = "";
    private readonly string _fingerprint = "";
    private readonly string _fileName = "";

    public GitConflictDialog()
    {
        InitializeComponent();
        GitDialogFooter.Span(Shell, FooterRow);
    }

    public GitConflictDialog(
        GitConflictContent conflict,
        string? initialResult = null,
        GitOperationKind operationKind = GitOperationKind.Normal,
        string? currentBranchLabel = null) : this()
    {
        ArgumentNullException.ThrowIfNull(conflict);
        _path = conflict.Path;
        _fingerprint = conflict.Fingerprint;
        // What Current and Incoming mean is said on the tabs themselves, where
        // the choice is made, rather than in a line under the title.
        var checkout = string.IsNullOrWhiteSpace(currentBranchLabel) ? "the checked-out branch" : $"“{currentBranchLabel}”";
        ToolTip.SetTip(CurrentTab, operationKind == GitOperationKind.Rebase
            ? $"The branch {checkout} is being rebased onto"
            : $"The version on {checkout}");
        ToolTip.SetTip(IncomingTab, operationKind == GitOperationKind.Rebase
            ? "The commit being replayed"
            : "The version from the other side of the conflict");
        PathLabel.Text = conflict.IsBinary ? $"{conflict.Path} · binary file; keep one version or delete it" : conflict.Path;
        _fileName = Path.GetFileName(conflict.Path);
        CurrentIdentity.Text = $"Revision: {conflict.CurrentRevision ?? "not available"}\nFile object, stage 2: {FileObject(conflict.CurrentObjectId, conflict.CurrentExists)}";
        IncomingIdentity.Text = $"Revision: {conflict.IncomingRevision ?? "not associated with a known incoming commit"}\nFile object, stage 3: {FileObject(conflict.IncomingObjectId, conflict.IncomingExists)}";
        BaseIdentity.Text = $"Common ancestor file object, stage 1: {FileObject(conflict.BaseObjectId, conflict.BaseExists)}";
        BaseInput.Text = conflict.BaseExists ? conflict.Base : "This file did not exist in the common ancestor.";
        CurrentInput.Text = conflict.CurrentExists ? conflict.Current : "This file was deleted in the current version.";
        IncomingInput.Text = conflict.IncomingExists ? conflict.Incoming : "This file was deleted in the incoming version.";
        ResultInput.Text = initialResult ?? conflict.Current;
        CurrentButton.Content = conflict.CurrentExists ? "Keep Current" : "Accept Current Deletion";
        IncomingButton.Content = conflict.IncomingExists ? "Keep Incoming" : "Accept Incoming Deletion";
        BothButton.IsEnabled = !conflict.IsBinary && conflict.CurrentExists && conflict.IncomingExists;
        ResultInput.IsEnabled = !conflict.IsBinary;
        SaveButton.IsEnabled = !conflict.IsBinary;
    }

    private static string FileObject(string? objectId, bool exists) => !exists ? "absent" : objectId ?? "not available";

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnCurrent(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Current, ExpectedFingerprint: _fingerprint));
    private void OnIncoming(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Incoming, ExpectedFingerprint: _fingerprint));
    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (await new ConfirmationDialog(new ConfirmationDialogOptions
        {
            Heading = $"Delete “{_fileName}”?",
            Detail = "The file is removed from the working tree and the deletion is marked as the resolution.",
            Notice = "Both versions stay in the repository’s history.",
            ConfirmLabel = "Delete File",
            Intent = ConfirmationIntent.Destructive,
        }).ShowDialog<bool>(this))
        {
            Close(new GitConflictRequest(_path, GitConflictResolution.Delete, ExpectedFingerprint: _fingerprint));
        }
    }
    private void OnSave(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Edited, ResultInput.Text ?? "", _fingerprint));
    private void OnBoth(object? sender, RoutedEventArgs e) => ResultInput.Text = (CurrentInput.Text ?? "").TrimEnd('\r', '\n') + "\n" + IncomingInput.Text;
}
