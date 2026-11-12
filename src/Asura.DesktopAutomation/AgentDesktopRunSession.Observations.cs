using System.Text.Json;
using Asura.Application;

namespace Asura.DesktopAutomation;

// Read-only requests: application and window listings, snapshots, waits.
// Each one filters Asura's own processes out before anything is returned.
internal sealed unsafe partial class AgentDesktopRunSession
{
    private AgentDesktopExecutionResult ListApps() =>
        ListRunningApps(out var running) is { } failure
            ? Failed(failure)
            : Succeeded(ProjectApps(running, _guard));

    /// <summary>
    /// What <c>desktop.list_apps</c> returns from agent-desktop's listing:
    /// every foreign application, up to the list bound.
    /// </summary>
    internal static AgentDesktopResult.AppList ProjectApps(
        IReadOnlyList<AgentDesktopRunningApp> running,
        AgentDesktopSelfTargetGuard guard)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(guard);
        var apps = new List<AgentDesktopApplicationEntry>();
        var truncated = false;
        foreach (var app in running)
        {
            // Unverifiable processes are hidden with Asura's own: a target
            // that could not be proposed should not be advertised.
            if (guard.Classify(app.ProcessId, app.BundleId) != AgentDesktopProcessOwnership.Foreign)
            {
                continue;
            }

            if (apps.Count == AgentDesktopResult.MaximumListEntries)
            {
                truncated = true;
                break;
            }

            // The identity a close of this entry binds, established the same
            // way the close re-establishes it before dispatch.
            apps.Add(new AgentDesktopApplicationEntry(
                AgentDesktopDisplayText.FromUntrusted(app.Name, "unknown"),
                app.ProcessId,
                AgentDesktopDisplayText.FromUntrusted(app.BundleId),
                guard.IdentifyApplication(app.ProcessId, app.BundleId)));
        }

