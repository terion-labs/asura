using System.Text;
using Asura.App.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A link behaves the same in every Markdown layout: a long document that is
/// drawn block by block, and a table cell, act on a click like the short
/// continuous document does, and only the words are underlined.
/// </summary>
public sealed partial class AgentChatViewModelTests
{
    private const string GuideLink = "https://example.com/guide";

    [Fact]
    public Task A_link_in_a_long_document_is_a_link_word_by_word() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            // More than 32 blocks: the document is drawn on demand, block by block.
            var markdown = "See [the setup guide](" + GuideLink + ") before you start.\n\n"
                + string.Join("\n\n", Enumerable.Range(1, 40).Select(index => $"Paragraph {index}."));
            await AssertLinkActsAsync(new MarkdownPreviewView { Text = markdown, ContinuousSelection = true });
        });

    [Fact]
    public Task A_link_in_a_table_cell_is_a_link_word_by_word() =>
        RunAgentComposerHeadlessAsync(async () =>
        {
            var markdown = "| Topic | Where |\n| --- | --- |\n| Setup | [the setup guide](" + GuideLink + ") |";
            await AssertLinkActsAsync(new MarkdownPreviewView { Text = markdown, ContinuousSelection = true });
        });

    [Fact]
    public Task Only_a_link_a_click_opens_offers_Open_Link() =>
        RunAgentComposerHeadlessAsync(() =>
        {
            var owner = new Border();

            Assert.Equal(
                ["Open Link", "Copy Link"],
                MarkdownLinks.MenuItems(owner, GuideLink).Select(item => item.Header as string),
                StringComparer.Ordinal);
            Assert.Equal(
                ["Copy Link"],
                MarkdownLinks.MenuItems(owner, "file:///etc/hosts").Select(item => item.Header as string),
                StringComparer.Ordinal);
            return Task.CompletedTask;
        });

    private static async Task AssertLinkActsAsync(MarkdownPreviewView preview)
    {
        var window = new Window { Width = 600, Height = 700, Content = preview };
        try
        {
            window.Show();
            var text = await WaitForVisualAsync<SelectableTextBlock>(
                preview,
                window,
                candidate => candidate.Bounds.Width > 0 && LinkSpan(candidate) is not null,
                "the block that holds the link");
            var link = LinkSpan(text)!;

            // The words carry the underline, the spaces between them do not.
            var parts = link.Inlines.OfType<Run>().ToArray();
            Assert.Equal(
                ["the", " ", "setup", " ", "guide"],
                parts.Select(part => part.Text),
                StringComparer.Ordinal);
            Assert.All(
                parts,
                part => Assert.Equal(
                    !string.IsNullOrWhiteSpace(part.Text),
                    part.TextDecorations?.Any(decoration => decoration.Location == TextDecorationLocation.Underline) == true));

            // The link's own text is where a click finds it; the prose around
            // it is not a link.
            var plain = PlainText(text.Inlines!);
            var start = plain.IndexOf("setup", StringComparison.Ordinal);
            var word = text.TextLayout.HitTestTextRange(start, "setup".Length).First();
            Assert.Equal(GuideLink, MarkdownLinks.TargetAt(text, word.Center));
            var outside = plain.IndexOf("before", StringComparison.Ordinal);
            if (outside >= 0)
            {
                var prose = text.TextLayout.HitTestTextRange(outside, 3).First();
                Assert.Null(MarkdownLinks.TargetAt(text, prose.Center));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static Span? LinkSpan(SelectableTextBlock text) =>
        text.Inlines?.OfType<Span>().FirstOrDefault(span => MarkdownLinks.GetTarget(span) == GuideLink);

    private static string PlainText(InlineCollection inlines)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                builder.Append(PlainText(span.Inlines));
            }
            else if (inline is Run run)
            {
                builder.Append(run.Text);
            }
        }

        return builder.ToString();
    }
}
