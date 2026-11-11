using System.Diagnostics;
using System.Text.Json;
using Asura.Application;

namespace Asura.DesktopAutomation;

// Mutating requests. Each one re-runs the self-target guard, then makes
// exactly one mutating ad_* call. BeginMutation() is the last step before
// that call: it reads cancellation once more and raises _mutationDispatched,
// so every later failure path knows input may have landed.
internal sealed unsafe partial class AgentDesktopRunSession
{
    private AgentDesktopExecutionResult ExecuteElementAction(AgentDesktopRequest.ElementAction request)
    {
        var target = request.Target;
        if (_guard.RefuseProcess(target.ProcessId) is { } refused)
        {
            return Failed(refused);
        }

        // The request types already refuse this; repeated here because this
        // is the last point before text, a clear or a key press reaches
        // another application's secure field.
        if (target.IsSecure
            && request is AgentDesktopRequest.SetValue
                or AgentDesktopRequest.TypeText
                or AgentDesktopRequest.Clear
                or AgentDesktopRequest.PressKey)
        {
            return Failed(new AgentDesktopFailure(AgentDesktopFailureCode.SecureField));
        }

        if (RefuseUnboundReference(target) is { } unbound)
        {
            return Failed(unbound);
        }

        var action = default(AdAction);
        action.Kind = ActionKind(request);
        byte[]? text = request switch
        {
            AgentDesktopRequest.SetValue setValue => AgentDesktopNativeText.ToUtf8Z(setValue.Text.Value),
            AgentDesktopRequest.TypeText typeText => AgentDesktopNativeText.ToUtf8Z(typeText.Text.Value),
            AgentDesktopRequest.Select select => AgentDesktopNativeText.ToUtf8Z(select.Option.Value),
            _ => null,
        };
        if (request is AgentDesktopRequest.Scroll { Direction: { } direction } scroll)
        {
            action.Scroll = new AdScrollParams
            {
                Direction = DirectionCode(direction),
                Amount = (uint)scroll.Amount,
            };
        }

        var keyPress = (request as AgentDesktopRequest.PressKey)?.Key;
        byte[]? key = keyPress is null ? null : AgentDesktopNativeText.ToUtf8Z(keyPress.Key);
        var modifier = keyPress?.Modifier is { } pressed ? ModifierCode(pressed) : 0;

        byte* output = null;
        int status;
        fixed (byte* reference = AgentDesktopNativeText.ToUtf8Z(target.Reference.Value))
        fixed (byte* textPointer = text)
        fixed (byte* keyPointer = key)
        {
            action.Text = textPointer;
            action.Key.Key = keyPointer;
            if (keyPress?.Modifier is not null)
            {
                action.Key.Modifiers = &modifier;
                action.Key.ModifierCount = 1;
            }

            if (BeginMutation() is { } cancelled)
            {
                return Failed(cancelled);
            }

            status = _library.ExecuteByRefTimeout(
                _adapter,
                reference,
                &action,
                AdConstants.PolicyHeadless,
                ActionTimeoutMilliseconds,
                &output);
        }

        var envelope = ReadEnvelope(status, output);
        return envelope.Kind == AgentDesktopEnvelopeKind.Data
            ? Succeeded(ReadDeliveredAction(envelope, target.IsSecure))
            : Failed(FailureFrom(envelope));
    }

    private AgentDesktopExecutionResult FocusWindow(AgentDesktopRequest.FocusWindow request)
    {
        var target = request.Window;
        if (_guard.RefuseProcess(target.ProcessId) is { } refused)
        {
            return Failed(refused);
        }

        if (OpenWindowList(applicationName: null, out var list, out var count) is { } failure)
        {
            return Failed(failure);
        }

        try
        {
            // The proposal bound a window identifier, a process and that
            // process's generation token. Only the live entry that still
            // matches all three is handed back to agent-desktop.
            for (uint index = 0; index < count; index++)
            {
                var entry = _library.ExactWindowListGet(list, index);
                if (ReadWindow(entry) is not { } window || !IsSameWindow(window, target))
                {
                    continue;
                }

                if (BeginMutation() is { } cancelled)
                {
                    return Failed(cancelled);
                }

                var status = _library.FocusWindowExact(_adapter, entry);
                return status == AdResult.Ok
                    ? Succeeded(new AgentDesktopResult.ActionDelivered(
                        AgentDesktopDelivery.DeliveredUnverified,
                        postRole: null,
                        postValue: null,
                        postStates: null))
                    : Failed(FailureFromStatus(status));
            }

            return Failed(new AgentDesktopFailure(AgentDesktopFailureCode.WindowNotFound));
        }
        finally
        {
            _library.ExactWindowListFree(list);
        }
    }

