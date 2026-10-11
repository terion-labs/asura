using System.Globalization;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Application.Previews;

namespace Asura.App.Tests;

/// <summary>
/// The Files panel and its previews as the HIG review asked for them: dates
/// that shorten rather than clip, a kind in Finder's words, one control for
/// preview or source, code that wraps under itself, and links that act.
/// </summary>
public sealed class FilePreviewHigTests
{
    [Fact]
    public void A_narrow_modified_column_shows_the_time_for_today_and_the_date_otherwise()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var now = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero);
            var today = now.AddHours(-2);
            var earlier = now.AddDays(-40);

            Assert.Equal(
                today.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
                FileEntryViewModel.FormatCompactDate(today, now));
            Assert.Equal(
                earlier.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
                FileEntryViewModel.FormatCompactDate(earlier, now));
            Assert.Equal("Unknown", FileEntryViewModel.FormatCompactDate(null, now));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(200, 150, "Thursday, October 8, 2026", "10/8/2026", "Thursday, October 8, 2026")]
    [InlineData(100, 150, "Thursday, October 8, 2026", "10/8/2026", "10/8/2026")]
    [InlineData(100, 150, "Thursday, October 8, 2026", null, "Thursday, October 8, 2026")]
    public void A_fitting_text_says_the_long_form_only_when_it_fits(
        double available,
        double fullWidth,
        string full,
        string? compact,
        string expected) =>
        Assert.Equal(expected, FittingTextBlock.Choose(full, compact, fullWidth, available));

    [Fact]
    public void A_file_kind_is_named_rather_than_called_a_file()
    {
        var location = new FilePanelLocation(
            "builtin.files.home",
            "local",
            new FilePanelAddress.Hierarchical(FilePanelPath.Root));
        var file = new FileEntryMetadataViewModel(
            new FilePanelEntry(location, "notes.md", FilePanelEntryKind.File, 1200, null, IsHidden: false),
            isStatBacked: true);
        var folder = new FileEntryMetadataViewModel(
            new FilePanelEntry(location, "docs", FilePanelEntryKind.Directory, null, null, IsHidden: false),
            isStatBacked: true);

        Assert.NotEqual("File", file.Kind, StringComparer.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(file.Kind));
        Assert.Equal("Folder", folder.Kind);
        // No version tag, no "Version: Unavailable".
        Assert.Null(file.Version);
        Assert.False(file.HasVersion);
    }

    [Fact]
    public void A_change_stamp_is_not_shown_as_a_version_unless_the_provider_keeps_versions()
    {
        var entry = new FilePanelEntry(
            new FilePanelLocation(
                "s3.assets",
                "bucket",
                new FilePanelAddress.Hierarchical(FilePanelPath.Root),
                "3HL4kqtJlcpXroDTDmJ.rmSpXd3dIbrHY"),
            "logo.png",
            FilePanelEntryKind.File,
            1200,
            null,
            IsHidden: false);

        var stamped = new FileEntryMetadataViewModel(entry, isStatBacked: true);
        var versioned = new FileEntryMetadataViewModel(
            entry,
            isStatBacked: true,
            providerKeepsVersions: true);

        Assert.False(stamped.HasVersion);
        Assert.True(versioned.HasVersion);
        Assert.Equal("3HL4kqtJlcpXroDTDmJ.rmSpXd3dIbrHY", versioned.Version);
    }

    [Fact]
    public void A_missing_item_says_what_to_do_next()
    {
        var issue = FileOperationIssue.FromProvider(new FilePanelError(
            FilePanelErrorCode.NotFound,
            "file.not_found",
            "Couldn't open the folder.",
            Retryable: false));

        Assert.Equal("Item not found", issue.Title);
        Assert.Contains("Refresh", issue.SuggestedAction, StringComparison.Ordinal);
        Assert.True(issue.CanRetry);
    }

    /// <summary>
    /// "As table" and "Prettify" are on for the rendered side, "Show raw" for
    /// the source side; the segments read the same either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_preview_switch_maps_segments_to_the_previewer_polarity(bool onShowsSource)
    {
        var changes = new List<bool>();
        var toggle = new PreviewToggleViewModel(
            new FilePreviewToggle("mode", "Source", IsOn: !onShowsSource, onShowsSource),
            (_, value) => changes.Add(value));

        Assert.Equal(PreviewToggleViewModel.PreviewSegment, toggle.SelectedSegment);
        toggle.SelectedSegment = PreviewToggleViewModel.SourceSegment;

        Assert.True(toggle.ShowsSource);
        Assert.Equal([onShowsSource], changes);
        Assert.Equal("Show preview or source", toggle.AccessibleName);
    }

    [Theory]
    [InlineData("csharp", ".cs")]
    [InlineData("python", ".py")]
    [InlineData("shell", ".sh")]
    [InlineData("json", ".json")]
    [InlineData("yml", ".yaml")]
    [InlineData("Dockerfile", ".dockerfile")]
    public void A_fence_named_by_language_is_highlighted_by_its_grammar(string language, string extension) =>
        Assert.Equal(extension, SourcePreviewGrammar.ResolveExtension(MarkdownPreviewView.FenceFileName(language)));

    [Fact]
    public void A_fence_without_a_language_stays_plain() =>
        Assert.Null(MarkdownPreviewView.FenceFileName("  "));

    [Fact]
    public void A_link_is_underlined_word_by_word() =>
        Assert.Equal(
            ["Read", " ", "the", " ", "guide", " "],
            SelectableMarkdownDocument.SplitAtWhitespace("Read the guide "),
            StringComparer.Ordinal);

    [Theory]
    [InlineData("https://example.com/guide", true)]
    [InlineData("mailto:team@example.com", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("docs/guide.md", false)]
    public void Only_web_and_mail_links_open_on_a_click(string target, bool opens) =>
        Assert.Equal(opens, SelectableMarkdownDocument.OpensOnClick(target));

    [Fact]
    public void A_one_page_archive_has_no_page_number()
    {
        // The summary alone, rather than "Page 1:" in front of it.
        Assert.DoesNotContain("Page 1", new PreviewTreeViewModel([], "3 items").Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_narrow_hex_view_lays_each_row_out_as_two_halves()
    {
        var row = new HexPreviewRow(
            "00000010",
            "00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F",
            "0123456789abcdef");

        var halves = PreviewHexViewModel.Lay(row, PreviewHexViewModel.NarrowRow).ToArray();

        Assert.Equal(2, halves.Length);
        Assert.Equal(new HexPreviewRow("00000010", "00 01 02 03 04 05 06 07", "01234567"), halves[0]);
        Assert.Equal(new HexPreviewRow("00000018", "08 09 0A 0B 0C 0D 0E 0F", "89abcdef"), halves[1]);
        Assert.Single(PreviewHexViewModel.Lay(new HexPreviewRow("00000020", "AA BB", "ab"), PreviewHexViewModel.NarrowRow));
    }

    [Fact]
    public async Task Switching_the_row_width_keeps_every_byte_on_screen()
    {
        var hex = new PreviewHexViewModel(PreviewText.HexRows(new byte[40], providerTruncated: false));
        await hex.FillAsync(CancellationToken.None);
        Assert.Equal(3, hex.Rows.Count);

        hex.BytesPerRow = PreviewHexViewModel.NarrowRow;
        Assert.Equal(5, hex.Rows.Count);

        hex.BytesPerRow = PreviewHexViewModel.WideRow;
        Assert.Equal(3, hex.Rows.Count);
    }
}
