namespace Asura.Application.ApplicationUpdates;

public sealed record ApplicationUpdateSnapshot(
    DistributionIdentity Distribution,
    ApplicationUpdateStage Stage,
    string? AvailableVersion = null,
    int? DownloadProgress = null,
    ApplicationUpdateError Error = ApplicationUpdateError.None)
{
    public bool CanCheck => Stage is ApplicationUpdateStage.Idle
        or ApplicationUpdateStage.UpToDate
        or ApplicationUpdateStage.Available
        or ApplicationUpdateStage.Failed;

    public bool CanDownload =>
        Stage == ApplicationUpdateStage.Available;

    public bool CanRestartToApply =>
        Stage == ApplicationUpdateStage.ReadyToRestart;
}
