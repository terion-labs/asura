using Asura.App.Controls;
using Avalonia.Data.Converters;

namespace Asura.App.Views;

/// <summary>
/// The transfer card's state chips. A chip that was always green said
/// "done" about a transfer that had failed or not started; the tone now
/// follows the state.
/// </summary>
public sealed partial class WorkspaceView
{
    public static IValueConverter TransferStateTone { get; } =
        new FuncValueConverter<string?, SurfaceTone>(state => state switch
        {
            "Completed" => SurfaceTone.Success,
            "Failed" => SurfaceTone.Danger,
            _ => SurfaceTone.Default,
        });
}