    private AgentDesktopExecutionResult LaunchApp(AgentDesktopRequest.LaunchApp request)
    {
        if (ListRunningApps(out var running) is { } failure)
        {
            return Failed(failure);
        }

        // An already-running application is addressed by its proven name. One
        // that is not running is launched by the identifier as given; the
        // returned process is checked below because nothing could be checked
        // before it existed.
        string identifier;
        switch (AgentDesktopApplicationResolver.Resolve(running, request.Application.Value, _guard, out var application))
        {
            case null:
                identifier = application.Name;
                break;
            case AgentDesktopFailureCode.AppNotFound:
                identifier = request.Application.Value;
                break;
            case { } refusal:
                return Failed(new AgentDesktopFailure(refusal));
        }

        var window = default(AdExactWindowInfo);
        try
        {
            int status;
            fixed (byte* id = AgentDesktopNativeText.ToUtf8Z(identifier))
            {
                if (BeginMutation() is { } cancelled)
                {
                    return Failed(cancelled);
                }

                status = _library.LaunchAppExact(_adapter, id, (ulong)request.TimeoutMilliseconds, &window);
            }

            return status switch
            {
                AdResult.Ok => ReadLaunchedApp(&window),

                // agent-desktop reports APP_NOT_FOUND from launch only when
                // the operating system started no process, so this one
                // failure is safe to retry whatever its recorded disposition.
                AdResult.AppNotFound => Failed(new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound)),
                _ => Failed(FailureFromStatus(status)),
            };
        }
        finally
        {
            // Safe on a zeroed struct, so it also covers every failure path.
            _library.ReleaseExactWindowFields(&window);
        }
    }

    private AgentDesktopExecutionResult CloseApp(AgentDesktopRequest.CloseApp request)
    {
        if (ResolveObservedApplication(request.Target, checkGeneration: true, out var application) is { } refused)
        {
            return Failed(refused);
        }

        int status;
        fixed (byte* name = AgentDesktopNativeText.ToUtf8Z(application.Name))
        {
            if (BeginMutation() is { } cancelled)
            {
                return Failed(cancelled);
            }

            status = _library.CloseApp(_adapter, name, Flag(false));
        }

        if (status != AdResult.Ok)
        {
            return Failed(FailureFromStatus(status));
        }

        // ad_close_app takes a name and answers with a status: the library
        // looks the application up again by that name and never says which
        // process it asked to quit. Asura proved the name unique immediately
        // before the call and cannot hand over the process it proved: ABI 7
        // has no close by process or by exact application entry, and
        // ad_window_op_exact only moves, resizes, minimizes, maximizes or
        // restores a window. If the
        // resolved application quit in between and another one answering to
        // the same name or bundle identifier started, the library closed
        // that one. The only evidence left afterwards is the listing: when
        // any other process answers to the name now, the close is reported
        // as an unknown outcome. A replacement that has itself exited by
        // then leaves no trace and is reported as requested.
        var unlisted = ListRunningApps(out var afterwards);
        if (unlisted is { Code: AgentDesktopFailureCode.EngineFaulted })
        {
            return Failed(unlisted);
        }

        // The application may still ask the user to save; "requested" is all
        // a graceful close can claim.
        return unlisted is null
            && AgentDesktopApplicationResolver.AnswersOnlyFor(afterwards, application.Name, application.ProcessId)
            ? Succeeded(new AgentDesktopResult.AppCloseRequested(AgentDesktopDelivery.DeliveredUnverified))
            : Failed(new AgentDesktopFailure(AgentDesktopFailureCode.OutcomeUnknown, AgentDesktopDelivery.Unknown));
    }

    /// <summary>
    /// Finds the live application behind an observed close target, or the
    /// refusal. The proposal bound a process ID, the identity observed for
    /// it and, when a window carried one, its generation token; nothing the
    /// model named is resolved here.
    /// </summary>
    /// <remarks>
    /// ABI 7 closes by name only, so the observed process must also be what
    /// the library will find under the name it is handed: the live listing's
    /// display name for that process ID must resolve to exactly that process
    /// under the resolver's rules, which refuse any name Asura or an
    /// uninspectable process could answer to. A process that is gone, or
    /// whose identity or token changed, is <c>StaleRef</c>, and the model
    /// must observe the application again. agent-desktop itself still
    /// refuses protected system processes, and re-checks the process
    /// instance it resolved immediately before it terminates it.
    /// </remarks>
    /// <param name="checkGeneration">
    /// Whether to compare the generation token with the live windows'. Done
    /// before dispatch; skipped before authority, where a window listing is
    /// more native work than the guard needs.
    /// </param>
    internal AgentDesktopFailure? ResolveObservedApplication(
        AgentDesktopApplicationTarget target,
        bool checkGeneration,
        out AgentDesktopRunningApp application)
    {
        application = default;
        if (_guard.RefuseProcess(target.ProcessId) is { } refused)
        {
            return refused;
        }

        if (ListRunningApps(out var running) is { } unlisted)
        {
            return unlisted;
        }

        if (AgentDesktopApplicationResolver.ResolveObserved(
                running,
                target.ProcessId,
                target.ApplicationIdentity,
                _guard,
                out var resolved) is { } code)
        {
            return new AgentDesktopFailure(code);
        }

        if (checkGeneration && target.ProcessInstance is { } token)
        {
            // Read the same way a snapshot reads it: the first live window of
            // that process. A process with no window left has no token to
            // compare and is not the generation that was observed with one.
            if (ListProcessWindows(resolved.Name, resolved.ProcessId, out var windows) is { } failure)
            {
                return failure.Code == AgentDesktopFailureCode.EngineFaulted
                    ? failure
                    : new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef);
            }

            if (!string.Equals(ProcessInstanceOf(windows), token, StringComparison.Ordinal))
            {
                return new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef);
            }
        }

        application = resolved;
        return null;
    }

    /// <summary>
    /// The refusal for an element target whose reference Asura cannot tie to
    /// the process the caller guarded and approved, or null when it can.
    /// </summary>
    /// <remarks>
    /// The library resolves a reference from its own on-disk record and acts
    /// on whatever process that record names. The request's process is the
    /// one the guard judged and the user approved, so the two must be shown
    /// to be the same before the call: the reference's snapshot is one this
    /// session took, that snapshot was resolved to exactly the process,
    /// generation and window the request names, and the process still has
    /// that generation now. A reference minted for another process, by
    /// another adapter or by <c>desktop.wait</c>'s internal snapshot never
    /// reaches the library.
    /// </remarks>
    private AgentDesktopFailure? RefuseUnboundReference(AgentDesktopElementTarget target)
    {
        if (_snapshots.Find(target.Reference.SnapshotId) is not { } origin || !origin.Describes(target))
        {
            return new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef);
        }

        // A listing that fails leaves no window to read the token from. A
        // target that carries a token is then stale; one without a token
        // had no window when it was observed either.
        if (ListProcessWindows(origin.ApplicationName, origin.ProcessId, out var windows)
            is { Code: AgentDesktopFailureCode.EngineFaulted } faulted)
        {
            return faulted;
        }

        return string.Equals(ProcessInstanceOf(windows), target.ProcessInstance, StringComparison.Ordinal)
            ? null
            : new AgentDesktopFailure(AgentDesktopFailureCode.StaleRef);
    }

    private static int ActionKind(AgentDesktopRequest.ElementAction request) => request switch
    {
        AgentDesktopRequest.Click { Kind: AgentDesktopClickKind.Single } => AdActionKind.Click,
        AgentDesktopRequest.Click { Kind: AgentDesktopClickKind.Double } => AdActionKind.DoubleClick,
        AgentDesktopRequest.Click { Kind: AgentDesktopClickKind.Right } => AdActionKind.RightClick,
        AgentDesktopRequest.SetValue => AdActionKind.SetValue,
        AgentDesktopRequest.TypeText => AdActionKind.TypeText,
        AgentDesktopRequest.Clear => AdActionKind.Clear,
        AgentDesktopRequest.Select => AdActionKind.Select,
        AgentDesktopRequest.Toggle { Kind: AgentDesktopToggleKind.Toggle } => AdActionKind.Toggle,
        AgentDesktopRequest.Toggle { Kind: AgentDesktopToggleKind.Check } => AdActionKind.Check,
        AgentDesktopRequest.Toggle { Kind: AgentDesktopToggleKind.Uncheck } => AdActionKind.Uncheck,
        AgentDesktopRequest.Expand { Kind: AgentDesktopExpandKind.Expand } => AdActionKind.Expand,
        AgentDesktopRequest.Expand { Kind: AgentDesktopExpandKind.Collapse } => AdActionKind.Collapse,
        AgentDesktopRequest.Scroll { IsScrollIntoView: true } => AdActionKind.ScrollTo,
        AgentDesktopRequest.Scroll => AdActionKind.Scroll,
        AgentDesktopRequest.PressKey => AdActionKind.PressKey,
        _ => throw new UnreachableException(),
    };

    private static int DirectionCode(AgentDesktopScrollDirection direction) => direction switch
    {
        AgentDesktopScrollDirection.Up => AdDirection.Up,
        AgentDesktopScrollDirection.Down => AdDirection.Down,
        AgentDesktopScrollDirection.Left => AdDirection.Left,
        AgentDesktopScrollDirection.Right => AdDirection.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static int ModifierCode(AgentDesktopKeyModifier modifier) => modifier switch
    {
        AgentDesktopKeyModifier.Command => AdModifier.Meta,
        AgentDesktopKeyModifier.Control => AdModifier.Control,
        AgentDesktopKeyModifier.Alt => AdModifier.Alt,
        AgentDesktopKeyModifier.Shift => AdModifier.Shift,
        _ => throw new ArgumentOutOfRangeException(nameof(modifier)),
    };

    private static bool IsSameWindow(NativeWindow window, AgentDesktopWindowTarget target) =>
        window.ProcessId == target.ProcessId
        && string.Equals(window.Id, target.WindowId, StringComparison.Ordinal)
        && string.Equals(window.ProcessInstance, target.ProcessInstance, StringComparison.Ordinal);

    /// <summary>
    /// The element's state after the action, as agent-desktop re-read it. The
    /// value is withheld when the element was or has become a secure field.
    /// </summary>
    private static AgentDesktopResult.ActionDelivered ReadDeliveredAction(
        AgentDesktopEnvelope envelope,
        bool targetIsSecure)
    {
        if (!envelope.Data.TryGetProperty("post_state", out var postState)
            || postState.ValueKind != JsonValueKind.Object)
        {
            return new AgentDesktopResult.ActionDelivered(
                envelope.Delivery,
                postRole: null,
                postValue: null,
                postStates: null);
        }

        var role = AgentDesktopSnapshotProjection.ReadString(postState, "role");
        var states = AgentDesktopSnapshotProjection.ReadStates(postState);
        var secure = targetIsSecure || AgentDesktopSnapshotProjection.IsSecure(states, role);
        return new AgentDesktopResult.ActionDelivered(
            envelope.Delivery,
            AgentDesktopDisplayText.FromUntrusted(role),
            secure ? null : AgentDesktopDisplayText.FromUntrusted(AgentDesktopSnapshotProjection.ReadString(postState, "value")),
            states);
    }

    /// <summary>
    /// The launched application's first window. The process is classified
    /// after the fact: a launch that turned out to start Asura, or a process
    /// that cannot be inspected, is reported as a failure the model cannot
    /// build on, even though the launch itself already happened.
    /// </summary>
    private AgentDesktopExecutionResult ReadLaunchedApp(AdExactWindowInfo* window)
    {
        var processId = window->Window.ProcessId;
        var unlisted = ListRunningApps(out var running);
        if (RefuseLaunchedProcess(_guard, processId, unlisted, running) is { } refused)
        {
            return Failed(refused);
        }

        var id = AgentDesktopNativeText.Read(window->Window.Id);
        return Succeeded(new AgentDesktopResult.AppLaunched(
            processId,
            AgentDesktopNativeText.IsBoundedToken(id, MaximumTokenBytes) ? id : null,
            AgentDesktopDisplayText.FromUntrusted(AgentDesktopNativeText.Read(window->Window.Title))));
    }

    /// <summary>
    /// The failure for a launch that started <paramref name="processId"/>,
    /// or null when that process is another application. Every failure here
    /// comes after the launch, so none reports "not delivered".
    /// </summary>
    /// <param name="unlisted">Why the running-application listing taken after the launch failed, or null.</param>
    /// <param name="running">That listing, which supplies the process's bundle identifier.</param>
    /// <remarks>
    /// The bundle identifier is what recognizes an Asura copy outside the
    /// install root, so the process is classified with it. A listing that
    /// cannot be read leaves that unproven, and the launch is an unknown
    /// outcome rather than the listing's own refusal, which would claim
    /// nothing was delivered.
    /// </remarks>
    internal static AgentDesktopFailure? RefuseLaunchedProcess(
        AgentDesktopSelfTargetGuard guard,
        uint processId,
        AgentDesktopFailure? unlisted,
        IReadOnlyList<AgentDesktopRunningApp> running)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(running);
        if (unlisted is not null)
        {
            return unlisted.Code == AgentDesktopFailureCode.EngineFaulted
                ? unlisted
                : new AgentDesktopFailure(AgentDesktopFailureCode.OutcomeUnknown, AgentDesktopDelivery.DeliveredUnverified);
        }

        var bundleId = running
            .Where(app => app.ProcessId == processId)
            .Select(app => app.BundleId)
            .FirstOrDefault();
        return guard.Classify(processId, bundleId) switch
        {
            AgentDesktopProcessOwnership.Foreign => null,
            AgentDesktopProcessOwnership.Asura => new AgentDesktopFailure(
                AgentDesktopFailureCode.SelfTarget,
                AgentDesktopDelivery.DeliveredUnverified),
            _ => new AgentDesktopFailure(
                AgentDesktopFailureCode.OutcomeUnknown,
                AgentDesktopDelivery.DeliveredUnverified),
        };
    }
}
