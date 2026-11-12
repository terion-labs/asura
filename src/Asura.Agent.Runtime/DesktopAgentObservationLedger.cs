using Asura.Application;

namespace Asura.Agent.Runtime;

/// <summary>
/// What one run has observed on the desktop, and therefore what it may act
/// on. The ledger is built only from host results: a model-supplied ref or
/// window id is a lookup key into it and nothing more, so the process, window
/// and element an approval names always come from an observation the host
/// made for this run (ADR 0061). Only what the model was actually shown is
/// recorded: a ref or window trimmed from the result it received could only
/// be guessed, and a guess is not an observation.
/// </summary>
/// <remarks>
/// An observation stops being usable when the same window is snapshotted
/// again, two minutes after it was made, when a mutation on its window
/// succeeds by delivering input, and when the run is cleared or stopped. A snapshot whose window
/// the host could not identify is treated as sharing a window with every
/// other snapshot of its process, so that doubt always expires more. An
/// application listing replaces the previous one, and a requested close
/// forgets everything observed of that process.
/// </remarks>
internal sealed class DesktopAgentObservationLedger
{
    internal static readonly TimeSpan ObservationLifetime = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly List<ObservedSnapshot> _snapshots = [];
    private readonly Dictionary<string, ObservedWindow> _windows = new(StringComparer.Ordinal);
    private readonly List<ObservedApplication> _applications = [];
    private bool _snapshotRequired;

