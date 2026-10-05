using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Asura.App.Views.Components;

/// <summary>Virtualized diff text uses the same shipped language grammars as source previews.</summary>
public sealed class GitDiffTextBlock : TextBlock
{
    public static readonly StyledProperty<string> SyntaxTextProperty = AvaloniaProperty.Register<GitDiffTextBlock, string>(nameof(SyntaxText), "");
    public static readonly StyledProperty<string?> FileNameProperty = AvaloniaProperty.Register<GitDiffTextBlock, string?>(nameof(FileName));
    public static readonly StyledProperty<string?> PeerTextProperty = AvaloniaProperty.Register<GitDiffTextBlock, string?>(nameof(PeerText));
    public static readonly StyledProperty<bool> ShowInvisiblesProperty = AvaloniaProperty.Register<GitDiffTextBlock, bool>(nameof(ShowInvisibles));
    public static readonly StyledProperty<bool> HighlightWordsProperty = AvaloniaProperty.Register<GitDiffTextBlock, bool>(nameof(HighlightWords), true);
    private static readonly Dictionary<ThemeName, Registry> Registries = [];
    private bool _rendering;

    public string SyntaxText { get => GetValue(SyntaxTextProperty); set => SetValue(SyntaxTextProperty, value); }
    public string? FileName { get => GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public string? PeerText { get => GetValue(PeerTextProperty); set => SetValue(PeerTextProperty, value); }
    public bool ShowInvisibles { get => GetValue(ShowInvisiblesProperty); set => SetValue(ShowInvisiblesProperty, value); }
    public bool HighlightWords { get => GetValue(HighlightWordsProperty); set => SetValue(HighlightWordsProperty, value); }

    public GitDiffTextBlock()
    {
        ActualThemeVariantChanged += (_, _) => Present();
        AttachedToVisualTree += (_, _) => Present();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SyntaxTextProperty || change.Property == FileNameProperty || change.Property == PeerTextProperty
            || change.Property == ShowInvisiblesProperty || change.Property == HighlightWordsProperty)
        {
            Present();
        }
    }

    private void Present()
    {
        if (_rendering)
        {
            return;
        }

        _rendering = true;
        try
        {
            Inlines!.Clear();
            var text = SyntaxText;
            var (changedStart, changedEnd) = ChangedRange(text, HighlightWords ? PeerText : null);
            var themeName = ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus;
            var options = TextMateRegistries.For(themeName);
            var language = SourcePreviewGrammar.ResolveExtension(FileName) is { } extension ? options.GetLanguageByExtension(extension) : null;
            var registry = GetRegistry(themeName, options);
            var grammar = language is null ? null : registry.LoadGrammar(options.GetScopeByLanguageId(language.Id));
            var tokens = grammar?.TokenizeLine(text, null, TimeSpan.FromMilliseconds(5)).Tokens;
            if (tokens is null || tokens.Length == 0)
            {
                Append(text, 0, text.Length, null, changedStart, changedEnd);
                return;
            }

            foreach (var token in tokens)
            {
                var start = Math.Clamp(token.StartIndex, 0, text.Length);
                var end = Math.Clamp(token.EndIndex, start, text.Length);
                var rule = registry.GetTheme().Match(token.Scopes).FirstOrDefault();
                var color = rule is null ? null : registry.GetTheme().GetColor(rule.foreground);
                Append(text, start, end, color is { Length: > 0 } ? new SolidColorBrush(Color.Parse(color)) : null, changedStart, changedEnd);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Inlines!.Clear();
            Inlines.Add(new Run(SyntaxText));
        }
        finally
        {
            _rendering = false;
        }
    }

    private static Registry GetRegistry(ThemeName name, RegistryOptions options)
    {
        if (!Registries.TryGetValue(name, out var registry))
        {
            registry = new Registry(options);
            Registries.Add(name, registry);
        }

        return registry;
    }

    private void Append(string text, int start, int end, IBrush? foreground, int changedStart, int changedEnd)
    {
        for (var position = start; position < end;)
        {
            var changed = position >= changedStart && position < changedEnd;
            var boundary = changed ? Math.Min(end, changedEnd) : position < changedStart ? Math.Min(end, changedStart) : end;
            var value = text[position..boundary];
            if (ShowInvisibles)
            {
                value = value.Replace(" ", "·", StringComparison.Ordinal).Replace("\t", "→   ", StringComparison.Ordinal).Replace("\r", "␍", StringComparison.Ordinal);
            }

            var run = new Run(value);
            if (foreground is not null)
            {
                run.Foreground = foreground;
            }

            if (changed)
            {
                run.FontWeight = FontWeight.Bold;
                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
            }

            Inlines!.Add(run);
            position = boundary;
        }
    }

    internal static (int Start, int End) ChangedRange(string text, string? peer)
    {
        if (peer is null)
        {
            return (text.Length, text.Length);
        }

        var start = 0;
        while (start < Math.Min(text.Length, peer.Length) && text[start] == peer[start])
        {
            start++;
        }

        var end = text.Length;
        var peerEnd = peer.Length;
        while (end > start && peerEnd > start && text[end - 1] == peer[peerEnd - 1])
        {
            end--;
            peerEnd--;
        }

        return (start, end);
    }
}
