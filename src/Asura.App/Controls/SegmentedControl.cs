using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Controls;

/// <summary>
/// A choice between two to five stable views of the same content.
///
/// The shell switched views four ways: push buttons wearing a "selected"
/// class, toggle-button segments, a segmented TabControl, and in one inspector
/// a strip of up to nine segments that appeared and vanished with the
/// selection. A segmented control is the host's answer for a few fixed views
/// of one thing, so this is the one control for it: each segment is a radio
/// button, so exactly one is chosen, a screen reader hears "radio button,
/// selected" and its position rather than a bare button, and the arrow keys
/// move the choice as they do in an <c>NSSegmentedControl</c>.
///
/// Segments that do not apply are disabled, not removed — a control whose
/// segments come and go makes every segment move under the pointer. Five is
/// the most that reads at a glance; more than that is a pop-up button or a
/// tab view.
/// </summary>
internal sealed class SegmentedControl : SelectingItemsControl
{
    /// <summary>The most segments that still read as one control.</summary>
    public const int MaximumSegments = 5;

    /// <summary>
    /// Binds a two-segment choice to a flag: the first segment when it is
    /// false, the second when it is true ("Read-Only" or "Read &amp; Write").
    /// Bound to the selected index rather than to each segment's checked
    /// state, so the choice the control makes on its own before the data
    /// arrives never writes over the stored one.
    /// </summary>
    public static readonly IValueConverter SecondWhenTrue = new FlagSegmentConverter();

    private readonly string _groupName = $"SegmentedControl{Guid.NewGuid():N}";
    private bool _synchronizing;

    static SegmentedControl()
    {
        SelectionModeProperty.OverrideDefaultValue<SegmentedControl>(
            SelectionMode.Single | SelectionMode.AlwaysSelected);
        // Tab lands on the chosen segment, once, and the arrows take it from
        // there; the strip itself is not a stop of its own.
        FocusableProperty.OverrideDefaultValue<SegmentedControl>(false);
        KeyboardNavigation.TabNavigationProperty.OverrideDefaultValue<SegmentedControl>(
            KeyboardNavigationMode.Once);
        ToggleButton.IsCheckedChangedEvent.AddClassHandler<SegmentedControl>(
            (control, args) => control.OnSegmentCheckedChanged(args));
        SelectedIndexProperty.Changed.AddClassHandler<SegmentedControl>(
            (control, _) => control.SynchronizeSegments());
    }

    protected override Type StyleKeyOverride => typeof(SegmentedControl);

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<RadioButton>(item, out recycleKey);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new RadioButton();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is RadioButton segment)
        {
            segment.Classes.Add("Segment");
            segment.GroupName = _groupName;
        }
    }

    protected override void ContainerForItemPreparedOverride(Control container, object? item, int index)
    {
        base.ContainerForItemPreparedOverride(container, item, index);
        if (container is RadioButton segment)
        {
            Synchronize(segment, index);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new SegmentedControlAutomationPeer(this);

    /// <summary>
    /// Left and right step the choice to the neighbouring enabled segment and
    /// take focus with it, which is how the host's segmented control answers
    /// the arrow keys; up and down do the same for a vertical strip.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0,
        };
        if (step == 0 || e.KeyModifiers != KeyModifiers.None)
        {
            base.OnKeyDown(e);
            return;
        }

        var count = ItemCount;
        for (var index = SelectedIndex + step; index >= 0 && index < count; index += step)
        {
            if (ContainerFromIndex(index) is RadioButton { IsEffectivelyEnabled: true } segment)
            {
                SelectedIndex = index;
                segment.Focus(NavigationMethod.Directional);
                break;
            }
        }

        e.Handled = true;
    }

    private void OnSegmentCheckedChanged(RoutedEventArgs args)
    {
        if (_synchronizing || args.Source is not RadioButton { IsChecked: true } segment)
        {
            return;
        }

        var index = IndexFromContainer(segment);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }

    /// <summary>
    /// A selection set before the containers exist is applied as each one is
    /// prepared; one set afterwards has to reach the realised segments.
    /// </summary>
    private void SynchronizeSegments()
    {
        for (var index = 0; index < ItemCount; index++)
        {
            if (ContainerFromIndex(index) is RadioButton segment)
            {
                Synchronize(segment, index);
            }
        }
    }

    private void Synchronize(RadioButton segment, int index)
    {
        _synchronizing = true;
        try
        {
            segment.SetCurrentValue(ToggleButton.IsCheckedProperty, index == SelectedIndex);
        }
        finally
        {
            _synchronizing = false;
        }
    }

    /// <summary>
    /// A selection that is neither segment — none while the segments are
    /// being replaced — leaves the flag as it is.
    /// </summary>
    private sealed class FlagSegmentConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? 1 : 0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value switch
            {
                0 => false,
                1 => true,
                _ => BindingOperations.DoNothing,
            };
    }

    /// <summary>
    /// Exposes the control as a group whose single selection is its checked
    /// segment, rather than as the list a selecting control reports by default.
    /// </summary>
    private sealed class SegmentedControlAutomationPeer(SegmentedControl owner)
        : SelectingItemsControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Group;
    }
}
