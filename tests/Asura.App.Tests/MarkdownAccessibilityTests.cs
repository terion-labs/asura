using Asura.App.Views.Components;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// Markdown is how every agent reply and Markdown file preview reads, so it
/// has to reach a screen reader, follow the text-size setting, and keep each
/// paragraph's own reading direction.
/// </summary>
public sealed partial class AgentChatViewModelTests
{
    [Theory]
    [InlineData("Hello, world", FlowDirection.LeftToRight)]
    [InlineData("שלום עולם", FlowDirection.RightToLeft)]
    [InlineData("  42 — مرحبا بالعالم", FlowDirection.RightToLeft)]
    [InlineData("Release שלום", FlowDirection.LeftToRight)]
    [InlineData("123 ...", FlowDirection.LeftToRight)]
    [InlineData("", FlowDirection.LeftToRight)]
    public void Markdown_paragraph_direction_follows_its_first_strong_character(
        string text,
        FlowDirection direction) =>
        Assert.Equal(direction, MarkdownTypography.ParagraphDirection(text));

    [Fact]
    public Task Markdown_document_reads_as_a_document_with_its_text() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var preview = new MarkdownPreviewView
            {
                Text = "# Deploy notes\n\nThe API restarted cleanly.\n\n- Logs rotated",
                ContinuousSelection = true,
            };
            var window = new Window { Width = 500, Height = 400, Content = preview };
            try
            {
                window.Show();
                var document = await WaitForVisualAsync<SelectableMarkdownDocument>(
                    preview,
                    window,
                    candidate => candidate.Bounds.Height > 0,
                    "the Markdown document");
                var peer = ControlAutomationPeer.CreatePeerForElement(document);

                Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
                Assert.True(peer.IsContentElement());
                Assert.Equal(document.Text, peer.GetName());
                Assert.Contains("The API restarted cleanly.", peer.GetName(), StringComparison.Ordinal);
                Assert.Contains("Logs rotated", peer.GetName(), StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Markdown_follows_the_text_size_setting_without_a_rebuild() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var continuous = new MarkdownPreviewView
            {
                Text = "A paragraph long enough to measure.\n\n## Heading",
                ContinuousSelection = true,
            };
            var blocks = new MarkdownPreviewView
            {
                Text = "A paragraph in block mode.",
                ContinuousSelection = false,
            };
            var window = new Window
            {
                Width = 500,
                Height = 600,
                Content = new StackPanel { Children = { continuous, blocks } },
            };
            window.Resources["ShellFontSize13"] = 13d;
            window.Resources["ShellFontSize19"] = 19d;
            try
            {
                window.Show();
                var document = await WaitForVisualAsync<SelectableMarkdownDocument>(
                    continuous,
                    window,
                    candidate => candidate.Bounds.Height > 0,
                    "the continuous document");
                var prose = await WaitForVisualAsync<SelectableTextBlock>(
                    blocks,
                    window,
                    candidate => RenderedText(candidate).Contains("block mode", StringComparison.Ordinal),
                    "the block-mode paragraph");
                var height = document.Bounds.Height;
                Assert.Equal(13, prose.FontSize);
                Assert.Equal(20, prose.LineHeight);

                window.Resources["ShellFontSize13"] = 26d;
                window.Resources["ShellFontSize19"] = 38d;
                window.UpdateLayout();

                Assert.Same(document, continuous.GetVisualDescendants().OfType<SelectableMarkdownDocument>().Single());
                Assert.True(document.Bounds.Height > height * 1.5, $"{document.Bounds.Height} <= {height}");
                Assert.Equal(26, prose.FontSize);
                Assert.Equal(40, prose.LineHeight);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Right_to_left_paragraphs_flow_from_the_right_in_both_renderers() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            const string markdown = "The service is healthy.\n\nהשירות תקין.\n\n- פריט ראשון";
            var continuous = new MarkdownPreviewView { Text = markdown, ContinuousSelection = true };
            var blocks = new MarkdownPreviewView { Text = markdown, ContinuousSelection = false };
            var window = new Window
            {
                Width = 500,
                Height = 600,
                Content = new StackPanel { Children = { continuous, blocks } },
            };
            try
            {
                window.Show();
                var document = await WaitForVisualAsync<SelectableMarkdownDocument>(
                    continuous,
                    window,
                    candidate => candidate.Bounds.Height > 0,
                    "the continuous document");
                Assert.Equal(
                    new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft, FlowDirection.RightToLeft },
                    document.BlockDirections);
                // The right-to-left list item's marker moves to the right edge.
                var item = Assert.Single(document.ListLayouts);
                Assert.True(item.MarkerX > item.ContentX);

                var hebrew = await WaitForVisualAsync<SelectableTextBlock>(
                    blocks,
                    window,
                    candidate => RenderedText(candidate).Contains("השירות", StringComparison.Ordinal),
                    "the Hebrew paragraph");
                var english = blocks.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Single(candidate => RenderedText(candidate).Contains("healthy", StringComparison.Ordinal));
                Assert.Equal(FlowDirection.RightToLeft, hebrew.FlowDirection);
                Assert.Equal(FlowDirection.LeftToRight, english.FlowDirection);
            }
            finally
            {
                window.Close();
            }
        });
}
