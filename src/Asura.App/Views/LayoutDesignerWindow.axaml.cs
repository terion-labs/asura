using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

/// <summary>The Layout Designer's window.</summary>
public sealed partial class LayoutDesignerWindow : EditorWindow
{
    public LayoutDesignerWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
