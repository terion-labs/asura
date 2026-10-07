using Asura.Application;

namespace Asura.App.ViewModels;

public sealed record ConnectionDiagnosticItemViewModel(
    string Stage,
    string Status,
    string Message,
    StatusTone StatusTone)
{
    public static ConnectionDiagnosticItemViewModel From(ConnectionDiagnosticItem item) => new(
        item.Stage.ToString(),
        item.Status.ToString(),
        item.Message,
        item.Status switch
        {
            ConnectionDiagnosticStatus.Passed => StatusTone.Success,
            ConnectionDiagnosticStatus.Warning => StatusTone.Warning,
            ConnectionDiagnosticStatus.Failed => StatusTone.Danger,
            ConnectionDiagnosticStatus.NotRun => StatusTone.Neutral,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Status, null),
        });
}
