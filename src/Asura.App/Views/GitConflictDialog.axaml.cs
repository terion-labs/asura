using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class GitConflictDialog : Window
{
    private readonly string _path = "";
    private readonly string _fingerprint = "";

    public GitConflictDialog()
    {
        InitializeComponent();
        Opened += (_, _) => BoundFooter();
        SizeChanged += (_, _) => BoundFooter();
    }

    private void BoundFooter() => FooterActions.MaxWidth = Math.Max(0,
        Bounds.Width - Shell.Padding.Left - Shell.Padding.Right - FooterActions.ItemSpacing);

    public GitConflictDialog(
        GitConflictContent conflict,
        string? initialResult = null,
        GitOperationKind operationKind = GitOperationKind.Normal,
        string? currentBranchLabel = null) : this()
    {
        ArgumentNullException.ThrowIfNull(conflict);
        _path = conflict.Path;
        _fingerprint = conflict.Fingerprint;
        var checkout = string.IsNullOrWhiteSpace(currentBranchLabel) ? "detached checkout" : currentBranchLabel;
        var operation = operationKind switch
        {
            GitOperationKind.CherryPick => "Cherry-pick",
            GitOperationKind.Normal => "Conflict resolution",
            _ => operationKind.ToString(),
        };
        Shell.Subtitle = operationKind == GitOperationKind.Rebase
            ? $"{operation} · {checkout}. Current is the branch being rebased onto; incoming is the commit being replayed."
            : $"{operation} · {checkout}. Current is the checked-out version; incoming is the other side of this conflict.";
        PathLabel.Text = conflict.IsBinary ? $"{conflict.Path} · binary conflict; choose a version or deletion" : conflict.Path;
        CurrentIdentity.Text = $"Revision: {conflict.CurrentRevision ?? "not available"}\nFile object, stage 2: {FileObject(conflict.CurrentObjectId, conflict.CurrentExists)}";
        IncomingIdentity.Text = $"Revision: {conflict.IncomingRevision ?? "not associated with a known incoming commit"}\nFile object, stage 3: {FileObject(conflict.IncomingObjectId, conflict.IncomingExists)}";
        BaseIdentity.Text = $"Common ancestor file object, stage 1: {FileObject(conflict.BaseObjectId, conflict.BaseExists)}";
        BaseInput.Text = conflict.BaseExists ? conflict.Base : "This file did not exist in the common ancestor.";
        CurrentInput.Text = conflict.CurrentExists ? conflict.Current : "This file was deleted in the current version.";
        IncomingInput.Text = conflict.IncomingExists ? conflict.Incoming : "This file was deleted in the incoming version.";
        ResultInput.Text = initialResult ?? conflict.Current;
        CurrentButton.Content = conflict.CurrentExists ? "Keep current" : "Accept current deletion";
        IncomingButton.Content = conflict.IncomingExists ? "Keep incoming" : "Accept incoming deletion";
        BothButton.IsEnabled = !conflict.IsBinary && conflict.CurrentExists && conflict.IncomingExists;
        ResultInput.IsEnabled = !conflict.IsBinary;
        SaveButton.IsEnabled = !conflict.IsBinary;
    }

    private static string FileObject(string? objectId, bool exists) => !exists ? "absent" : objectId ?? "not available";

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnCurrent(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Current, ExpectedFingerprint: _fingerprint));
    private void OnIncoming(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Incoming, ExpectedFingerprint: _fingerprint));
    private void OnDelete(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Delete, ExpectedFingerprint: _fingerprint));
    private void OnSave(object? sender, RoutedEventArgs e) => Close(new GitConflictRequest(_path, GitConflictResolution.Edited, ResultInput.Text ?? "", _fingerprint));
    private void OnBoth(object? sender, RoutedEventArgs e) => ResultInput.Text = (CurrentInput.Text ?? "").TrimEnd('\r', '\n') + "\n" + IncomingInput.Text;
}
