using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

/// <summary>
/// A gallery of the shell's components, for looking at rather than shipping. It
/// holds no view model and reaches nothing: everything on it is declared inline,
/// so what it renders is the design system and only the design system.
/// </summary>
public sealed partial class DesignSystemGalleryWindow : Window
{
    public DesignSystemGalleryWindow() => InitializeComponent();

    /// <summary>
    /// The sample settings row's shortcut, written by the formatter every other
    /// surface uses. A literal "Ctrl+Alt+T" was a sixth notation on a Mac.
    /// </summary>
    public static string SampleShortcut { get; } =
        KeySequenceDisplay.Format(new KeyStroke("T", KeyModifiers.Control | KeyModifiers.Alt));

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
