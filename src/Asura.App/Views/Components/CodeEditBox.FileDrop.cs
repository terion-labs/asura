using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvaloniaEdit.Editing;

namespace Asura.App.Views.Components;

/// <summary>
/// Files dropped from the Finder onto an editable editor that opts in with
/// <see cref="AcceptsFileDrop"/>. A single SQL or
/// text script opens into the statement at the drop point, so a saved query
/// runs without a trip through Open; anything else types its path, as a drop
/// on a Mac text view does.
/// </summary>
public sealed partial class CodeEditBox
{
    /// <summary>
    /// Whether a Finder drop opens a script or types paths here. Off by
    /// default: only the SQL statement editor takes files, so a YAML manifest
    /// or a Redis value is never filled with a dropped script.
    /// </summary>
    public static readonly StyledProperty<bool> AcceptsFileDropProperty =
        AvaloniaProperty.Register<CodeEditBox, bool>(nameof(AcceptsFileDrop));

    private int? _caretBeforeDrag;

    public bool AcceptsFileDrop
    {
        get => GetValue(AcceptsFileDropProperty);
        set => SetValue(AcceptsFileDropProperty, value);
    }

    static CodeEditBox()
    {
        // Drag events only bubble, and the text area inside inserts any drag
        // that carries text, which a Finder or file-manager drag usually does
        // (the file's name or URI). A class handler on the text area runs
        // before its own instance handlers, so for a file payload the editor
        // answers first and marks the event handled: the text area never
        // inserts a second copy. Drags without files pass through untouched,
        // so moving a selection inside the editor still works.
        DragDrop.DragEnterEvent.AddClassHandler<TextArea>((area, e) => OwnerOf(area)?.OnFileDragOver(area, e));
        DragDrop.DragOverEvent.AddClassHandler<TextArea>((area, e) => OwnerOf(area)?.OnFileDragOver(area, e));
        DragDrop.DropEvent.AddClassHandler<TextArea>((area, e) => OwnerOf(area)?.OnFileDrop(area, e));
    }

    private static CodeEditBox? OwnerOf(TextArea area) =>
        area.FindAncestorOfType<CodeEditBox>() is { } box && ReferenceEquals(box.Editor.TextArea, area)
            ? box
            : null;

    private void InitializeFileDrop()
    {
        // A drop beside the text area (its padding) lands on the box itself.
        AddHandler(DragDrop.DragEnterEvent, OnFileDragOver);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnFileDragLeave, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnFileDrop);
    }

    private void SyncAllowDrop() => DragDrop.SetAllowDrop(this, AcceptsFileDrop);

    private bool CarriesFiles(DragEventArgs e) =>
        AcceptsFileDrop && e.DataTransfer.Contains(DataFormat.File);

    private bool CanTakeFiles() => !IsReadOnly && IsEffectivelyEnabled;

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        _ = sender;
        if (!CarriesFiles(e))
        {
            // Any other drag is the text area's to decide.
            return;
        }

        e.Handled = true;
        if (!CanTakeFiles())
        {
            EndFileDragFeedback(restoreCaret: true);
            e.DragEffects = DragDropEffects.None;
            return;
        }

        // The caret follows the pointer while it is over the editor, so the
        // place the file will land is drawn before it is let go.
        _caretBeforeDrag ??= Editor.CaretOffset;
        if (DropOffsetAt(e) is { } offset)
        {
            Editor.CaretOffset = offset;
        }

        Editor.TextArea.Caret.Show();
        FileDropHighlight.IsVisible = true;
        e.DragEffects = DragDropEffects.Copy;
    }

    private void OnFileDragLeave(object? sender, DragEventArgs e)
    {
        _ = sender;
        _ = e;
        EndFileDragFeedback(restoreCaret: true);
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        _ = sender;
        if (!CarriesFiles(e))
        {
            EndFileDragFeedback(restoreCaret: true);
            return;
        }

        // A file payload is this handler's alone, taken or refused: the text
        // area must not insert its text form as well.
        e.Handled = true;
        if (!CanTakeFiles())
        {
            e.DragEffects = DragDropEffects.None;
            EndFileDragFeedback(restoreCaret: true);
            return;
        }

        // Read the payload now: the drag source may release it once the drop
        // event returns.
        var paths = e.DataTransfer.TryGetFiles()?
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .ToArray();
        var offset = DropOffsetAt(e) ?? Editor.CaretOffset;
        EndFileDragFeedback(restoreCaret: false);
        if (paths is not { Length: > 0 })
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        var text = await CodeEditBoxFileDrop.TextForAsync(paths, CancellationToken.None);
        Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
        InsertAtCaret(text);
        FocusEditor();
    }

    /// <summary>
    /// Puts <paramref name="text"/> at the caret as one edit, so a single Undo
    /// takes the whole drop back out, and leaves the caret after it.
    /// </summary>
    internal void InsertAtCaret(string text)
    {
        if (string.IsNullOrEmpty(text) || IsReadOnly)
        {
            return;
        }

        var offset = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
        Editor.Document.BeginUpdate();
        try
        {
            Editor.Document.Insert(offset, text);
        }
        finally
        {
            Editor.Document.EndUpdate();
        }

        Editor.CaretOffset = offset + text.Length;
    }

    private int? DropOffsetAt(DragEventArgs e) =>
        Editor.GetPositionFromPoint(e.GetPosition(Editor)) is { } position
            ? Editor.Document.GetOffset(position.Line, position.Column)
            : null;

    private void EndFileDragFeedback(bool restoreCaret)
    {
        FileDropHighlight.IsVisible = false;
        if (_caretBeforeDrag is { } caret && restoreCaret)
        {
            Editor.CaretOffset = Math.Clamp(caret, 0, Editor.Document.TextLength);
        }

        _caretBeforeDrag = null;
        if (!Editor.TextArea.IsKeyboardFocusWithin)
        {
            Editor.TextArea.Caret.Hide();
        }
    }
}

/// <summary>
/// What a file drop types into an editor, apart from any event: the rule the
/// SQL editor answers a Finder drop with.
/// </summary>
internal static class CodeEditBoxFileDrop
{
    /// <summary>The largest script a drop opens inline; a bigger one types its path.</summary>
    public const long MaximumScriptBytes = 1024 * 1024;

    private static readonly string[] ScriptExtensions = [".sql", ".txt"];

    /// <summary>
    /// The text for dropped local <paramref name="paths"/>: the contents of a
    /// single .sql or .txt file of at most 1 MiB, otherwise the paths
    /// themselves separated by spaces.
    /// </summary>
    public static async Task<string> TextForAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 1 && IsInlineScript(paths[0]))
        {
            try
            {
                return await File.ReadAllTextAsync(paths[0], cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Unreadable after all: fall through to its path.
            }
        }

        return string.Join(' ', paths);
    }

    /// <summary>Whether <paramref name="path"/> is a script small enough to open inline.</summary>
    public static bool IsInlineScript(string path)
    {
        if (!ScriptExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaximumScriptBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
