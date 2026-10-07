using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// The diff's own navigation keys, and the tooltips that name them.
///
/// They act while the diff has keyboard focus. Option-arrow steps between
/// changes and Option-Shift-arrow between files: Option-Command-arrow already
/// moves focus between panels, and Command-arrow scrolls a list to its ends.
/// Return and Shift-Return in the find field step through matches, as Find
/// Next and Find Previous do from the Edit menu.
/// </summary>
public static class GitDiffShortcuts
{
    public static KeyStroke PreviousChange { get; } = new("ARROWUP", KeyModifiers.Alt);

    public static KeyStroke NextChange { get; } = new("ARROWDOWN", KeyModifiers.Alt);

    public static KeyStroke PreviousFile { get; } = new("ARROWUP", KeyModifiers.Alt | KeyModifiers.Shift);

    public static KeyStroke NextFile { get; } = new("ARROWDOWN", KeyModifiers.Alt | KeyModifiers.Shift);

    public static KeyStroke PreviousMatch { get; } = new("ENTER", KeyModifiers.Shift);

    public static KeyStroke NextMatch { get; } = new("ENTER");

    public static string PreviousChangeTip { get; } = KeySequenceDisplay.WithShortcut("Previous Change", PreviousChange);

    public static string NextChangeTip { get; } = KeySequenceDisplay.WithShortcut("Next Change", NextChange);

    public static string PreviousFileTip { get; } = KeySequenceDisplay.WithShortcut("Previous File", PreviousFile);

    public static string NextFileTip { get; } = KeySequenceDisplay.WithShortcut("Next File", NextFile);

    public static string PreviousMatchTip { get; } = KeySequenceDisplay.WithShortcut("Previous Match", PreviousMatch);

    public static string NextMatchTip { get; } = KeySequenceDisplay.WithShortcut("Next Match", NextMatch);
}
