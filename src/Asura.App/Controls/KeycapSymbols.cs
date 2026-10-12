using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Asura.App.Controls;

/// <summary>
/// Draws the key symbols in a text block's shortcut as monochrome text:
///
/// <code>
/// &lt;Setter Property="controls:KeycapSymbols.AsText" Value="True" /&gt;
/// </code>
///
/// The interface font has letters and digits but few of ⌃⌥⇧⌘ ↩ ⎋ ⌫, and
/// Avalonia's fallback for a missing character takes the best face among those
/// already loaded before it asks the platform. A fallback family named after
/// the interface font is not reached while that sweep finds anything, so the
/// face depended on what the window had drawn so far: in the command palette
/// and a database context menu ↩ came out of Apple Color Emoji as a picture,
/// even with its text-presentation selector, ⎋ out of a face nothing else
/// used, and ⌘ beside it out of a third. Here each run of symbols names its
/// face outright and everything else keeps the block's own font. The block's
/// <see cref="TextBlock.Text"/> stays as it was, so it is still what
/// assistive technology reads; the runs only change how it is drawn.
/// </summary>
internal static class KeycapSymbols
{
    public static readonly AttachedProperty<bool> AsTextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("AsText", typeof(KeycapSymbols));

    private static readonly FontFamily? SymbolFamily = CreateSymbolFamily(HostPlatform.Current);

    static KeycapSymbols()
    {
        AsTextProperty.Changed.AddClassHandler<TextBlock>(OnAsTextChanged);
    }

    public static bool GetAsText(TextBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.GetValue(AsTextProperty);
    }

    public static void SetAsText(TextBlock block, bool value)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.SetValue(AsTextProperty, value);
    }

    /// <summary>
    /// <paramref name="text"/> as runs: each stretch of key symbols in
    /// <paramref name="symbolFamily"/>, the rest in the inherited font. Empty
    /// when there is no symbol to draw, so plain text stays plain.
    /// </summary>
    internal static IReadOnlyList<Run> Runs(string? text, FontFamily symbolFamily, HostOperatingSystem host)
    {
        ArgumentNullException.ThrowIfNull(symbolFamily);
        if (string.IsNullOrEmpty(text) || !text.Any(character => ShortcutText.IsKeySymbol(character, host)))
        {
            return [];
        }

        var runs = new List<Run>();
        var start = 0;
        while (start < text.Length)
        {
            var symbol = ShortcutText.IsKeySymbol(text[start], host);
            var end = start + 1;
            while (end < text.Length && ShortcutText.IsKeySymbol(text[end], host) == symbol)
            {
                end++;
            }

            var run = new Run(text[start..end]);
            if (symbol)
            {
                run.FontFamily = symbolFamily;
            }

            runs.Add(run);
            start = end;
        }

        return runs;
    }

    internal static FontFamily? CreateSymbolFamily(HostOperatingSystem host) =>
        ShortcutText.SymbolFontFamilies(host) is { Count: > 0 } names
            ? new FontFamily(string.Join(", ", names))
            : null;

    private static void OnAsTextChanged(TextBlock block, AvaloniaPropertyChangedEventArgs change)
    {
        // A new Text clears the block's runs before this hears of it, so the
        // runs are rebuilt from every Text the block is given.
        block.PropertyChanged -= OnBlockPropertyChanged;
        if (change.NewValue is true)
        {
            block.PropertyChanged += OnBlockPropertyChanged;
        }

        Apply(block);
    }

    private static void OnBlockPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (sender is TextBlock block && change.Property == TextBlock.TextProperty)
        {
            Apply(block);
        }
    }

    private static void Apply(TextBlock block)
    {
        var runs = GetAsText(block) && SymbolFamily is { } family
            ? Runs(block.Text, family, HostPlatform.Current)
            : [];
        if (runs.Count == 0)
        {
            if (block.Inlines is { Count: > 0 } stale)
            {
                stale.Clear();
            }

            return;
        }

        var inlines = block.Inlines ??= [];
        inlines.Clear();
        inlines.AddRange(runs);
    }
}
