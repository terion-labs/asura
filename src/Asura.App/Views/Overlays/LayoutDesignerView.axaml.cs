using System.Globalization;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Overlays;

public sealed partial class LayoutDesignerView : UserControl
{
    public LayoutDesignerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether a saved layout is the one open in the designer: its id, then
    /// the designer's. A new layout has an id no saved one shares.
    /// </summary>
    public static IMultiValueConverter SameLayout { get; } = new SameLayoutConverter();

    /// <summary>
    /// A saved layout's accessible name: its name, then its id and the
    /// designer's. The open one adds ", open", since its check is only drawn.
    /// </summary>
    public static IMultiValueConverter LayoutRowName { get; } = new LayoutRowNameConverter();

    public event EventHandler<RoutedEventArgs>? AddSlotRequested;

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<RoutedEventArgs>? EditLayoutRequested;

    public event EventHandler<RoutedEventArgs>? RemoveSlotRequested;

    public event EventHandler<RoutedEventArgs>? ResetRequested;

    public event EventHandler<RoutedEventArgs>? SaveRequested;

    public event EventHandler<RoutedEventArgs>? SlotSelectedRequested;

    public event EventHandler<RoutedEventArgs>? SplitSlotDownRequested;

    public event EventHandler<RoutedEventArgs>? SplitSlotRightRequested;

    internal void FocusNameEditor() =>
        NewLayoutName.Focus(NavigationMethod.Tab);

    private void OnLayoutAddSlotClick(object? sender, RoutedEventArgs e) =>
        AddSlotRequested?.Invoke(sender, e);

    private void OnLayoutDesignerCancelClick(object? sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(sender, e);

    private void OnEditLayoutClick(object? sender, RoutedEventArgs e) =>
        EditLayoutRequested?.Invoke(sender, e);

    private void OnLayoutSlotClick(object? sender, RoutedEventArgs e) =>
        SlotSelectedRequested?.Invoke(sender, e);

    private void OnResetLayoutClick(object? sender, RoutedEventArgs e) =>
        ResetRequested?.Invoke(sender, e);

    private void OnSaveLayoutDesignerClick(object? sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(sender, e);

    private void OnLayoutSlotPointerPressed(object? sender, PointerPressedEventArgs e) =>
        SlotSelectedRequested?.Invoke(sender, e);

    private void OnLayoutRemoveSlotClick(object? sender, RoutedEventArgs e) =>
        RemoveSlotRequested?.Invoke(sender, e);

    private void OnLayoutSplitSlotDownClick(object? sender, RoutedEventArgs e) =>
        SplitSlotDownRequested?.Invoke(sender, e);

    private void OnLayoutSplitSlotRightClick(object? sender, RoutedEventArgs e) =>
        SplitSlotRightRequested?.Invoke(sender, e);

    private sealed class SameLayoutConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            _ = targetType;
            _ = parameter;
            _ = culture;
            return values is [LayoutId saved, LayoutId open] && saved == open;
        }
    }

    private sealed class LayoutRowNameConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            _ = targetType;
            _ = parameter;
            _ = culture;
            return values switch
            {
                [string name, LayoutId saved, LayoutId open] when saved == open => $"{name}, open",
                [string name, ..] => name,
                _ => null,
            };
        }
    }
}
