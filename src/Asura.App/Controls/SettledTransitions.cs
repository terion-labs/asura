using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Asura.App.Controls;

/// <summary>
/// Gives a control the class "settled" once it has loaded and the work
/// queued with it has run, and takes it away when the control unloads.
/// Transition styles name the class, so the state a control opens in arrives
/// at once and only later changes move. A switch that loaded already on and
/// slid there would show, for its first moments, the wrong state. The rail
/// tile does the same with its own pseudo-class (WorkspaceRailTile.OnLoaded);
/// this is the same rule for stock controls whose pseudo-classes we cannot set.
/// </summary>
public static class SettledTransitions
{
    public const string ClassName = "settled";

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled",
            typeof(SettledTransitions));

    static SettledTransitions() =>
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);

    public static bool GetIsEnabled(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(Control element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        if (e.NewValue is true)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            if (element.IsLoaded)
            {
                Settle(element);
            }
        }
        else
        {
            element.Classes.Remove(ClassName);
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control element)
        {
            Settle(element);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _ = e;
        (sender as Control)?.Classes.Remove(ClassName);
    }

    private static void Settle(Control element) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (element.IsLoaded && !element.Classes.Contains(ClassName))
                {
                    element.Classes.Add(ClassName);
                }
            },
            DispatcherPriority.Background);
}
