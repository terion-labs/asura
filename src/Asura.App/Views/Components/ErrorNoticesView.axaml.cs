using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

public sealed partial class ErrorNoticesView : UserControl
{
    public ErrorNoticesView() => InitializeComponent();

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ErrorNoticeViewModel notice })
        {
            notice.Dismiss();
        }
    }
}
