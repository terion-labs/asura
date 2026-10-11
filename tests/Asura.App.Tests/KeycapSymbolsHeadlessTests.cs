using Asura.App.Controls;
using Asura.App.Views.Overlays;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Asura.App.Tests;

/// <summary>
/// Shortcut text draws its key symbols as monochrome text. Left to the
/// renderer's fallback, the face for a symbol was whichever loaded face
/// answered first: design QA showed ↩ in the palette footer and beside "Open
/// Cell Editor…" as an Apple Color Emoji tile, and ⎋ and ⌘ in faces nothing
/// else used. These render through real Skia with those faces already loaded,
/// the state that produced it.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class KeycapSymbolsHeadlessTests
{
    private static readonly bool IsMac = HostPlatform.Current == HostOperatingSystem.MacOS;

    [Fact]
    public Task Symbols_and_words_split_into_runs_with_the_symbols_in_the_symbol_face() =>
        RunHeadlessAsync(() =>
        {
            var family = new FontFamily("Lucida Grande, Apple Symbols");
            var runs = KeycapSymbols.Runs("↑↓ to choose · ↩︎ to open", family, HostOperatingSystem.MacOS);

            Assert.Equal(
                ["↑↓", " to choose · ", "↩︎", " to open"],
                runs.Select(run => run.Text),
                StringComparer.Ordinal);
            Assert.Same(family, runs[0].FontFamily);
            Assert.Same(family, runs[2].FontFamily);
            Assert.False(runs[1].IsSet(TextElement.FontFamilyProperty), "Words lost the block's own font.");
            Assert.False(runs[3].IsSet(TextElement.FontFamilyProperty), "Words lost the block's own font.");
        });

    [Theory]
    [InlineData("Settings")]
    [InlineData("")]
    [InlineData(null)]
    public Task Text_without_a_key_symbol_stays_plain(string? text) =>
        RunHeadlessAsync(() =>
            Assert.Empty(KeycapSymbols.Runs(text, new FontFamily("Lucida Grande"), HostOperatingSystem.MacOS)));

    [Fact]
    public Task Text_notation_hosts_draw_shortcuts_in_the_interface_font() =>
        RunHeadlessAsync(() =>
        {
            Assert.Null(KeycapSymbols.CreateSymbolFamily(HostOperatingSystem.Windows));
            Assert.Null(KeycapSymbols.CreateSymbolFamily(HostOperatingSystem.Linux));
            Assert.Empty(KeycapSymbols.Runs("Ctrl+←", new FontFamily("Segoe UI"), HostOperatingSystem.Windows));
        });

    [Fact]
    public Task Every_mac_key_symbol_draws_from_a_text_face() =>
        RunHeadlessAsync(() =>
        {
            if (!IsMac)
            {
                return;
            }

            var keys = new[]
            {
                "ESCAPE", "ENTER", "BACKSPACE", "DELETE", "TAB", "HOME", "END", "PAGEUP", "PAGEDOWN",
                "ARROWLEFT", "ARROWRIGHT", "ARROWUP", "ARROWDOWN",
            };
            var all = Asura.Core.KeyModifiers.Control | Asura.Core.KeyModifiers.Alt
                | Asura.Core.KeyModifiers.Shift | Asura.Core.KeyModifiers.Meta;
            var text = string.Join(" ", keys.Select(key => ShortcutText.Format(new KeyStroke(key, all), HostOperatingSystem.MacOS)));

            var block = new TextBlock { Text = text };
            KeycapSymbols.SetAsText(block, true);
            using var window = Show(block);

            AssertSymbolsDrawAsText(block);
        });

    [Fact]
    public Task The_command_palette_footer_and_close_keycap_draw_their_keys_as_text() =>
        RunHeadlessAsync(() =>
        {
            var palette = new CommandPaletteView();
            using var window = Show(palette);

            var blocks = palette.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var footer = Assert.Single(blocks, block => block.Text == CommandPaletteView.KeyHintText);
            var close = Assert.Single(blocks, block => block.Text == CommandPaletteView.CloseKeyText);
            Assert.True(KeycapSymbols.GetAsText(footer), "The palette footer leaves its keys to the renderer's fallback.");
            Assert.True(KeycapSymbols.GetAsText(close), "The close keycap leaves ⎋ to the renderer's fallback.");
            AssertSymbolsDrawAsText(footer);
            AssertSymbolsDrawAsText(close);
        });

    [Fact]
    public Task A_palette_row_shortcut_draws_its_keys_as_text() =>
        RunHeadlessAsync(() =>
        {
            var palette = new CommandPaletteView();
            using var window = Show(palette);

            var sample = Sample();
            var template = Assert.IsAssignableFrom<Avalonia.Controls.Templates.IDataTemplate>(
                palette.FindControl<ListBox>("LauncherSearchResultList")!.ItemTemplate);
            var row = template.Build(sample)!;
            row.DataContext = sample;
            using var rowWindow = Show(row);

            var shortcut = Assert.Single(
                row.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == sample.TrailingText);
            Assert.True(KeycapSymbols.GetAsText(shortcut), "A palette row's shortcut leaves its keys to the renderer's fallback.");
            AssertSymbolsDrawAsText(shortcut);
        });

    [Fact]
    public Task A_context_menu_shortcut_draws_return_as_text() =>
        RunHeadlessAsync(() =>
        {
            var item = new MenuItem
            {
                Header = "Open Cell Editor…",
                InputGesture = new KeyGesture(Key.Enter, AvaloniaKeyModifiers.Meta),
            };
            using var window = Show(new StackPanel { Children = { item } });

            var text = item.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => string.Equals(block.Name, "PART_InputGestureText", StringComparison.Ordinal));
            Assert.True(KeycapSymbols.GetAsText(text), "A menu item's shortcut leaves its keys to the renderer's fallback.");
            if (IsMac)
            {
                Assert.Equal("⌘↩︎", text.Text);
            }

            AssertSymbolsDrawAsText(text);
        });

    [Fact]
    public Task A_new_shortcut_redraws_its_symbols_and_plain_text_drops_them() =>
        RunHeadlessAsync(() =>
        {
            var block = new TextBlock { Text = "⌘K" };
            KeycapSymbols.SetAsText(block, true);
            using var window = Show(block);

            block.Text = "⇧⌘↩︎";
            window.Window.UpdateLayout();
            Assert.Equal("⇧⌘↩︎", block.Text);
            AssertSymbolsDrawAsText(block);

            block.Text = "Space";
            window.Window.UpdateLayout();
            Assert.True(block.Inlines is null or { Count: 0 }, "Plain text kept the previous shortcut's runs.");
            Assert.Equal("Space", string.Concat(Runs(block).Select(run => run.Text.ToString())));
        });

    private static Asura.App.ViewModels.LauncherSearchResultViewModel Sample() =>
        new(
            new Asura.App.ViewModels.LauncherSearchTarget.Screen(ScreenId.New()),
            FluentIcons.Common.Symbol.Settings,
            "Application",
            "Open Cell Editor…",
            "Database",
            ShortcutText.Format(new KeyStroke("ENTER", Asura.Core.KeyModifiers.Meta), HostPlatform.Current),
            IsAvailable: true,
            UnavailableReason: null,
            SearchTerms: []);

    /// <summary>
    /// Every run that holds a key symbol is drawn from a symbol face, never the
    /// emoji face, and every character in it has a glyph of its own.
    /// </summary>
    private static void AssertSymbolsDrawAsText(TextBlock block)
    {
        var runs = Runs(block);
        Assert.Equal(block.Text, string.Concat(runs.Select(run => run.Text.ToString())));
        if (!IsMac)
        {
            Assert.True(block.Inlines is null or { Count: 0 }, "Text notation was split into symbol runs.");
            return;
        }

        var faces = ShortcutText.SymbolFontFamilies(HostOperatingSystem.MacOS);
        var symbolRuns = runs
            .Where(run => run.Text.ToString().Any(character => ShortcutText.IsKeySymbol(character, HostOperatingSystem.MacOS)))
            .ToArray();
        Assert.NotEmpty(symbolRuns);
        foreach (var run in symbolRuns)
        {
            var family = run.GlyphRun.GlyphTypeface.FamilyName;
            Assert.True(
                faces.Contains(family, StringComparer.OrdinalIgnoreCase),
                $"'{run.Text}' in '{block.Text}' drew from {family}.");
            Assert.All(run.GlyphRun.GlyphInfos, glyph => Assert.NotEqual(0, glyph.GlyphIndex));
        }
    }

    private static ShapedTextRun[] Runs(TextBlock block) =>
        [.. block.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()];

    private static ShownWindow Show(Control content)
    {
        var window = new Window { Width = 720, Height = 480, Content = content };
        window.Show();
        window.UpdateLayout();
        return new ShownWindow(window);
    }

    private static async Task RunHeadlessAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(AgentAttachmentHeadlessApplication));
        var completed = await session.Dispatch(
            () =>
            {
                // The faces the renderer's own fallback reached for in design
                // QA, loaded first so the sweep it runs could find them.
                foreach (var face in new[] { "Apple Color Emoji", "Apple SD Gothic Neo", "Andale Mono" })
                {
                    _ = FontManager.Current.TryGetGlyphTypeface(new Typeface(face), out _);
                }

                assertion();
                return true;
            },
            timeout.Token);
        Assert.True(completed);
    }

    private sealed class ShownWindow(Window window) : IDisposable
    {
        public Window Window { get; } = window;

        public void Dispose() => Window.Close();
    }
}
