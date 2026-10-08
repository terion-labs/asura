using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;

namespace Asura.App.Views.Components;

/// <summary>
/// What every code view in the app shares beyond its grammar: links in the
/// accent's text colour rather than the editor's stock blue, and a
/// line-number gutter that follows the text when wrapping re-flows it.
///
/// Wrapped code has no hanging indent to offer: the text formatter
/// underneath does not indent continuation lines, so a wrapped line would
/// start at the margin and read as a new one. Code therefore keeps its lines
/// whole and scrolls sideways; only prose wraps.
/// </summary>
internal static class CodeEditorAppearance
{
    /// <summary>Applies the shared look; call again when the theme changes.</summary>
    public static void Apply(TextEditor editor, Control host)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(host);
        if (host.TryFindResource("ShellAccentTextBrush", host.ActualThemeVariant, out var value)
            && value is IBrush accent)
        {
            editor.TextArea.TextView.LinkTextForegroundBrush = accent;
        }
    }

    /// <summary>
    /// Redraws the gutter whenever the visible lines are rebuilt. When the
    /// vertical scroll bar comes or goes the text re-wraps to the new width,
    /// and a gutter drawn against the old lines numbers the wrong rows.
    /// </summary>
    public static void KeepGutterInStep(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.TextArea.TextView.VisualLinesChanged += (_, _) =>
        {
            foreach (var margin in editor.TextArea.LeftMargins)
            {
                margin.InvalidateVisual();
            }
        };
    }
}
