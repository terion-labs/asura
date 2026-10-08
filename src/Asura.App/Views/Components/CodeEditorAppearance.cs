using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;

namespace Asura.App.Views.Components;

/// <summary>
/// What every code view in the app shares beyond its grammar: links in the
/// accent's text colour rather than the editor's stock blue, wrapped lines
/// that hang under the first so a continuation never reads as a new line,
/// and a line-number gutter that follows the text when wrapping re-flows it.
/// </summary>
internal static class CodeEditorAppearance
{
    /// <summary>How far a wrapped line hangs, in characters of the editor's font.</summary>
    private const double HangingIndentCharacters = 4;

    /// <summary>Roughly the advance of a monospaced character, as a share of its size.</summary>
    private const double MonospacedAdvance = 0.6;

    /// <summary>The hanging indent for a font size, in device-independent pixels.</summary>
    internal static double HangingIndent(double fontSize) =>
        fontSize * MonospacedAdvance * HangingIndentCharacters;

    /// <summary>Applies the shared look; call again when the theme or the font size changes.</summary>
    public static void Apply(TextEditor editor, Control host)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(host);
        if (host.TryFindResource("ShellAccentTextBrush", host.ActualThemeVariant, out var value)
            && value is IBrush accent)
        {
            editor.TextArea.TextView.LinkTextForegroundBrush = accent;
        }

        // A continuation keeps the line's own indentation and then hangs a
        // little further, the way a wrapped line reads in Xcode.
        editor.Options.InheritWordWrapIndentation = true;
        editor.Options.WordWrapIndentation = HangingIndent(editor.FontSize);
    }

    /// <summary>
    /// Redraws the gutter whenever the visible lines are rebuilt. When the
    /// vertical scroll bar comes or goes the text re-wraps to the new width,
    /// and a gutter drawn against the old lines numbers the wrong rows.
    /// </summary>
    public static void KeepGutterInStep(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        // The hanging indent is measured in characters, so it follows the
        // text size.
        editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TemplatedControl.FontSizeProperty)
            {
                editor.Options.WordWrapIndentation = HangingIndent(editor.FontSize);
            }
        };
        editor.TextArea.TextView.VisualLinesChanged += (_, _) =>
        {
            foreach (var margin in editor.TextArea.LeftMargins)
            {
                margin.InvalidateVisual();
            }
        };
    }
}
