using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

/// <summary>
/// About Asura: a small window of its own, opened from the application menu.
/// </summary>
public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
