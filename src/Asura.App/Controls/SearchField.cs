using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Asura.App.Controls;

/// <summary>
/// A search field as the host draws one: a magnifying glass at the leading
/// edge, the placeholder saying what is searched, and a clear button that
/// appears once there is something to clear. Escape clears it too, and only
/// when there is text — an empty field lets Escape through to whatever owns
/// it, so it still closes the sheet or popover the field sits in.
///
/// Of twenty-one search and filter fields, four drew a magnifier and none
/// could be cleared without selecting the text and deleting it. A text box
/// that is a search field is this control, so each one behaves the same.
///
/// It is a <see cref="TextBox"/> in every other respect and is styled as one:
/// the shell's text-field look, focus ring and validation all apply, and the
/// <c>SearchField</c> class carries what is particular to searching.
/// </summary>
internal sealed class SearchField : TextBox
{
    private readonly Button _clear;

    public SearchField()
    {
        Classes.Add("SearchField");
        PlaceholderText = "Search";
        InnerLeftContent = new SymbolIcon
        {
            Symbol = Symbol.Search,
            IsHitTestVisible = false,
            Classes = { "SearchFieldGlyph" },
        };

        // Out of the tab order, as the host's cancel button is: the keyboard
        // clears with Escape, and a stop on the way out of every search field
        // would only be in the way.
        _clear = new Button
        {
            Classes = { "IconButton", "SearchFieldClear" },
            IsTabStop = false,
            IsVisible = false,
            Content = new SymbolIcon
            {
                Symbol = Symbol.DismissCircle,
                IconVariant = IconVariant.Filled,
            },
        };
        AutomationProperties.SetName(_clear, "Clear search");
        ToolTip.SetTip(_clear, "Clear");
        _clear.Click += OnClearClick;
        InnerRightContent = _clear;
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _clear.IsVisible = !string.IsNullOrEmpty(Text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && e.KeyModifiers == KeyModifiers.None
            && !string.IsNullOrEmpty(Text))
        {
            Clear();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new SearchFieldAutomationPeer(this);

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        Clear();
        Focus(NavigationMethod.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// A field nobody labelled is still named by what it searches: the
    /// placeholder is the only words it carries.
    /// </summary>
    private sealed class SearchFieldAutomationPeer(SearchField owner) : TextBoxAutomationPeer(owner)
    {
        protected override string? GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrWhiteSpace(name) ? Owner.PlaceholderText : name;
        }
    }
}
