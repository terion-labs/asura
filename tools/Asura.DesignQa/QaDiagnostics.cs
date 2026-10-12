using Asura.App;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Asura.DesignQa;

/// <summary>
/// The Data &amp; Diagnostics page's own collaborators: storage that lists a
/// cache and an inactive log, three earlier runs' recovery snapshots, and a
/// diagnostics bundle that can be described but is never written. The
/// harness's main window is made without them, which hid the cache and the
/// crash-recovery sections from every capture of the page.
/// </summary>
internal static class QaDiagnostics
{
    /// <summary>When every snapshot listed was last written, fixed so captures agree.</summary>
    private static readonly DateTimeOffset LastSaved = new(2026, 10, 11, 9, 45, 0, TimeSpan.Zero);

    /// <summary>
    /// Binds the page's sections to the sample collaborators and loads them,
    /// as the product does once its own are ready.
    /// </summary>
    internal static void BindSettingsPage(MainWindow window)
    {
        Window root = (Window?)SettingsWindow.Current ?? window;
        var settings = root.GetVisualDescendants().OfType<SettingsView>().First();
        var recovery = new RecoveryDataControlViewModel(new QaRecoveryDataControl());
        var storage = new LocalArtifactControlViewModel(new QaLocalArtifactControl());
        var bundle = new DiagnosticsExportViewModel(
            new QaDiagnosticsExporter(),
            new QaDiagnosticsRequestSource(),
            new QaDiagnosticsDestinationPicker(),
            new QaDiagnosticsPresenter(),
            TimeProvider.System);
        settings.BindOperationalViewModels(recovery, storage, bundle);
        recovery.Start();
        storage.Start();
    }

    private sealed class QaLocalArtifactControl : ILocalArtifactControl
    {
        public ValueTask<LocalArtifactControlResult<LocalArtifactInventory>> InspectAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(LocalArtifactControlResult<LocalArtifactInventory>.Success(
                new LocalArtifactInventory(
                [
                    new LocalArtifactSummary(LocalArtifactKind.Cache, 12, 3_481_600),
                    new LocalArtifactSummary(LocalArtifactKind.InactiveApplicationLogs, 1, 48_128),
                ])));

        public ValueTask<LocalArtifactControlResult<LocalArtifactClearReceipt>> ClearAsync(
            LocalArtifactKind kind,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(LocalArtifactControlResult<LocalArtifactClearReceipt>.Success(
                new LocalArtifactClearReceipt(kind, 0, 0)));
    }

    private sealed class QaRecoveryDataControl : IRuntimeRecoveryDataControl
    {
        public ValueTask<ApplicationRunResult<RuntimeRecoveryInventory>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApplicationRunResult<RuntimeRecoveryInventory>.Success(
                new RuntimeRecoveryInventory(
                [
                    new RuntimeRecoveryRunSummary("qa-run-1", 2, 3_276, LastSaved),
                    new RuntimeRecoveryRunSummary("qa-run-2", 2, 3_244, LastSaved.AddDays(-4)),
                    new RuntimeRecoveryRunSummary("qa-run-3", 2, 3_174, LastSaved.AddDays(-24)),
                ],
                hasAdditionalRuns: false)));

        public ValueTask<ApplicationRunResult<long>> DiscardRunAsync(
            string runId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApplicationRunResult<long>.Success(0));

        public ValueTask<ApplicationRunResult<long>> DiscardAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApplicationRunResult<long>.Success(0));
    }

    private sealed class QaDiagnosticsRequestSource : IDiagnosticsBundleRequestSource
    {
        public IReadOnlyList<DiagnosticsRedactionLevel> SupportedRedactionLevels { get; } =
            [DiagnosticsRedactionLevel.Safe, DiagnosticsRedactionLevel.Full];

        public ValueTask<DiagnosticsBundleRequest> CreateRequestAsync(
            DiagnosticsRedactionLevel redactionLevel,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DiagnosticsBundleRequest(
                new DiagnosticsBundleMetadata(
                    ProductIdentity.DisplayName,
                    ProductIdentity.BundleIdentifier,
                    ProductIdentity.ExecutableName,
                    "0.1.0",
                    ".NET 10",
                    "Design QA",
                    "arm64",
                    LastSaved),
                []));
    }

    private sealed class QaDiagnosticsExporter : IDiagnosticsBundleExporter
    {
        public ValueTask<DiagnosticsBundleResult<DiagnosticsBundleReceipt>> ExportAsync(
            DiagnosticsBundleRequest request,
            Stream destination,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(DiagnosticsBundleResult<DiagnosticsBundleReceipt>.Success(
                new DiagnosticsBundleReceipt(0, 0, 0, new string('0', 64))));
    }

    /// <summary>The harness never writes a bundle, so the save panel is always cancelled.</summary>
    private sealed class QaDiagnosticsDestinationPicker : IDiagnosticsBundleDestinationPicker
    {
        public ValueTask<IDiagnosticsBundleDestination?> PickAsync(string suggestedFileName) =>
            ValueTask.FromResult<IDiagnosticsBundleDestination?>(null);
    }

    private sealed class QaDiagnosticsPresenter : IDiagnosticsArtifactPresenter
    {
        public DiagnosticsArtifactPresentationCapabilities Capabilities =>
            DiagnosticsArtifactPresentationCapabilities.None;

        public ValueTask<DiagnosticsArtifactPresentationResult> PresentAsync(
            DiagnosticsGeneratedArtifact artifact,
            DiagnosticsArtifactPresentationAction action,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(DiagnosticsArtifactPresentationResult.Presented);
    }
}
