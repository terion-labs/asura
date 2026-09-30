using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Asura.App.Views.Components;

public sealed partial class MarkdownPreviewView
{
    public static readonly StyledProperty<IReadOnlyList<ChatHiddenReference>?> HiddenReferencesProperty =
        AvaloniaProperty.Register<MarkdownPreviewView, IReadOnlyList<ChatHiddenReference>?>(nameof(HiddenReferences));
    public static readonly StyledProperty<IAgentChatSecretRuntime?> SecretRuntimeProperty =
        AvaloniaProperty.Register<MarkdownPreviewView, IAgentChatSecretRuntime?>(nameof(SecretRuntime));

    public IReadOnlyList<ChatHiddenReference>? HiddenReferences
    {
        get => GetValue(HiddenReferencesProperty);
        set => SetValue(HiddenReferencesProperty, value);
    }
    public IAgentChatSecretRuntime? SecretRuntime
    {
        get => GetValue(SecretRuntimeProperty);
        set => SetValue(SecretRuntimeProperty, value);
    }

    private readonly List<Action> _hideChatReveals = [];

    private readonly List<Visual> _chatRevealAncestors = [];

    private void AttachChatRevealVisibility()
    {
        DetachChatRevealVisibility();
        foreach (var ancestor in this.GetVisualAncestors().Prepend(this))
        {
            _chatRevealAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnChatRevealVisibilityChanged;
        }
    }

    private void DetachChatRevealVisibility()
    {
        foreach (var ancestor in _chatRevealAncestors)
        {
            ancestor.PropertyChanged -= OnChatRevealVisibilityChanged;
        }
        _chatRevealAncestors.Clear();
    }

    private void OnChatRevealVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty && sender is Visual { IsVisible: false })
        {
            HideChatReveals(discardControls: false);
        }
    }

    private long _revealGeneration;

    private void HideChatReveals(bool discardControls = true)
    {
        _revealGeneration++;
        foreach (var hide in _hideChatReveals)
        {
            hide();
        }
        if (discardControls)
        {
            _hideChatReveals.Clear();
        }
    }

    private bool HasHiddenText(string? text) => text is not null && HiddenReferences?.Any(reference =>
        text.Contains(reference.Placeholder, StringComparison.Ordinal)) == true;

    private Inline? HiddenInline(MarkdownRun run)
    {
        if (HasHiddenText(run.LinkTarget))
        {
            var link = new Span();
            link.Inlines.Add(Inline(run with { LinkTarget = null }));
            link.Inlines.Add(new Run(" "));
            link.Inlines.Add(Inline(new MarkdownRun(run.LinkTarget!, MarkdownRunStyle.Code)));
            return link;
        }
        if (!HasHiddenText(run.Text))
        {
            return null;
        }
        var span = new Span();
        var cursor = 0;
        while (cursor < run.Text.Length)
        {
            var found = (HiddenReferences ?? []).Select(reference =>
                (Reference: reference, Index: run.Text.IndexOf(reference.Placeholder, cursor, StringComparison.Ordinal)))
                .Where(candidate => candidate.Index >= 0).OrderBy(candidate => candidate.Index).FirstOrDefault();
            if (found.Reference is null)
            {
                span.Inlines.Add(Inline(run with { Text = run.Text[cursor..] }));
                break;
            }
            if (found.Index > cursor)
            {
                span.Inlines.Add(Inline(run with { Text = run.Text[cursor..found.Index] }));
            }
            span.Inlines.Add(new InlineUIContainer
            {
                BaselineAlignment = BaselineAlignment.Baseline,
                Child = SecretSpoiler(found.Reference)
            });
            cursor = found.Index + found.Reference.Placeholder.Length;
        }
        return span;
    }

    private Button SecretSpoiler(ChatHiddenReference reference)
    {
        const string label = "<secret>";
        var display = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        var button = new SecretSpoilerButton { Content = display };
        button.Classes.Add("ChatSecret");
        AutomationProperties.SetName(button, "Reveal hidden content " + reference.Id[..6]);
        string? revealed = null;
        CancellationTokenSource? pendingReveal = null;
        void Hide()
        {
            pendingReveal?.Cancel();
            revealed = null;
            display.Text = label;
            button.ContextMenu = null;
            AutomationProperties.SetName(button, "Reveal hidden content " + reference.Id[..6]);
        }
        _hideChatReveals.Add(Hide);
        button.Click += async (_, _) =>
        {
            if (revealed is not null)
            {
                Hide();
                return;
            }
            if (SecretRuntime is not { } runtime)
            {
                display.Text = "Unavailable";
                return;
            }
            var generation = _buildGeneration;
            var revealGeneration = _revealGeneration;
            using var revealCancellation = new CancellationTokenSource();
            pendingReveal = revealCancellation;
            button.IsEnabled = false;
            try
            {
                var resolved = await runtime.RevealChatSecretAsync(reference, revealCancellation.Token);
                if (resolved is not SecretVaultResult<string>.Success success)
                {
                    if (generation == _buildGeneration && revealGeneration == _revealGeneration)
                    {
                        display.Text = "Unavailable";
                    }
                    return;
                }
                if (generation != _buildGeneration || revealGeneration != _revealGeneration || !button.IsEffectivelyVisible || TopLevel.GetTopLevel(button) is null)
                {
                    return;
                }
                revealed = LiteralSecretValidator.GetLiteralSecretDisplayValue(success.Value);
                // Original text is never parsed as Markdown, a URI, or a control.
                display.Text = revealed;
                AutomationProperties.SetName(button, "Hide revealed content");
                var copy = new MenuItem { Header = "Copy revealed content" };
                copy.Click += async (_, _) =>
                {
                    if (revealed is not null && TopLevel.GetTopLevel(button)?.Clipboard is { } clipboard)
                    {
                        await clipboard.SetTextAsync(revealed);
                    }
                };
                button.ContextMenu = new ContextMenu { ItemsSource = new[] { copy } };
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (generation == _buildGeneration && revealGeneration == _revealGeneration)
                {
                    display.Text = "Unavailable";
                }
            }
            finally
            {
                pendingReveal = null;
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private sealed class SecretSpoilerButton : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);

        protected override Size MeasureOverride(Size availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            if (Content is TextBlock text && text.TextLayout.TextLines.Count > 0)
            {
                // Embedded controls otherwise use their bottom edge as the
                // baseline, clipping text inside the paragraph's fixed line height.
                TextBlock.SetBaselineOffset(this, text.TextLayout.TextLines[0].Baseline);
            }
            return size;
        }
    }

    private Control ProtectedCode(MarkdownBlock block)
    {
        var text = Prose([new MarkdownRun(block.Text ?? string.Empty, MarkdownRunStyle.Code)]);
        return new Border { Padding = new Thickness(6, 4), Child = text };
    }
}
