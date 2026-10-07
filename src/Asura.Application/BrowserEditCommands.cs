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
}
