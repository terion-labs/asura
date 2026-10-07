using Avalonia.Data.Converters;

namespace Asura.App.Controls;

/// <summary>
/// Lengths that are really a number of characters, stated in ems so they grow
/// with the text they hold.
///
/// A width written in pixels is right at one text size and clips at every
/// larger one: the panel title stopped at 160 whatever the text-size setting
/// said, so at 200% it held half the words it was sized for. Bound to the
/// element's own font size, the same allowance holds at every size.
/// </summary>
internal static class TextScaledLength
{
    /// <summary>
    /// How much of a panel's header its title may take: about fourteen and a
    /// half ems, which is the 160 it used to be at the default 11-point title.
    /// </summary>
    public static readonly IValueConverter PanelTitle =
        new FuncValueConverter<double, double>(fontSize => Math.Round(fontSize * 14.5));
}
