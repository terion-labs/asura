using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Asura.App.Views.SettingsPages;

/// <summary>
/// The option row's styles as a compiled style set, added to a view's
/// <c>Styles</c> as it is built. A <c>StyleInclude</c> loads its file the
/// first time it is asked for, which restyled a Settings view that was
/// already in a window and re-templated its sidebar under it.
/// </summary>
public sealed partial class SettingOptionStyles : Styles
{
    public SettingOptionStyles() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
