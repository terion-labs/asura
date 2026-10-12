namespace Asura.Application;

public enum BrowserEditCommand
{
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    SelectAll,
}

/// <summary>
/// A browser view that runs the standard Edit menu commands in its focused
/// frame. On macOS the menu bar receives Command-C and the other editing key
/// equivalents before the view does, so the menu forwards them here.
/// </summary>
public interface IBrowserEditCommandTarget
{
    /// <summary>
    /// Runs the command as user input. Returns false when the browser is not
    /// ready or user input is currently withheld from it.
    /// </summary>
    bool ExecuteEditCommand(BrowserEditCommand command);

    /// <summary>
    /// The page's selected text, read on demand for Use Selection for Find.
    /// Null when nothing is selected or the page can't be read.
    /// </summary>
    Task<string?> ReadSelectedTextAsync() => Task.FromResult<string?>(null);
}
