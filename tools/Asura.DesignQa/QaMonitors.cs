using System.Reflection;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.DesignQa;

/// <summary>
/// The Statistics and Processes panels with their real view models, fed by a
/// session host that answers from memory. The views bind with compiled
/// bindings typed to those view models; a look-alike object breaks every one
/// of them, and the panel then captures blank.
/// </summary>
internal static class QaMonitorPanels
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 3, 18, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// A Statistics panel with a minute of history, started: the returned task
    /// completes when the last sample has been read.
    /// </summary>
    public static (StatisticsRuntimePanelViewModel Panel, Task Started) Statistics(PanelInstanceId? panelId = null)
    {
        var client = DispatchProxy.Create<ISessionHostClient, QaMonitorSessionClient>();
        var id = panelId ?? new PanelInstanceId("qa-statistics");
        var samples = StatisticsSamples();
        var reads = 0;
        var panel = new StatisticsRuntimePanelViewModel(
            id,
            "Statistics",
            client,
            new ClientId("qa-monitor"),
            Owner(id),
            BuiltInConnections.Local,
            new ImmediateUiDispatcher(),
            // Each poll reads the next sample at once, then the panel waits
            // for good once the history is full.
            (_, cancellationToken) => ++reads < samples.Count
                ? Task.CompletedTask
                : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        ((QaMonitorSessionClient)(object)client).Statistics = samples;
        return (panel, panel.Start());
    }

    /// <summary>A Processes panel listing a handful of real-looking processes.</summary>
    public static (ProcessMonitorRuntimePanelViewModel Panel, Task Started) Processes(PanelInstanceId? panelId = null)
    {
        var client = DispatchProxy.Create<ISessionHostClient, QaMonitorSessionClient>();
        var id = panelId ?? new PanelInstanceId("qa-processes");
        var panel = new ProcessMonitorRuntimePanelViewModel(
            id,
            "Processes",
            client,
            new ClientId("qa-monitor"),
            Owner(id),
            BuiltInConnections.Local,
            new ImmediateUiDispatcher(),
            (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        ((QaMonitorSessionClient)(object)client).Processes = ProcessSample();
        return (panel, panel.Start());
    }

    private static SessionOwner Owner(PanelInstanceId panelId) => new(
        HostMode.Desktop,
        new WindowInstanceId("qa-window"),
        new WorkspaceInstanceId("qa-workspace"),
        new TabInstanceId("qa-tab-monitor"),
        panelId);

    private static List<SystemStatisticsSnapshot> StatisticsSamples()
    {
        double[] cpu = [4.1, 3.6, 5.2, 3.9, 4.4, 3.2, 3.8, 4.9, 3.5, 3.8];
        double[] received = [1.8, 2.1, 3.4, 2.7, 4.1, 2.9, 3.6, 5.2, 3.8, 4.6];
        double[] sent = [0.9, 1.1, 1.7, 1.3, 2.2, 1.5, 1.8, 2.6, 1.9, 2.3];
        const long gigabyte = 1_000_000_000;
        return [.. Enumerable.Range(0, cpu.Length).Select(index => new SystemStatisticsSnapshot(
            CapturedAt.AddSeconds(index * 2),
            new TimeSpan(9, 5, 30, index * 2),
            16,
            1_180,
            1_180,
            cpu[index],
            (101 * gigabyte) + (index * 90_000_000L),
            received[index] * 1_000_000,
            sent[index] * 1_000_000))];
    }

    private static ProcessMonitorSnapshot ProcessSample()
    {
        static ProcessMonitorEntry Entry(int pid, string name, double cpu, long memory, int hoursAgo, bool isAsura = false) =>
            new(pid, name, cpu, memory, TimeSpan.FromMinutes(hoursAgo), CapturedAt.AddHours(-hoursAgo), isAsura);
        return new(
            CapturedAt,
            [
                Entry(96971, "Asura", 0.8, 589_700_000, 1, isAsura: true),
                Entry(61335, "Brave Browser Helper (Renderer)", 0.2, 224_900_000, 44),
                Entry(37330, "AMSUIPaymentsViewService_macOS", 0.0, 15_700_000, 76),
                Entry(49345, "BackgroundTaskManagementAgent", 0.0, 29_500_000, 44),
                Entry(51129, "sysmond", 0.2, 5_600_000, 44),
                Entry(17880, "Signal", 0.0, 282_200_000, 79),
                Entry(908, "bridge-gui", 0.0, 95_100_000, 222),
            ],
            250,
            250,
            IsTruncated: false);
    }
}

/// <summary>A session host that answers the monitor panels' four calls from memory.</summary>
public class QaMonitorSessionClient : DispatchProxy
{
    private const long Revision = 1;
    private int _statisticsRead;

    internal IReadOnlyList<SystemStatisticsSnapshot> Statistics { get; set; } = [];

    internal ProcessMonitorSnapshot? Processes { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            nameof(ISessionHostClient.EnsureStatisticsSessionAsync)
                when args is [EnsureStatisticsSessionRequest request, _, _] =>
                EnsureAsync(request.SessionId, request.Owner, PanelKind.Statistics, SessionCapabilities.StatisticsRead),
            nameof(ISessionHostClient.EnsureProcessMonitorSessionAsync)
                when args is [EnsureProcessMonitorSessionRequest request, _, _] =>
                EnsureAsync(request.SessionId, request.Owner, PanelKind.ProcessMonitor, SessionCapabilities.ProcessesList),
            nameof(ISessionHostClient.ReadStatisticsAsync) => ReadStatisticsAsync(),
            nameof(ISessionHostClient.ListProcessesAsync) => ListProcessesAsync(),
            _ => throw new NotSupportedException(targetMethod?.Name),
        };

    // Each answer is the completed value task the interface returns, boxed
    // for the proxy.
    private static object EnsureAsync(
        SessionId sessionId,
        SessionOwner owner,
        PanelKind kind,
        string capability) =>
        new ValueTask<HostResult<SessionSnapshot>>(Session(sessionId, owner, kind, capability));

    private object ReadStatisticsAsync() =>
        new ValueTask<HostResult<MonitorPanelResult<SystemStatisticsSnapshot>>>(
            HostResult<MonitorPanelResult<SystemStatisticsSnapshot>>.Succeed(
                MonitorPanelResult<SystemStatisticsSnapshot>.Success(
                    Statistics[Math.Min(_statisticsRead++, Statistics.Count - 1)]),
                Revision));

    private object ListProcessesAsync() =>
        new ValueTask<HostResult<MonitorPanelResult<ProcessMonitorSnapshot>>>(
            HostResult<MonitorPanelResult<ProcessMonitorSnapshot>>.Succeed(
                MonitorPanelResult<ProcessMonitorSnapshot>.Success(
                    Processes ?? throw new InvalidOperationException("No processes were staged.")),
                Revision));

    private static HostResult<SessionSnapshot> Session(
        SessionId sessionId,
        SessionOwner owner,
        PanelKind kind,
        string capability) =>
        HostResult<SessionSnapshot>.Succeed(
            new SessionSnapshot(
                new SessionDescriptor(
                    sessionId,
                    kind,
                    SessionLifecycle.Active,
                    SessionHealth.Healthy,
                    owner,
                    new CapabilitySet([SessionCapabilities.AttachRead, capability]),
                    Revision,
                    false,
                    "Ready"),
                1,
                [],
                null),
            Revision);
}