        return new AgentDesktopResult.AppList(apps, truncated);
    }

    private AgentDesktopExecutionResult ListWindows(AgentDesktopRequest.ListWindows request)
    {
        // The window list names a process but not its application; the
        // application list supplies the identifier each window is bound to.
        if (ListRunningApps(out var running) is { } unlisted)
        {
            return Failed(unlisted);
        }

        string? filter = null;
        uint? onlyProcess = null;
        if (request.Application is { } requested)
        {
            if (AgentDesktopApplicationResolver.Resolve(running, requested.Value, _guard, out var application) is { } refusal)
            {
                return Failed(new AgentDesktopFailure(refusal));
            }

            filter = application.Name;
            onlyProcess = application.ProcessId;
        }

        if (OpenWindowList(filter, out var list, out var count) is { } failure)
        {
            return Failed(failure);
        }

        var listed = new List<NativeWindow>();
        try
        {
            for (uint index = 0; index < count; index++)
            {
                if (ReadWindow(_library.ExactWindowListGet(list, index)) is { } window)
                {
                    listed.Add(window);
                }
            }
        }
        finally
        {
            _library.ExactWindowListFree(list);
        }

        return Succeeded(ProjectWindows(listed, running, onlyProcess, _guard));
    }

    /// <summary>
    /// What <c>desktop.list_windows</c> returns from agent-desktop's window
    /// listing: every window of a foreign application that has an identity a
    /// proposal can bind, up to the list bound.
    /// </summary>
    /// <param name="listed">The window listing, already copied out of native memory.</param>
    /// <param name="running">The application listing, which names each window's application.</param>
    /// <param name="onlyProcess">The resolved application's process when the request named one.</param>
    internal static AgentDesktopResult.WindowList ProjectWindows(
        IReadOnlyList<NativeWindow> listed,
        IReadOnlyList<AgentDesktopRunningApp> running,
        uint? onlyProcess,
        AgentDesktopSelfTargetGuard guard)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(guard);
        var owners = new WindowOwners(guard, running);
        var windows = new List<AgentDesktopWindowEntry>();
        var truncated = false;
        foreach (var window in listed)
        {
            // agent-desktop's filter matches by name; the process check
            // keeps a second application with the same name out.
            if ((onlyProcess is { } processId && window.ProcessId != processId)
                || owners.Identify(window.ProcessId) is not { } owner)
            {
                continue;
            }

            if (windows.Count == AgentDesktopResult.MaximumListEntries)
            {
                truncated = true;
                break;
            }

            windows.Add(new AgentDesktopWindowEntry(
                window.Id,
                AgentDesktopDisplayText.FromUntrusted(window.Title, "(untitled)"),
                AgentDesktopDisplayText.FromUntrusted(window.ApplicationName, "unknown"),
                owner,
                window.ProcessId,
                window.ProcessInstance,
                window.IsFocused));
        }

        return new AgentDesktopResult.WindowList(windows, truncated);
    }

    private AgentDesktopExecutionResult Snapshot(AgentDesktopRequest.Snapshot request)
    {
        if (ResolveApplication(request.Application.Value, out var application) is { } refused)
        {
            return Failed(refused);
        }

        // Without an identity no proposal could bind what this snapshot
        // shows, so the application is answered like one that cannot be
        // inspected.
        if (_guard.IdentifyApplication(application.ProcessId, application.BundleId) is not { } identity)
        {
            return Failed(new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound));
        }

        // Read before the walk as well as after it, so a process that was
        // replaced under the same process ID while the library walked is
        // noticed.
        if (ListProcessWindows(application.Name, application.ProcessId, out var before)
            is { Code: AgentDesktopFailureCode.EngineFaulted } faulted)
        {
            return Failed(faulted);
        }

        byte* output = null;
        int status;
        fixed (byte* name = AgentDesktopNativeText.ToUtf8Z(application.Name))
        {
            status = _library.Snapshot(
                _adapter,
                name,
                SurfaceCode(request.Surface),
                (byte)request.MaximumDepth,
                Flag(request.InteractiveOnly),
                Flag(request.Compact),
                &output);
        }

        var envelope = ReadEnvelope(status, output);
        if (envelope.Kind != AgentDesktopEnvelopeKind.Data)
        {
            return Failed(FailureFrom(envelope));
        }

        // ad_snapshot takes a name, and the envelope names the application
        // it walked but not its process. A second process with the same name
        // that started after Asura's listing could therefore be the one the
        // library walked. The snapshot is kept only when the walked window
        // is one of the resolved process's own windows and the name selects
        // exactly that process, in the same generation, once the walk is
        // over; otherwise it is refused and nothing of it is returned.
        if (!AgentDesktopSnapshotProjection.TryReadWindowId(envelope.Data, out var windowId))
        {
            return Failed(new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed));
        }

        if (ConfirmWalkedProcess(application, windowId, ProcessInstanceOf(before), out var processInstance) is { } unconfirmed)
        {
            return Failed(unconfirmed);
        }

        if (AgentDesktopSnapshotProjection.Project(envelope.Data, application, identity, processInstance) is not { } snapshot)
        {
            return Failed(new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed));
        }

        RememberSnapshot(
            snapshot.SnapshotId,
            new AgentDesktopSnapshotOrigin(application.Name, application.ProcessId, processInstance, snapshot.WindowId));
        return Succeeded(snapshot);
    }

    private AgentDesktopExecutionResult Wait(AgentDesktopRequest.Wait request)
    {
        var condition = request.Condition;
        if (ResolveApplication(condition.Application.Value, out var application) is { } refused)
        {
            return Failed(refused);
        }

        byte* output = null;
        int status;
        fixed (byte* name = AgentDesktopNativeText.ToUtf8Z(application.Name))
        fixed (byte* value = AgentDesktopNativeText.ToUtf8Z(condition.Value))
        {
            var arguments = default(AdWaitArgs);
            if (condition is AgentDesktopWaitCondition.Text)
            {
                arguments.Mode.Text = value;
            }
            else
            {
                arguments.Mode.Window = value;
            }

            arguments.Scope.Application = name;
            arguments.Scope.TimeoutMilliseconds = (ulong)request.TimeoutMilliseconds;
            status = _library.Wait(_adapter, &arguments, &output);
        }

        var envelope = ReadEnvelope(status, output);
        if (envelope.Kind == AgentDesktopEnvelopeKind.Data)
        {
            var satisfied = AgentDesktopSnapshotProjection.IsTrue(envelope.Data, "found");
            if (satisfied && RefuseUnattributedWait(application, envelope.Data) is { } unattributed)
            {
                return Failed(unattributed);
            }

            return Succeeded(new AgentDesktopResult.WaitCompleted(
                satisfied,
                ReadElapsedMilliseconds(envelope.Data, request.TimeoutMilliseconds)));
        }

        // The deadline passing is the wait's ordinary negative answer.
        var timedOut = envelope.Kind == AgentDesktopEnvelopeKind.Error
            && string.Equals(envelope.ErrorCode, AgentDesktopEnvelope.TimeoutCode, StringComparison.Ordinal);
        return timedOut
            ? Succeeded(new AgentDesktopResult.WaitCompleted(Satisfied: false, request.TimeoutMilliseconds))
            : Failed(FailureFrom(envelope));
    }

    /// <summary>
    /// The refusal for a satisfied wait that cannot be attributed to the
    /// resolved application, or null when it can.
    /// </summary>
    /// <remarks>
    /// ad_wait takes a name and polls for up to thirty seconds, looking the
    /// application up by that name on every poll. If the resolved
    /// application quit and another one answering to the same name or bundle
    /// identifier started, "found" describes that one. A window-title wait
    /// reports the window it matched, so its process is compared directly. A
    /// text wait reports no process at all; for both, the name must still
    /// select exactly the resolved process once the wait is over. What that
    /// leaves open is a replacement that matched and was itself gone, with
    /// the original back under the same process ID, before the check: ABI 7's
    /// wait scope is a timeout and an application name only, and a text
    /// answer carries no process.
    /// </remarks>
    private AgentDesktopFailure? RefuseUnattributedWait(AgentDesktopRunningApp application, JsonElement data) =>
        IsMatchedWindowOf(data, application.ProcessId)
            ? ConfirmStillResolved(application)
            : new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget);

    /// <summary>
    /// False when a satisfied wait's answer reports the window it matched
    /// and that window is not one of <paramref name="processId"/>'s, or
    /// cannot be read as anyone's. An answer that reports no window, as a
    /// text wait does, says nothing either way and is true here.
    /// </summary>
    internal static bool IsMatchedWindowOf(JsonElement data, uint processId) =>
        data.ValueKind == JsonValueKind.Object
        && (!data.TryGetProperty("window", out var window)
            || (window.ValueKind == JsonValueKind.Object
                && window.TryGetProperty("pid", out var reported)
                && reported.ValueKind == JsonValueKind.Number
                && reported.TryGetUInt32(out var matched)
                && matched == processId));

    /// <summary>
    /// Null when the exact name the library was handed selects, now, exactly
    /// the process Asura resolved before the call.
    /// </summary>
    private AgentDesktopFailure? ConfirmStillResolved(AgentDesktopRunningApp application)
    {
        if (ResolveApplication(application.Name, out var current) is { } refused)
        {
            return refused;
        }

        return current.ProcessId == application.ProcessId
            ? null
            : new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget);
    }

    /// <summary>
    /// Null when a finished snapshot can be attributed to the resolved
    /// process; <paramref name="processInstance"/> is then the generation
    /// token its targets bind.
    /// </summary>
    /// <param name="windowId">The window the envelope says was walked, or null for an application-level surface.</param>
    /// <param name="processInstanceBefore">The token read before the walk, when a window carried one.</param>
    private AgentDesktopFailure? ConfirmWalkedProcess(
        AgentDesktopRunningApp application,
        string? windowId,
        string? processInstanceBefore,
        out string? processInstance)
    {
        processInstance = null;
        if (ConfirmStillResolved(application) is { } replaced)
        {
            return replaced;
        }

        var unlisted = ListProcessWindows(application.Name, application.ProcessId, out var windows);
        return AttributeWalk(windowId, processInstanceBefore, unlisted, windows, out processInstance);
    }

    /// <summary>
    /// The decision of <see cref="ConfirmWalkedProcess"/> once the resolved
    /// process's windows have been listed again after the walk: null when the
    /// walked window is one of them and the process kept its generation.
    /// </summary>
    /// <param name="windowId">The window the envelope says was walked, or null for an application-level surface.</param>
    /// <param name="processInstanceBefore">The token read before the walk, when a window carried one.</param>
    /// <param name="unlisted">Why the listing after the walk failed, or null.</param>
    /// <param name="windowsAfter">That listing, already narrowed to the resolved process.</param>
    internal static AgentDesktopFailure? AttributeWalk(
        string? windowId,
        string? processInstanceBefore,
        AgentDesktopFailure? unlisted,
        IReadOnlyList<NativeWindow> windowsAfter,
        out string? processInstance)
    {
        processInstance = null;

        // An application without windows may have no listing at all; that
        // is acceptable only when the envelope names no window either.
        if (unlisted is not null
            && (windowId is not null || unlisted.Code == AgentDesktopFailureCode.EngineFaulted))
        {
            return unlisted;
        }

        var walked = windowId is null
            ? windowsAfter.FirstOrDefault()
            : windowsAfter.FirstOrDefault(window => string.Equals(window.Id, windowId, StringComparison.Ordinal));
        if (windowId is not null && walked is null)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget);
        }

        processInstance = walked?.ProcessInstance;
        return processInstanceBefore is null
            || string.Equals(processInstanceBefore, processInstance, StringComparison.Ordinal)
            ? null
            : new AgentDesktopFailure(AgentDesktopFailureCode.AmbiguousTarget);
    }

    private static int ReadElapsedMilliseconds(JsonElement data, int timeoutMilliseconds) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("elapsed_ms", out var elapsed)
        && elapsed.ValueKind == JsonValueKind.Number
        && elapsed.TryGetInt64(out var milliseconds)
            ? (int)Math.Clamp(milliseconds, 0, timeoutMilliseconds)
            : timeoutMilliseconds;

    private static int SurfaceCode(AgentDesktopSurface surface) => surface switch
    {
        AgentDesktopSurface.Window => AdSurface.Window,
        AgentDesktopSurface.Focused => AdSurface.Focused,
        AgentDesktopSurface.Menu => AdSurface.Menu,
        AgentDesktopSurface.Menubar => AdSurface.Menubar,
        AgentDesktopSurface.Sheet => AdSurface.Sheet,
        AgentDesktopSurface.Popover => AdSurface.Popover,
        AgentDesktopSurface.Alert => AdSurface.Alert,
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>
    /// Every running application agent-desktop reports, Asura included, so
    /// the resolver can tell "not running" from "is Asura".
    /// </summary>
    private AgentDesktopFailure? ListRunningApps(out List<AgentDesktopRunningApp> running)
    {
        running = [];
        nint list = 0;
        var status = _library.ListApps(_adapter, &list);
        try
        {
            if (status != AdResult.Ok)
            {
                return FailureFromStatus(status);
            }

            if (list == 0)
            {
                return new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed);
            }

            var count = _library.AppListCount(list);
            if (count > MaximumNativeListEntries)
            {
                return new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed);
            }

            for (uint index = 0; index < count; index++)
            {
                var entry = _library.AppListGet(list, index);
                if (entry is null || AgentDesktopNativeText.Read(entry->Name) is not { Length: > 0 } name)
                {
                    continue;
                }

                running.Add(new AgentDesktopRunningApp(
                    name,
                    entry->ProcessId,
                    AgentDesktopNativeText.Read(entry->BundleId)));
            }

            return null;
        }
        finally
        {
            if (list != 0)
            {
                _library.AppListFree(list);
            }
        }
    }

    /// <summary>
    /// Resolves a model-supplied name or bundle identifier to exactly one
    /// running application that is not Asura.
    /// </summary>
    private AgentDesktopFailure? ResolveApplication(string identifier, out AgentDesktopRunningApp application)
    {
        application = default;
        if (ListRunningApps(out var running) is { } failure)
        {
            return failure;
        }

        return AgentDesktopApplicationResolver.Resolve(running, identifier, _guard, out application) is { } code
            ? new AgentDesktopFailure(code)
            : null;
    }

    /// <summary>
    /// Opens agent-desktop's window list. On success the caller owns
    /// <paramref name="list"/> and frees it with <c>ad_exact_window_list_free</c>.
    /// </summary>
    private AgentDesktopFailure? OpenWindowList(string? applicationName, out nint list, out uint count)
    {
        nint opened = 0;
        count = 0;
        list = 0;
        int status;
        fixed (byte* filter = applicationName is null ? null : AgentDesktopNativeText.ToUtf8Z(applicationName))
        {
            status = _library.ListWindowsExact(_adapter, filter, Flag(false), &opened);
        }

        if (status != AdResult.Ok)
        {
            if (opened != 0)
            {
                _library.ExactWindowListFree(opened);
            }

            return FailureFromStatus(status);
        }

        if (opened == 0)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed);
        }

        count = _library.ExactWindowListCount(opened);
        if (count > MaximumNativeListEntries)
        {
            _library.ExactWindowListFree(opened);
            count = 0;
            return new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed);
        }

        list = opened;
        return null;
    }

    /// <summary>
    /// The listed windows of one process, asked for by its application's
    /// exact name. agent-desktop's filter matches by name, so entries of any
    /// other process are dropped here. The list is empty when the listing
    /// fails.
    /// </summary>
    private AgentDesktopFailure? ListProcessWindows(string applicationName, uint processId, out List<NativeWindow> windows)
    {
        windows = [];
        if (OpenWindowList(applicationName, out var list, out var count) is { } failure)
        {
            return failure;
        }

        try
        {
            for (uint index = 0; index < count; index++)
            {
                if (ReadWindow(_library.ExactWindowListGet(list, index)) is { } window && window.ProcessId == processId)
                {
                    windows.Add(window);
                }
            }

            return null;
        }
        finally
        {
            _library.ExactWindowListFree(list);
        }
    }

    /// <summary>
    /// The process-generation token a process's windows carry, read from the
    /// first one listed. agent-desktop reports the token per window only, so
    /// a process without a listed window has none Asura can read, and a
    /// target observed that way is bound by process ID alone.
    /// </summary>
    private static string? ProcessInstanceOf(List<NativeWindow> windows) =>
        windows.Count == 0 ? null : windows[0].ProcessInstance;

    /// <summary>
    /// Copies one native window entry. Null when its identity cannot be
    /// carried in a proposal: an unbounded window identifier or
    /// process-generation token.
    /// </summary>
    private static NativeWindow? ReadWindow(AdExactWindowInfo* info)
    {
        if (info is null)
        {
            return null;
        }

        var id = AgentDesktopNativeText.Read(info->Window.Id);
        if (!AgentDesktopNativeText.IsBoundedToken(id, MaximumTokenBytes))
        {
            return null;
        }

        string? processInstance = null;
        if (info->ProcessInstance is not null)
        {
            processInstance = AgentDesktopNativeText.Read(info->ProcessInstance);
            if (!AgentDesktopNativeText.IsBoundedToken(processInstance, MaximumTokenBytes))
            {
                return null;
            }
        }

        return new NativeWindow(
            id!,
            AgentDesktopNativeText.Read(info->Window.Title),
            AgentDesktopNativeText.Read(info->Window.ApplicationName),
            info->Window.ProcessId,
            processInstance,
            info->Window.IsFocused != 0);
    }

    /// <summary>One window of agent-desktop's exact window list, copied out of native memory.</summary>
    internal sealed record NativeWindow(
        string Id,
        string? Title,
        string? ApplicationName,
        uint ProcessId,
        string? ProcessInstance,
        bool IsFocused);

    /// <summary>
    /// Answers, once per process, which application owns a listed window.
    /// Asura's own processes, processes that cannot be inspected and
    /// processes without an identity a proposal could carry have no owner
    /// here, and their windows are not listed.
    /// </summary>
    private sealed class WindowOwners
    {
        private readonly AgentDesktopSelfTargetGuard _guard;
        private readonly Dictionary<uint, string?> _bundleIds = [];
        private readonly Dictionary<uint, AgentDesktopApplicationIdentity?> _owners = [];

        public WindowOwners(AgentDesktopSelfTargetGuard guard, IReadOnlyList<AgentDesktopRunningApp> running)
        {
            _guard = guard;
            foreach (var app in running)
            {
                // The first entry wins if the listing repeats a process.
                _ = _bundleIds.TryAdd(app.ProcessId, app.BundleId);
            }
        }

        public AgentDesktopApplicationIdentity? Identify(uint processId)
        {
            if (!_owners.TryGetValue(processId, out var owner))
            {
                var bundleId = _bundleIds.GetValueOrDefault(processId);
                owner = _guard.Classify(processId, bundleId) == AgentDesktopProcessOwnership.Foreign
                    ? _guard.IdentifyApplication(processId, bundleId)
                    : null;
                _owners.Add(processId, owner);
            }

            return owner;
        }
    }
}
