using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Application.Previews;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using StubFilePanelClient = Asura.App.Tests.FileRuntimePanelViewModelTests.StubFilePanelClient;

namespace Asura.App.Tests;

/// <summary>
/// An archive too long for one page: the owner found a "Next Archive Page"
/// push button wedged between the details card and the listing, at its own
/// inset. The page controls are the listing's footer, beside its summary, and
/// the listing follows the card at the card's own gap.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ArchivePreviewLayoutHeadlessTests
{
    [Fact]
    public Task A_long_archive_pages_from_the_listing_footer_not_above_the_entries() =>
        RunHeadlessAsync(async window =>
        {
            using var panel = await OpenArchiveAsync(entryCount: 501);
            var view = new FileRuntimePanelView { DataContext = panel };
            window.Content = view;
            window.Show();
            await WaitUntilAsync(() =>
            {
                window.UpdateLayout();
                return view.GetVisualDescendants().OfType<TreeViewItem>().Any(item => item.Bounds.Height > 0);
            });

            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<Button>(),
                button => button.Content is string label && label.Contains("Archive Page", StringComparison.Ordinal));

            var listing = view.FindControl<PreviewTreeView>("ArchivePreview")!;
            var entries = listing.FindControl<TreeView>("Entries")!;
            var footer = listing.FindControl<Grid>("Footer")!;
            var next = listing.FindControl<Button>("NextPageButton")!;
            var previous = listing.FindControl<Button>("PreviousPageButton")!;
            Assert.True(next.IsEffectivelyVisible);
            Assert.True(next.IsEffectivelyEnabled);
            Assert.False(previous.IsEffectivelyEnabled);
            Assert.Equal("Next archive page", AutomationProperties.GetName(next));

            var entriesBottom = entries.TranslatePoint(new Point(0, entries.Bounds.Height), view)!.Value.Y;
            var nextTop = next.TranslatePoint(default, view)!.Value.Y;
            Assert.True(nextTop >= entriesBottom, "The page controls sit under the entries they turn.");
            Assert.True(next.TranslatePoint(default, footer).HasValue);

            // Nothing between the details card and the entries but the card's
            // own bottom margin.
            var card = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Kind" && text.IsEffectivelyVisible)
                .GetVisualAncestors().OfType<Asura.App.Controls.SurfaceCard>().First();
            var cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), view)!.Value.Y;
            var entriesTop = entries.TranslatePoint(default, view)!.Value.Y;
            Assert.True(
                entriesTop - cardBottom >= 0 && entriesTop - cardBottom <= card.Margin.Bottom + 1,
                $"The entries start {entriesTop - cardBottom} below the card, whose margin is {card.Margin}.");

            next.Command!.Execute(null);
            await WaitUntilAsync(() => panel.HasPreviousArchivePage && !panel.IsPreviewLoading);
            window.UpdateLayout();
            Assert.StartsWith("Page 2 · ", panel.PreviewTree!.Summary, StringComparison.Ordinal);
        });

    [Fact]
    public Task An_archive_that_fits_on_one_page_shows_no_page_controls() =>
        RunHeadlessAsync(async window =>
        {
            using var panel = await OpenArchiveAsync(entryCount: 3);
            Assert.Null(panel.PreviewTree!.Paging);
            var listing = new PreviewTreeView { DataContext = panel.PreviewTree };
            window.Content = listing;
            window.Show();
            window.UpdateLayout();

            Assert.False(listing.FindControl<StackPanel>("Pager")!.IsVisible);
        });

    private static async Task<FileRuntimePanelViewModel> OpenArchiveAsync(int entryCount)
    {
        var client = new StubFilePanelClient();
        client.Entries.Add(new FilePanelEntry(
            client.Root.Child(new FilePanelPathSegment("bundle.zip")),
            "bundle.zip",
            FilePanelEntryKind.File,
            512,
            DateTimeOffset.UnixEpoch,
            IsHidden: false));
        client.Preview = new FilePanelPreview(
            client.Root.Child(new FilePanelPathSegment("bundle.zip")),
            FilePanelPreviewKind.Hex,
            "application/octet-stream",
            [0x50, 0x4B, 0x03, 0x04],
            isTruncated: true);
        client.MaterializedPath = Path.Combine(Path.GetTempPath(), "bundle.zip");
        var reader = new ListingReader([.. Enumerable.Range(0, entryCount)
            .Select(index => new ArchiveEntryDescriptor($"entry-{index:D3}.txt", false, 1, 1))]);
        var panel = new FileRuntimePanelViewModel(PanelInstanceId.New(), "Files", client, archiveReader: reader);
        await panel.Initialization;
        panel.SelectedEntry = Assert.Single(panel.Entries);
        await panel.PreviewSelectedAsync();
        Assert.NotNull(panel.PreviewTree);
        return panel;
    }

    private sealed class ListingReader(IReadOnlyList<ArchiveEntryDescriptor> entries) : IArchiveTableOfContents
    {
        public bool Claims(string fileName) => true;

        public ValueTask<IReadOnlyList<ArchiveEntryDescriptor>?> ReadAsync(
            FilePreviewContent content,
            string fileName,
            int maximumEntries,
            CancellationToken cancellationToken,
            int offset = 0) =>
            ValueTask.FromResult<IReadOnlyList<ArchiveEntryDescriptor>?>([.. entries.Skip(offset).Take(maximumEntries)]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task RunHeadlessAsync(Func<Window, Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        var completed = await session.Dispatch(async () =>
        {
            var window = new Window { Width = 1_000, Height = 700 };
            try
            {
                await assertion(window);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, timeout.Token);
        Assert.True(completed);
    }
}