    public DesktopAgentObservationLedger(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Binds an intent to the trusted target it names, or refuses it with a
    /// stable code. Every refusal here happens before authority is requested.
    /// </summary>
    public DesktopAgentResolution Resolve(DesktopAgentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (_gate)
        {
            DropExpired();
            return intent switch
            {
                DesktopAgentIntent.Direct direct => ResolveDirect(direct.Request),
                DesktopAgentIntent.OnElement element => ResolveElement(element),
                DesktopAgentIntent.OnWindow window => ResolveWindow(window.WindowId),
                DesktopAgentIntent.OnApplication application => ResolveClose(application.Application),
                _ => throw new ArgumentOutOfRangeException(nameof(intent)),
            };
        }
    }

    /// <summary>
    /// Records what a successful host result showed or changed: a snapshot
    /// and a window listing add targets, and a mutation that delivered input
    /// expires the refs of the window it touched.
    /// </summary>
    /// <param name="listedItems">
    /// How many leading snapshot nodes, listed windows or listed applications
    /// reached the model (<see cref="DesktopAgentToolJsonProjection.ListedItems"/>).
    /// Entries beyond it do not become targets. Ignored for any other result.
    /// </param>
    public void Observe(AgentDesktopRequest request, AgentDesktopResult result, int listedItems)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegative(listedItems);
        lock (_gate)
        {
            DropExpired();
            switch (request, result)
            {
                case (AgentDesktopRequest.Snapshot snapshot, AgentDesktopResult.Snapshot observed):
                    RecordSnapshot(snapshot, observed, listedItems);
                    break;
                case (AgentDesktopRequest.ListWindows, AgentDesktopResult.WindowList listed):
                    RecordWindows(listed, listedItems);
                    break;
                case (AgentDesktopRequest.ListApps, AgentDesktopResult.AppList listed):
                    RecordApplications(listed, listedItems);
                    break;
                case (AgentDesktopRequest.ElementAction, AgentDesktopResult.ActionDelivered { Delivery: AgentDesktopDelivery.NotDelivered }):
                    // The element was already in the requested state and no
                    // input was sent, so the window is as it was observed.
                    break;
                case (AgentDesktopRequest.ElementAction action, _):
                    ExpireWindowOf(action.Target.Reference);
                    break;
                case (AgentDesktopRequest.FocusWindow focus, _):
                    ExpireWindow(focus.Window.ProcessId, focus.Window.WindowId);
                    break;
                case (AgentDesktopRequest.LaunchApp, AgentDesktopResult.AppLaunched launched):
                    ExpireWindow(launched.ProcessId, windowId: null);
                    break;
                case (AgentDesktopRequest.CloseApp close, _):
                    ForgetProcess(close.Target.ProcessId);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Drops the snapshot a ref belongs to after the host reported that ref
    /// stale, so the next attempt is refused here instead of asking again.
    /// </summary>
    public void Forget(AgentDesktopElementRef reference)
    {
        lock (_gate)
        {
            _snapshots.RemoveAll(snapshot =>
                string.Equals(snapshot.SnapshotId, reference.SnapshotId, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Discards everything after an action whose effect is unknown. Until a
    /// fresh snapshot is recorded, no mutation other than launching an
    /// application resolves.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshots.Clear();
            _windows.Clear();
            _applications.Clear();
            _snapshotRequired = true;
        }
    }

    /// <summary>
    /// Forgets the run: called when it is cleared, stopped or cancelled. A
    /// pending snapshot requirement survives, because ending a run tells
    /// nothing about what an uncertain action did to the desktop; an adapter
    /// fault ends the run in the same breath as it raises the requirement.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _snapshots.Clear();
            _windows.Clear();
            _applications.Clear();
        }
    }

    /// <summary>
    /// The trusted target for a listed window, or null when the listing does
    /// not identify one. A window without a target is shown to the model
    /// without a <c>window_id</c>, since it could never be focused.
    /// </summary>
    /// <param name="observedAt">
    /// When the run received the listing. It plays no part in whether the
    /// window can be a target.
    /// </param>
    internal static AgentDesktopWindowTarget? WindowTargetFor(
        AgentDesktopWindowEntry window,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            return new AgentDesktopWindowTarget(
                new AgentDesktopApplicationId(window.Application.Text),
                window.ApplicationIdentity,
                window.Id,
                window.Title,
                window.ProcessId,
                window.ProcessInstance,
                observedAt);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private DesktopAgentResolution ResolveDirect(AgentDesktopRequest request)
    {
        // After an unknown outcome every mutation waits for a fresh snapshot,
        // except a launch. A launch acts on nothing the run observed, so no
        // observation can be stale for it, and a snapshot may be impossible
        // until it happens: the application the uncertain action touched may
        // no longer be running. It stays an exactly approved DesktopControl
        // action, and it is not an observation, so it does not lift the
        // requirement for anything else.
        return _snapshotRequired && request.IsMutation && request is not AgentDesktopRequest.LaunchApp
            ? Refused(AgentDesktopStableCodes.StaleRef)
            : new DesktopAgentResolution.Resolved(request);
    }

    /// <summary>
    /// Binds <c>desktop.close_app</c> to the one running application the run
    /// observed under that name, in an application or window listing or a
    /// snapshot. A name the run has not observed is stale: the model has to
    /// list or snapshot first, so the approval names a process the host
    /// reported rather than whatever answers to the name at dispatch (ADR
    /// 0061, threat model D7).
    /// </summary>
    /// <remarks>
    /// A sheet is how an unsaved-changes prompt appears; quitting under it
    /// would discard or block on the user's work, so a process any snapshot
    /// showed with a sheet is refused. Observations of one name that name two
    /// processes are ambiguous; nothing here picks one.
    /// </remarks>
    private DesktopAgentResolution ResolveClose(AgentDesktopApplicationId application)
    {
        if (_snapshotRequired)
        {
            return Refused(AgentDesktopStableCodes.StaleRef);
        }

        var observed = ObservationsOf(application);
        if (observed.Count == 0)
        {
            return Refused(AgentDesktopStableCodes.StaleRef);
        }

        var processId = observed[0].ProcessId;
        if (observed.Exists(target => target.ProcessId != processId))
        {
            return Refused(AgentDesktopStableCodes.AmbiguousTarget);
        }

        // The latest observation names the process; one that disagrees about
        // its identity means the process is not the one observed earlier.
        var latest = observed.MaxBy(target => target.ObservedAt)!;
        if (observed.Exists(target => target.ApplicationIdentity != latest.ApplicationIdentity))
        {
            return Refused(AgentDesktopStableCodes.StaleRef);
        }

        if (_snapshots.Exists(snapshot => snapshot.ShowsSheet && snapshot.ProcessId == processId))
        {
            return Refused(AgentDesktopStableCodes.UnsavedChanges);
        }

        // Only a window carries a generation token. The engine refuses the
        // close unless the live process still carries the one bound here.
        var processInstance = observed
            .Where(target => target.ProcessInstance is not null)
            .MaxBy(target => target.ObservedAt)
            ?.ProcessInstance;
        try
        {
            return new DesktopAgentResolution.Resolved(new AgentDesktopRequest.CloseApp(
                new AgentDesktopApplicationTarget(
                    latest.Application,
                    latest.ApplicationIdentity,
                    processId,
                    processInstance,
                    latest.ObservedAt)));
        }
        catch (ArgumentException)
        {
            return Refused(AgentDesktopStableCodes.InvalidArguments);
        }
    }

    /// <summary>Every observation of a running application the model may know it by.</summary>
    private List<AgentDesktopApplicationTarget> ObservationsOf(AgentDesktopApplicationId application)
    {
        var observed = new List<AgentDesktopApplicationTarget>();
        observed.AddRange(_applications
            .Where(entry => entry.IsFor(application))
            .Select(entry => entry.Target));
        observed.AddRange(_windows.Values
            .Where(window => window.IsFor(application))
            .Select(window => new AgentDesktopApplicationTarget(
                AgentDesktopDisplayText.FromUntrusted(window.Target.Application.Value, "unknown"),
                window.Target.ApplicationIdentity,
                window.Target.ProcessId,
                window.Target.ProcessInstance,
                window.Target.ObservedAt)));
        observed.AddRange(_snapshots
            .Where(snapshot => snapshot.IsFor(application))
            .Select(snapshot => new AgentDesktopApplicationTarget(
                snapshot.Application,
                snapshot.ApplicationIdentity,
                snapshot.ProcessId,
                snapshot.ProcessInstance,
                snapshot.ObservedAt)));
        return observed;
    }

    private DesktopAgentResolution ResolveElement(DesktopAgentIntent.OnElement intent)
    {
        var snapshot = _snapshots.Find(candidate =>
            string.Equals(candidate.SnapshotId, intent.Reference.SnapshotId, StringComparison.Ordinal));
        if (snapshot is null || !snapshot.Elements.TryGetValue(intent.Reference.Value, out var target))
        {
            return Refused(AgentDesktopStableCodes.StaleRef);
        }

        if (target.IsSecure && intent.RefusedOnSecureField)
        {
            return Refused(AgentDesktopStableCodes.SecureField);
        }

        try
        {
            return new DesktopAgentResolution.Resolved(intent.Bind(target));
        }
        catch (ArgumentException)
        {
            return Refused(AgentDesktopStableCodes.InvalidArguments);
        }
    }

    private DesktopAgentResolution ResolveWindow(string windowId) =>
        !_snapshotRequired && _windows.TryGetValue(windowId, out var window)
            ? new DesktopAgentResolution.Resolved(new AgentDesktopRequest.FocusWindow(window.Target))
            : Refused(AgentDesktopStableCodes.StaleRef);

    private void RecordSnapshot(
        AgentDesktopRequest.Snapshot request,
        AgentDesktopResult.Snapshot observed,
        int shownNodes)
    {
        // Every part of a target comes from the host's result and the run's
        // own clock; nothing the model sent is bound into it.
        var observedAt = _timeProvider.GetUtcNow();
        var elements = new Dictionary<string, AgentDesktopElementTarget>(StringComparer.Ordinal);
        try
        {
            foreach (var node in observed.Nodes.Take(shownNodes))
            {
                if (node.Reference is { } reference)
                {
                    elements[reference.Value] = new AgentDesktopElementTarget(
                        reference,
                        observed.Application,
                        observed.ApplicationIdentity,
                        observed.ProcessId,
                        observed.ProcessInstance,
                        observed.WindowId,
                        observed.WindowTitle,
                        observedAt,
                        node.Role,
                        node.Name,
                        node.IsSecure);
                }
            }
        }
        catch (ArgumentException)
        {
            // A snapshot whose process identity is not a usable target is not
            // recorded, so its refs resolve as stale rather than half-bound.
            return;
        }

        ExpireWindow(observed.ProcessId, observed.WindowId);
        _snapshots.Add(new ObservedSnapshot(
            observed.SnapshotId,
            request.Application,
            observed.Application,
            observed.ApplicationIdentity,
            observed.ProcessId,
            observed.ProcessInstance,
            observed.WindowId,
            ShowsSheet(request, observed),
            observedAt,
            elements));
        _snapshotRequired = false;
    }

    private void RecordWindows(AgentDesktopResult.WindowList listed, int shownWindows)
    {
        var observedAt = _timeProvider.GetUtcNow();
        foreach (var window in listed.Windows.Take(shownWindows))
        {
            if (WindowTargetFor(window, observedAt) is { } target)
            {
                _windows[target.WindowId] = new ObservedWindow(target);
            }
        }
    }

    /// <summary>
    /// Replaces the run's application listing. Only entries the model was
    /// shown, with an identity a proposal can bind, become close targets.
    /// </summary>
    private void RecordApplications(AgentDesktopResult.AppList listed, int shownApplications)
    {
        var observedAt = _timeProvider.GetUtcNow();
        _applications.Clear();
        foreach (var entry in listed.Apps.Take(shownApplications))
        {
            if (entry.Identity is not { } identity || entry.ProcessId == 0)
            {
                continue;
            }

            _applications.Add(new ObservedApplication(
                new AgentDesktopApplicationTarget(entry.Name, identity, entry.ProcessId, processInstance: null, observedAt),
                entry.BundleId));
        }
    }

    private void ForgetProcess(uint processId)
    {
        _snapshots.RemoveAll(snapshot => snapshot.ProcessId == processId);
        RemoveWindows(window => window.Target.ProcessId == processId);
        _applications.RemoveAll(entry => entry.Target.ProcessId == processId);
    }

    private void ExpireWindowOf(AgentDesktopElementRef reference)
    {
        var snapshot = _snapshots.Find(candidate =>
            string.Equals(candidate.SnapshotId, reference.SnapshotId, StringComparison.Ordinal));
        if (snapshot is not null)
        {
            ExpireWindow(snapshot.ProcessId, snapshot.WindowId);
        }
    }

    private void ExpireWindow(uint processId, string? windowId) =>
        _snapshots.RemoveAll(snapshot =>
            snapshot.ProcessId == processId
            && (windowId is null
                || snapshot.WindowId is null
                || string.Equals(snapshot.WindowId, windowId, StringComparison.Ordinal)));

    private void DropExpired()
    {
        var now = _timeProvider.GetUtcNow();
        _snapshots.RemoveAll(snapshot => now - snapshot.ObservedAt >= ObservationLifetime);
        RemoveWindows(window => now - window.Target.ObservedAt >= ObservationLifetime);
        _applications.RemoveAll(entry => now - entry.Target.ObservedAt >= ObservationLifetime);
    }

    private void RemoveWindows(Func<ObservedWindow, bool> expired)
    {
        foreach (var windowId in _windows.Where(entry => expired(entry.Value)).Select(entry => entry.Key).ToArray())
        {
            _windows.Remove(windowId);
        }
    }

    private static bool ShowsSheet(
        AgentDesktopRequest.Snapshot request,
        AgentDesktopResult.Snapshot observed) =>
        (request.Surface == AgentDesktopSurface.Sheet && observed.Nodes.Count > 0)
        || observed.Nodes.Any(node =>
            node.Role.Text.Equals("sheet", StringComparison.OrdinalIgnoreCase)
            || node.Role.Text.Equals("AXSheet", StringComparison.OrdinalIgnoreCase));

    private static DesktopAgentResolution.Refused Refused(string stableCode) => new(stableCode);

    // The model may name an application by the display name it was shown,
    // the name it asked for, or the bundle identifier the host reported.
    private static bool Names(AgentDesktopApplicationId application, string? name) =>
        name is not null && name.Equals(application.Value, StringComparison.OrdinalIgnoreCase);

    private static string? BundleIdentifier(AgentDesktopApplicationIdentity identity) =>
        identity.Kind == AgentDesktopApplicationIdentityKind.BundleIdentifier ? identity.Value : null;

    private sealed record ObservedSnapshot(
        string SnapshotId,
        AgentDesktopApplicationId RequestedApplication,
        AgentDesktopDisplayText Application,
        AgentDesktopApplicationIdentity ApplicationIdentity,
        uint ProcessId,
        string? ProcessInstance,
        string? WindowId,
        bool ShowsSheet,
        DateTimeOffset ObservedAt,
        Dictionary<string, AgentDesktopElementTarget> Elements)
    {
        /// <summary>
        /// True when the model's name for an application is the one this
        /// snapshot was requested with, the one the application reported, or
        /// its reported bundle identifier.
        /// </summary>
        public bool IsFor(AgentDesktopApplicationId application) =>
            Names(application, RequestedApplication.Value)
            || Names(application, Application.Text)
            || Names(application, BundleIdentifier(ApplicationIdentity));
    }

    private sealed record ObservedWindow(AgentDesktopWindowTarget Target)
    {
        public bool IsFor(AgentDesktopApplicationId application) =>
            Names(application, Target.Application.Value)
            || Names(application, BundleIdentifier(Target.ApplicationIdentity));
    }

    private sealed record ObservedApplication(AgentDesktopApplicationTarget Target, AgentDesktopDisplayText? BundleId)
    {
        public bool IsFor(AgentDesktopApplicationId application) =>
            Names(application, Target.Application.Text)
            || Names(application, BundleId?.Text)
            || Names(application, BundleIdentifier(Target.ApplicationIdentity));
    }
}

/// <summary>The ledger's answer for one intent.</summary>
internal abstract record DesktopAgentResolution
{
    private DesktopAgentResolution()
    {
    }

    public sealed record Resolved(AgentDesktopRequest Request) : DesktopAgentResolution;

    public sealed record Refused(string StableCode) : DesktopAgentResolution;
}
