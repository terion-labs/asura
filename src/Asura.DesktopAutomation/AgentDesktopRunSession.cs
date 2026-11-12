using System.Diagnostics;
using Asura.Application;

namespace Asura.DesktopAutomation;

/// <summary>
/// One agent run's agent-desktop adapter and every native call made through
/// it. agent-desktop keeps its last-error state per thread and its snapshot
/// refs per adapter session, so an instance is used from exactly one thread:
/// the run's <see cref="AgentDesktopRunWorker"/>. Nothing here is thread-safe.
/// </summary>
/// <remarks>
/// A request makes at most one mutating <c>ad_*</c> call and never repeats
/// it. The read-only calls that precede it (permission probe, application and
/// window listings) decide whether the mutation may be dispatched at all.
/// </remarks>
internal sealed unsafe partial class AgentDesktopRunSession(
    AgentDesktopLibrary library,
    AgentDesktopSelfTargetGuard guard,
    string sessionName)
{
    // ADR 0061: element actions always get agent-desktop's standard auto-wait
    // budget; a request cannot widen it.
    private const long ActionTimeoutMilliseconds = 5_000;

    // More entries than any desktop has. A list beyond it is treated as a
    // malformed answer rather than walked.
    private const uint MaximumNativeListEntries = 4_096;

    private const int MaximumTokenBytes = 128;

    private readonly AgentDesktopLibrary _library = library;
    private readonly AgentDesktopSelfTargetGuard _guard = guard;
    private readonly string _sessionName = sessionName;
    private readonly AgentDesktopSnapshotOrigins _snapshots = new();
    private nint _adapter;
    private bool _faulted;

    // Set immediately before the one mutating call of the current request and
    // cleared when that request ends. While it is set, an unreadable answer
    // means "input may have been delivered", not "nothing happened".
    private bool _mutationDispatched;

    // The current request's cancellation, which is also how SessionHost
    // revokes an authorization that is already on its way here.
    private CancellationToken _cancellation;

    public AgentDesktopExecutionResult Execute(AgentDesktopRequest request, CancellationToken cancellationToken) =>
        Contain(
            cancellationToken,
            () => EnsureReady() is { } notReady ? Failed(notReady) : Dispatch(request));

    /// <summary>
    /// <see cref="Execute"/> without first asking the operating system
    /// whether access is granted. Only the tests call it directly: on a host
    /// without the grant it is the one way to reach the native entrypoints
    /// with real arguments, and the library itself then refuses whatever
    /// needs the grant. The self-target guard is part of every request and
    /// applies here unchanged.
    /// </summary>
    internal AgentDesktopExecutionResult ExecuteUnprobed(AgentDesktopRequest request) =>
        Contain(
            CancellationToken.None,
            () => EnsureUsableAdapter() is { } unusable ? Failed(unusable) : Dispatch(request));

    /// <summary>
    /// Records what a snapshot of this run was resolved to. Element actions
    /// are dispatched only for references of a recorded snapshot. The tests
    /// call it where no snapshot can be taken, to reach the checks and the
    /// native call that follow the lookup.
    /// </summary>
    internal void RememberSnapshot(string snapshotId, AgentDesktopSnapshotOrigin origin) =>
        _snapshots.Remember(snapshotId, origin);

    /// <summary>
    /// Runs one request to a result, whatever happens inside it. Nothing
    /// thrown leaves this method: before the mutating call an exception is a
    /// closed failure with nothing delivered, and after it an unknown
    /// outcome, because losing the fact that input was sent would let the
    /// caller send it again.
    /// </summary>
    internal AgentDesktopExecutionResult Contain(
        CancellationToken cancellationToken,
        Func<AgentDesktopExecutionResult> request)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Failed(new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled));
        }

        _cancellation = cancellationToken;
        try
        {
            return request();
        }
        catch (Exception)
        {
            // Text from another application's accessibility tree feeds
            // validating constructors, and a request can carry a value no
            // constructor let through. Neither may surface as an exception.
            return Failed(_mutationDispatched
                ? new AgentDesktopFailure(AgentDesktopFailureCode.OutcomeUnknown, AgentDesktopDelivery.Unknown)
                : new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed));
        }
        finally
        {
            // The probes and listings that run between requests must not
            // inherit this request's "input was sent".
            _mutationDispatched = false;
            _cancellation = CancellationToken.None;
        }
    }

    /// <summary>
    /// The last step before a mutating native call. The guard, the listings
    /// and the reference checks before it take time in which the run can be
    /// stopped or its authorization revoked, so cancellation is read again
    /// here, where refusing still delivers nothing. Null means the call is
    /// now considered dispatched.
    /// </summary>
    internal AgentDesktopFailure? BeginMutation()
    {
        if (_cancellation.IsCancellationRequested)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.Cancelled);
        }

        _mutationDispatched = true;
        return null;
    }

    /// <summary>Whether the operating system lets this process drive other applications.</summary>
    public AgentDesktopAccessState ProbeAccess()
    {
        if (EnsureUsableAdapter() is not null)
        {
            return AgentDesktopAccessState.Unavailable;
        }

        switch (_library.CheckPermissions(_adapter))
        {
            case AdResult.Ok:
                return AgentDesktopAccessState.Granted;
            case AdResult.PermissionDenied:
                return AgentDesktopAccessState.NotGranted;
            case AdResult.Internal:
                Fault();
                return AgentDesktopAccessState.Unavailable;
            default:
                return AgentDesktopAccessState.Unavailable;
        }
    }

    /// <summary>
    /// The pre-authority refusal for a request that names an application by
    /// <paramref name="identifier"/>: Asura or an uninspectable process could
    /// answer to it (<see cref="AgentDesktopApplicationResolver.RefuseBeforeAuthority"/>).
    /// A listing that cannot be read refuses as well, because then no process
    /// the name could reach is shown to be foreign. <see cref="Execute"/>
    /// repeats the resolution immediately before the native call.
    /// </summary>
    public AgentDesktopFailure? RefuseNamedTarget(string identifier)
    {
        if (EnsureUsableAdapter() is { } unusable)
        {
            return unusable;
        }

        if (ListRunningApps(out var running) is { } unlisted)
        {
            return unlisted;
        }

        return AgentDesktopApplicationResolver.RefuseBeforeAuthority(running, identifier, _guard) is { } code
            ? new AgentDesktopFailure(code)
            : null;
    }

    /// <summary>
    /// The pre-authority refusal for <c>desktop.close_app</c>: the same
    /// checks <see cref="Execute"/> repeats before the native call, short of
    /// the generation token, which needs a window listing.
    /// </summary>
    public AgentDesktopFailure? RefuseApplicationTarget(AgentDesktopApplicationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (EnsureUsableAdapter() is { } unusable)
        {
            return unusable;
        }

        return ResolveObservedApplication(target, checkGeneration: false, out _);
    }

    /// <summary>
    /// Calls the library's panic-injection export, which exists only in the
    /// evidence build, and reports how the fault surfaced.
    /// </summary>
    public AgentDesktopFailure InjectPanic()
    {
        if (_faulted)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted);
        }

        if (_library.TestPanicBoundary is null)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable);
        }

        if (EnsureUsableAdapter() is { } unusable)
        {
            return unusable;
        }

        var status = _library.TestPanicBoundary();
        return status == AdResult.Ok
            ? new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed)
            : FailureFromStatus(status);
    }

    /// <summary>Releases the adapter. The session answers <c>EngineFaulted</c> or recreates it afterwards.</summary>
    public void Destroy()
    {
        if (_adapter != 0)
        {
            _library.AdapterDestroy(_adapter);
            _adapter = 0;
        }
    }

    private static AgentDesktopExecutionResult Failed(AgentDesktopFailure failure) =>
        new AgentDesktopExecutionResult.Failed(failure);

    private static AgentDesktopExecutionResult Succeeded(AgentDesktopResult result) =>
        new AgentDesktopExecutionResult.Succeeded(result);

    private static byte Flag(bool value) => value ? (byte)1 : (byte)0;

    private AgentDesktopExecutionResult Dispatch(AgentDesktopRequest request) => request switch
    {
        AgentDesktopRequest.ListApps => ListApps(),
        AgentDesktopRequest.ListWindows listWindows => ListWindows(listWindows),
        AgentDesktopRequest.Snapshot snapshot => Snapshot(snapshot),
        AgentDesktopRequest.Wait wait => Wait(wait),
        AgentDesktopRequest.ElementAction elementAction => ExecuteElementAction(elementAction),
        AgentDesktopRequest.FocusWindow focusWindow => FocusWindow(focusWindow),
        AgentDesktopRequest.LaunchApp launchApp => LaunchApp(launchApp),
        AgentDesktopRequest.CloseApp closeApp => CloseApp(closeApp),
        _ => throw new UnreachableException(),
    };

    /// <summary>
    /// Null once the run has an adapter, creating it on first use. A creation
    /// that fails with <c>AD_RESULT_ERR_INTERNAL</c>, a trapped panic or a
    /// state-directory fault, faults the run like any other internal error;
    /// any other failure leaves the run unavailable and is tried again on the
    /// next request.
    /// </summary>
    private AgentDesktopFailure? EnsureUsableAdapter()
    {
        if (_faulted)
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.EngineFaulted);
        }

        if (_adapter != 0)
        {
            return null;
        }

        fixed (byte* name = AgentDesktopNativeText.ToUtf8Z(_sessionName))
        {
            _adapter = _library.AdapterCreateWithSession(name);
        }

        if (_adapter != 0)
        {
            return null;
        }

        // The library keeps its last error per thread, and this is the
        // thread that made the call.
        return _library.LastErrorCode() == AdResult.Internal
            ? Fault()
            : new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable);
    }

    private AgentDesktopFailure? EnsureReady()
    {
        if (EnsureUsableAdapter() is { } unusable)
        {
            return unusable;
        }

        // Probed on every request: the user can revoke the grant mid-run.
        return _library.CheckPermissions(_adapter) switch
        {
            AdResult.Ok => null,
            AdResult.PermissionDenied => new AgentDesktopFailure(AgentDesktopFailureCode.PermissionDenied),
            AdResult.Internal => Fault(),
            _ => new AgentDesktopFailure(AgentDesktopFailureCode.Unavailable),
        };
    }

    /// <summary>
    /// The library reported <c>AD_RESULT_ERR_INTERNAL</c>, which includes a
    /// trapped panic. Its adapter state is no longer trusted: the adapter is
    /// destroyed and every later request of this run is refused.
    /// </summary>
    private AgentDesktopFailure Fault()
    {
        Destroy();
        _faulted = true;
        return new AgentDesktopFailure(
            AgentDesktopFailureCode.EngineFaulted,
            _mutationDispatched ? AgentDesktopDelivery.Unknown : AgentDesktopDelivery.NotDelivered);
    }

    private AgentDesktopDelivery ReadLastDelivery()
    {
        var semantics = default(AdDeliverySemantics);
        _ = _library.LastErrorDeliverySemantics(&semantics);
        return semantics.Delivery switch
        {
            AdConstants.DeliveryNotDelivered => AgentDesktopDelivery.NotDelivered,
            AdConstants.DeliveryUncertain => AgentDesktopDelivery.Uncertain,
            AdConstants.DeliveryDeliveredUnverified => AgentDesktopDelivery.DeliveredUnverified,
            AdConstants.DeliveryDeliveredVerified => AgentDesktopDelivery.DeliveredVerified,
            _ => AgentDesktopDelivery.Unknown,
        };
    }

    private AgentDesktopFailure FailureFromError(string? code, AgentDesktopDelivery delivery)
    {
        if (string.Equals(code, AgentDesktopEnvelope.InternalCode, StringComparison.Ordinal))
        {
            return Fault();
        }

        // Only an explicit "not delivered" makes a failed mutation retryable.
        return _mutationDispatched && delivery != AgentDesktopDelivery.NotDelivered
            ? new AgentDesktopFailure(AgentDesktopFailureCode.OutcomeUnknown, delivery)
            : new AgentDesktopFailure(AgentDesktopEnvelope.MapErrorCode(code));
    }

    /// <summary>For entrypoints that answer with a status code and no envelope.</summary>
    private AgentDesktopFailure FailureFromStatus(int status) =>
        FailureFromError(AgentDesktopEnvelope.StatusCode(status), ReadLastDelivery());

    private AgentDesktopFailure FailureFrom(AgentDesktopEnvelope envelope) =>
        envelope.Kind == AgentDesktopEnvelopeKind.Error
            ? FailureFromError(envelope.ErrorCode, envelope.Delivery)
            : _mutationDispatched
                ? new AgentDesktopFailure(AgentDesktopFailureCode.OutcomeUnknown, AgentDesktopDelivery.Unknown)
                : new AgentDesktopFailure(AgentDesktopFailureCode.ActionFailed);

    /// <summary>Takes ownership of <paramref name="output"/> and frees it.</summary>
    private AgentDesktopEnvelope ReadEnvelope(int status, byte* output)
    {
        try
        {
            if (status == AdResult.Internal)
            {
                return AgentDesktopEnvelope.Error(AgentDesktopEnvelope.InternalCode, ReadLastDelivery());
            }

            if (output is null)
            {
                return status == AdResult.Ok
                    ? AgentDesktopEnvelope.Invalid
                    : AgentDesktopEnvelope.Error(AgentDesktopEnvelope.StatusCode(status), ReadLastDelivery());
            }

            var bytes = AgentDesktopNativeText.ReadBytes(output, AgentDesktopEnvelope.MaximumBytes);
            if (bytes is null)
            {
                return AgentDesktopEnvelope.Invalid;
            }

            return AgentDesktopEnvelope.Parse(bytes, status);
        }
        finally
        {
            if (output is not null)
            {
                _library.FreeString(output);
            }
        }
    }
}
