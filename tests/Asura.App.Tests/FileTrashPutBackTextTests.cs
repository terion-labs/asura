using Asura.App.ViewModels;

namespace Asura.App.Tests;

/// <summary>
/// Undo after a move to the Trash says why an item couldn't come back from
/// where things are now, not with the file system's message and full paths.
/// </summary>
public sealed class FileTrashPutBackTextTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("asura-trash-text-").FullName;

    [Fact]
    public void An_item_already_in_the_folder_is_named_as_the_reason()
    {
        var original = Path.Combine(_root, "notes.txt");
        var trashed = Path.Combine(_root, "trashed-notes.txt");
        File.WriteAllText(original, "new");
        File.WriteAllText(trashed, "old");

        var text = FileRuntimePanelViewModel.DescribePutBackFailure(
            new TrashedItem(original, trashed),
            new IOException($"Cannot create '{original}' because a file with that name already exists."));

        Assert.Equal(
            "“notes.txt” couldn't be put back because an item with that name is already in the folder it came from. Rename or move that item, then choose Undo again.",
            text);
        Assert.DoesNotContain(_root, text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_gone_from_the_Trash_says_so()
    {
        var text = FileRuntimePanelViewModel.DescribePutBackFailure(
            new TrashedItem(Path.Combine(_root, "draft.md"), Path.Combine(_root, "missing.md")),
            new IOException("Could not find file."));

        Assert.Equal(
            "“draft.md” couldn't be put back because it's no longer in the Trash. Refresh the folder to see whether it's already back.",
            text);
    }

    [Fact]
    public void A_permission_failure_names_the_folder_it_came_from()
    {
        var text = FileRuntimePanelViewModel.DescribePutBackFailure(
            new TrashedItem(Path.Combine(_root, "draft.md"), null),
            new UnauthorizedAccessException($"Access to the path '{_root}' is denied."));

        Assert.Contains("permission", text, StringComparison.Ordinal);
        Assert.Contains("then choose Undo again.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_other_failure_still_names_a_next_step()
    {
        var original = Path.Combine(_root, "report.pdf");
        var trashed = Path.Combine(_root, "trashed-report.pdf");
        File.WriteAllText(trashed, "kept");

        var text = FileRuntimePanelViewModel.DescribePutBackFailure(
            new TrashedItem(original, trashed),
            new IOException($"The process cannot access the file '{trashed}'."));

        Assert.Equal(
            "“report.pdf” couldn't be put back from the Trash. Choose Undo again, or open the Trash and put it back from there.",
            text);
        Assert.DoesNotContain(_root, text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
