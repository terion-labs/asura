using Asura.App.Controls;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentIcons.Avalonia;

namespace Asura.App.Tests;

/// <summary>
/// The macOS 26 metrics the shell is held to, as the real application styles
/// draw them: menus whose items are one height inside one even inset, rows no
/// shorter than the host's, a type ramp that never renders under Caption, and
/// notices that are neutral cards with only their glyph in the info tone.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class MacOsMetricsHeadlessTests
{
    /// <summary>
    /// Every item of a menu is the same height, and the menu's inset is the
    /// same above the first item, under the last and beside them: Fluent's
    /// vertical-only scroller margin put a larger gap at the top and bottom
    /// than at the sides, and each item carried a control's height plus
    /// padding of its own.
    /// </summary>
    [Theory]
    [InlineData("flyout")]
    [InlineData("context")]
    public Task A_menu_has_uniform_items_inside_one_even_inset(string kind) =>
        RunHeadlessAsync(window =>
        {
            var items = new[] { "Add to the Left", "Add to the Right", "Add Above", "Add Below" }
                .Select(header => new MenuItem { Header = header })
                .ToArray();
            ItemsControl presenter = kind == "flyout"
                ? new MenuFlyoutPresenter { ItemsSource = items }
                : new ContextMenu { ItemsSource = items };
            presenter.HorizontalAlignment = HorizontalAlignment.Left;
            presenter.VerticalAlignment = VerticalAlignment.Top;
            Show(window, new Border { Padding = new Thickness(20), Child = presenter });

            var itemHeight = Number(window, "ShellMenuItemHeight");
            var inset = Thickness(window, "ShellMenuPadding");
            Assert.Equal(24, itemHeight);
            Assert.Equal(inset.Left, inset.Top);
            Assert.Equal(inset.Top, inset.Bottom);
            Assert.Equal(inset.Left, inset.Right);

            Assert.All(items, item => Assert.Equal(itemHeight, item.Bounds.Height, precision: 1));

            var border = ((TemplatedControl)presenter).BorderThickness;
            var first = items[0].TranslatePoint(default, presenter)!.Value;
            var last = items[^1].TranslatePoint(new Point(items[^1].Bounds.Width, items[^1].Bounds.Height), presenter)!.Value;
            var top = first.Y - border.Top;
            var left = first.X - border.Left;
            var bottom = presenter.Bounds.Height - border.Bottom - last.Y;
            var right = presenter.Bounds.Width - border.Right - last.X;
            Assert.Equal(inset.Top, top, precision: 1);
            Assert.Equal(top, bottom, precision: 1);
            Assert.Equal(top, left, precision: 1);
            Assert.Equal(left, right, precision: 1);
        });

    /// <summary>
    /// A pop-up button's list and a menu built from buttons are menus too, and
    /// take a menu item's height and corner.
    /// </summary>
    [Fact]
    public Task Pop_up_list_items_and_button_menu_items_are_menu_items() =>
        RunHeadlessAsync(window =>
        {
            var listItem = new ComboBoxItem { Content = "Direct" };
            var buttonItem = new Button { Classes = { "FlyoutMenuItem" }, Content = new TextBlock { Text = "Open" } };
            var menuItem = new MenuItem { Header = "Copy" };
            var menu = new MenuFlyoutPresenter { ItemsSource = new[] { menuItem } };
            Show(window, new StackPanel { Children = { listItem, buttonItem, menu } });

            var itemHeight = Number(window, "ShellMenuItemHeight");
            Assert.Equal(itemHeight, listItem.Bounds.Height, precision: 1);
            Assert.Equal(itemHeight, buttonItem.Bounds.Height, precision: 1);
            Assert.Equal(itemHeight, menuItem.Bounds.Height, precision: 1);
            var corner = Assert.IsType<CornerRadius>(Resource(window, "ShellMenuItemCornerRadius"));
            Assert.Equal(corner, listItem.CornerRadius);
            Assert.Equal(corner, buttonItem.CornerRadius);
            Assert.Equal(corner, menuItem.CornerRadius);
        });

    /// <summary>
    /// A list row is never shorter than macOS 26's 28 points and a tree row
    /// than its 24, whatever its content.
    /// </summary>
    [Fact]
    public Task Rows_are_never_shorter_than_the_hosts() =>
        RunHeadlessAsync(window =>
        {
            var list = new ListBox
            {
                Classes = { "Rows" },
                ItemsSource = new[] { "One", "Two" },
            };
            var tree = new TreeView
            {
                ItemsSource = new[] { new TreeViewItem { Header = "main" }, new TreeViewItem { Header = "dev" } },
            };
            Show(window, new StackPanel { Children = { list, tree } });

            Assert.Equal(28, Number(window, "ShellRowMinHeight"));
            Assert.Equal(24, Number(window, "ShellTreeRowMinHeight"));
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.True(row.Bounds.Height >= 28, $"list row {row.Bounds.Height}"));
            var treeRows = tree.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Name == "PART_LayoutRoot")
                .ToArray();
            Assert.NotEmpty(treeRows);
            Assert.All(treeRows, row => Assert.True(row.Bounds.Height >= 24, $"tree row {row.Bounds.Height}"));
        });

    /// <summary>
    /// A list row's inset is 10 across and 6 down, and the page's section gap
    /// is 20: the spacing tokens on the 4-point grid the shell lays out on.
    /// </summary>
    [Fact]
    public Task The_shared_spacing_tokens_are_the_hosts() =>
        RunHeadlessAsync(window =>
        {
            Show(window, new Border());

            Assert.Equal(new Thickness(10, 6), Thickness(window, "ShellListRowPadding"));
            Assert.Equal(20, Number(window, "ShellSpaceSection"));
            Assert.Equal(new Thickness(16), Thickness(window, "ShellCardPadding"));
            foreach (var step in new[] { "Xs", "Sm", "Md", "Lg", "Xl", "Xxl" })
            {
                Assert.Equal(0, Number(window, $"ShellSpace{step}") % 4);
            }
        });

    /// <summary>
    /// No published text size renders under Caption, and the roles are the
    /// macOS 26 ramp: Body 13, Secondary 12, Caption 11.
    /// </summary>
    [Fact]
    public Task No_published_text_size_is_under_caption() =>
        RunHeadlessAsync(window =>
        {
            Show(window, new Border());

            Assert.Equal(13, Number(window, "ShellBodyFontSize"));
            Assert.Equal(13, Number(window, "ShellBaseFontSize"));
            Assert.Equal(12, Number(window, "ShellSecondaryFontSize"));
            Assert.Equal(11, Number(window, "ShellCaptionFontSize"));
            Assert.Equal(11, Number(window, "ShellPillFontSize"));
            for (var nominal = 10; nominal <= 28; nominal++)
            {
                Assert.True(Number(window, $"ShellFontSize{nominal}") >= 11, $"ShellFontSize{nominal}");
            }

            // The sizes rows and panel titles name read at Body.
            Assert.Equal(13, Number(window, "ShellFontSize11"));
            Assert.Equal(13, Number(window, "ShellFontSize12"));
        });

    /// <summary>
    /// A panel's title, a list row's title and a row's second line read at
    /// Body, Body and Secondary: panel headers were at 11 points.
    /// </summary>
    [Fact]
    public Task Panel_titles_and_rows_read_at_body_with_a_secondary_line() =>
        RunHeadlessAsync(window =>
        {
            var chrome = new PanelChrome { Title = "Git", Content = new Border() };
            var row = new ListItem { Title = "jobsworth-db-1", Detail = "postgres:16-alpine" };
            Show(window, new StackPanel { Children = { chrome, row } });

            var body = Number(window, "ShellBodyFontSize");
            var title = chrome.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "Git");
            Assert.Equal(body, title.FontSize);
            Assert.Equal(body, Part<TextBlock>(row, "PART_Title").FontSize);
            Assert.Equal(Number(window, "ShellSecondaryFontSize"), Part<TextBlock>(row, "PART_Detail").FontSize);
        });

    /// <summary>
    /// A notice — a callout or a row of an inventory — is a neutral card; only
    /// a callout's glyph speaks in the info tone. Blue-filled cards for every
    /// note and every row turned whole settings pages blue.
    /// </summary>
    [Fact]
    public Task A_notice_is_a_neutral_card_with_only_its_glyph_tinted() =>
        RunHeadlessAsync(window =>
        {
            var card = new SurfaceCard { Tone = SurfaceTone.Notice, Content = new TextBlock { Text = "Saved Recovery 1" } };
            var callout = new Callout { Text = "Asura keeps these to reopen your work after a crash." };
            Show(window, new StackPanel { Children = { card, callout } });

            var raised = Brush(window, "ShellSurfaceRaisedBrush");
            var neutralBorder = Brush(window, "ShellBorderBrush");
            Assert.Equal(raised, Color(card.Background));
            Assert.Equal(neutralBorder, Color(card.BorderBrush));
            var calloutCard = callout.GetVisualDescendants().OfType<SurfaceCard>().First();
            Assert.Equal(raised, Color(calloutCard.Background));
            Assert.Equal(neutralBorder, Color(calloutCard.BorderBrush));
            Assert.Equal(Brush(window, "ShellInfoBrush"), Color(Part<SymbolIcon>(callout, "PART_Glyph").Foreground));
        });

    /// <summary>
    /// A destructive action's hover fill carries its white label at text
    /// contrast: the dark-mode danger red under black, beside a white primary
    /// label, was a third button colour scheme.
    /// </summary>
    [Fact]
    public Task A_prominent_destructive_fill_carries_white() =>
        RunHeadlessAsync(window =>
        {
            Show(window, new Border());

            Assert.Equal(Colors.White, Brush(window, "ShellDangerForegroundBrush"));
            Assert.True(
                EffectiveAppearanceResourceMapper.ContrastRatio(Brush(window, "ShellDangerFillBrush"), Colors.White) >= 4.5);
            Assert.Equal(Colors.White, Brush(window, "ShellAccentForegroundBrush"));
            Assert.True(
                EffectiveAppearanceResourceMapper.ContrastRatio(Brush(window, "ShellAccentBrush"), Colors.White) >= 4.5);
        });

    private static T Part<T>(Visual owner, string name)
        where T : Control =>
        owner.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), $"{key} is not published.");
        return value;
    }

    private static Color Brush(Window window, string key) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Resource(window, key)).Color;

    private static double Number(Window window, string key) =>
        Assert.IsType<double>(Resource(window, key));

    private static Thickness Thickness(Window window, string key) =>
        Assert.IsType<Thickness>(Resource(window, key));

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static void Show(Window window, Control content)
    {
        window.Content = content;
        window.Show();
        window.UpdateLayout();
    }

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var app = Assert.IsType<App>(Avalonia.Application.Current);
                    app.ApplyApplicationResources(EffectiveAppearanceResourceMapper.Map(
                        ThemePreference.Default.Resolve(
                            new HostAppearance(HostOperatingSystem.MacOS, HostColorScheme.Dark, accent: null))));
                    var window = new Window { Width = 600, Height = 700 };
                    try
                    {
                        assertion(window);
                    }
                    finally
                    {
                        window.Close();
                    }

                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
