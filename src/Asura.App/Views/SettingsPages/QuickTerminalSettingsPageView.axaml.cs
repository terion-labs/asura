using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.SettingsPages;

public sealed partial class QuickTerminalSettingsPageView : UserControl
{
    public QuickTerminalSettingsPageView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? RecordHotkeyRequested;

    private void OnRecordHotkeyClick(object? sender, RoutedEventArgs e) =>
        RecordHotkeyRequested?.Invoke(sender, e);
}
