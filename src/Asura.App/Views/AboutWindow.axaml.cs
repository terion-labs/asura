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
        var version = typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        this.FindControl<TextBlock>("VersionText")!.Text = $"Version {version}";
        this.FindControl<Image>("ApplicationIcon")!.Source = MacOsApplicationIconImage.Read();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
