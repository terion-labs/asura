using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

internal sealed partial class PanelChrome
{
    public static readonly StyledProperty<Control?> HeaderOverflowContentProperty =
        AvaloniaProperty.Register<PanelChrome, Control?>(nameof(HeaderOverflowContent));

    public static readonly DirectProperty<PanelChrome, bool> IsHeaderOverflowOpenProperty =
        AvaloniaProperty.RegisterDirect<PanelChrome, bool>(nameof(IsHeaderOverflowOpen), chrome => chrome.IsHeaderOverflowOpen);

    private bool _isHeaderOverflowOpen;
    private StackPanel? _headerActions;
    private StackPanel? _secondaryActions;
    private StackPanel? _overflowActions;
    private Button? _overflowTrigger;

    public Control? HeaderOverflowContent
    {
        get => GetValue(HeaderOverflowContentProperty);
        set
        {
            SetValue(HeaderOverflowContentProperty, value);
            UpdateHeaderOverflow();
        }
    }

    public bool IsHeaderOverflowOpen
    {
        get => _isHeaderOverflowOpen;
        private set => SetAndRaise(IsHeaderOverflowOpenProperty, ref _isHeaderOverflowOpen, value);
    }

    private void AttachHeaderOverflow(TemplateAppliedEventArgs e)
    {
        _headerActions = e.NameScope.Find<StackPanel>("PART_HeaderActions");
        _secondaryActions = e.NameScope.Find<StackPanel>("PART_SecondaryActions");
        _overflowActions = e.NameScope.Find<StackPanel>("PART_OverflowActions");
        _overflowActions?.AddHandler(Button.ClickEvent, OnOverflowActionClick);
        AddHandler(KeyDownEvent, OnOverflowKeyDown, RoutingStrategies.Tunnel);
    }

    private void DetachHeaderOverflow()
    {
        _overflowActions?.RemoveHandler(Button.ClickEvent, OnOverflowActionClick);
        RemoveHandler(KeyDownEvent, OnOverflowKeyDown);
    }

    private void UpdateHeaderOverflow()
    {
        // Keep one set of controls so icons, enabled states and accessibility
        // labels cannot drift between the header and its overflow strip.
        var destination = IsHeaderCondensed ? _overflowActions : _headerActions;
        if (_secondaryActions?.Parent is Panel current && destination is not null && current != destination)
        {
            current.Children.Remove(_secondaryActions);
            destination.Children.Insert(0, _secondaryActions);
        }

        if (!IsHeaderCondensed && HeaderOverflowContent is null)
        {
            IsHeaderOverflowOpen = false;
        }
    }

    public void ToggleHeaderOverflow(Button trigger)
    {
        _overflowTrigger = trigger;
        IsHeaderOverflowOpen = !IsHeaderOverflowOpen;
        if (IsHeaderOverflowOpen)
        {
            UpdateLayout();
            _overflowActions?.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)?.Focus(NavigationMethod.Tab);
        }
    }

    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ToggleHeaderOverflow(button);
            e.Handled = true;
        }
    }

    private void OnOverflowActionClick(object? sender, RoutedEventArgs e)
    {
        IsHeaderOverflowOpen = false;
    }

    private void OnOverflowKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsHeaderOverflowOpen && e.Key == Key.Escape)
        {
            IsHeaderOverflowOpen = false;
            _overflowTrigger?.Focus(NavigationMethod.Tab);
            e.Handled = true;
        }
    }
}
