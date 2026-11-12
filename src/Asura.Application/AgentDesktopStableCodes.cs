namespace Asura.Application;

/// <summary>
/// The closed stable result codes of the <c>desktop.*</c> family (ADR 0061).
/// SessionHost reports them in <see cref="HostError.StableCode"/> and in the
/// audit record; the runtime returns them to the model. They are the only
/// desktop text that reaches the audit trail.
/// </summary>
public static class AgentDesktopStableCodes
{
    public const string Unavailable = "desktop_unavailable";
    public const string PermissionDenied = "desktop_permission_denied";
    public const string SelfTarget = "desktop_self_target";

    /// <summary>
    /// <c>desktop.launch_app</c> started or brought forward a process that
    /// turned out to be Asura. Unlike <see cref="SelfTarget"/>, which is a
    /// refusal before any input, the launch already changed the desktop, so
    /// the run must reconcile. It has its own code because the runtime sees
    /// only the code and must not mistake it for the refusal.
    /// </summary>
    public const string SelfTargetLaunched = "desktop_self_target_launched";
    public const string AppNotFound = "desktop_app_not_found";
    public const string WindowNotFound = "desktop_window_not_found";
    public const string StaleRef = "desktop_stale_ref";
    public const string AmbiguousTarget = "desktop_ambiguous_target";
    public const string TimeoutBeforeDelivery = "desktop_timeout_before_delivery";
    public const string PolicyDenied = "desktop_policy_denied";
    public const string ActionFailed = "desktop_action_failed";
    public const string NotSupported = "desktop_not_supported";
    public const string InvalidArguments = "desktop_invalid_arguments";
    public const string SecureField = "desktop_secure_field";
    public const string ActionOutcomeUnknown = "desktop_action_outcome_unknown";
    public const string AdapterFaulted = "desktop_adapter_faulted";
    public const string Cancelled = "desktop_cancelled";

    /// <summary>
    /// <c>desktop.close_app</c> was refused because the application shows a
    /// sheet in the run's current snapshot, which is how an unsaved-changes
    /// prompt appears. Decided by the runtime before authority.
    /// </summary>
    public const string UnsavedChanges = "desktop_unsaved_changes";

    /// <summary>
    /// The code for an engine failure. An element or snapshot the library no
    /// longer knows is a stale reference: the remedy is the same fresh
    /// <c>desktop.snapshot</c>. Any failure that does not report "not
    /// delivered" is an unknown outcome whatever its cause, so a timeout or
    /// cancellation after dispatch can never read as safe to repeat. Two
    /// causes keep their own code after delivery: an adapter fault, and a
    /// launch that turned out to start Asura (<see cref="SelfTargetLaunched"/>),
    /// which the audit trail must name as a self-target. Neither is retryable.
    /// </summary>
    public static string For(AgentDesktopFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (failure.Delivery != AgentDesktopDelivery.NotDelivered)
        {
            return failure.Code switch
            {
                AgentDesktopFailureCode.EngineFaulted => AdapterFaulted,
                AgentDesktopFailureCode.SelfTarget => SelfTargetLaunched,
                _ => ActionOutcomeUnknown,
            };
        }

        return failure.Code switch
        {
            AgentDesktopFailureCode.Unavailable => Unavailable,
            AgentDesktopFailureCode.PermissionDenied => PermissionDenied,
            AgentDesktopFailureCode.SelfTarget => SelfTarget,
            AgentDesktopFailureCode.AppNotFound => AppNotFound,
            AgentDesktopFailureCode.WindowNotFound => WindowNotFound,
            AgentDesktopFailureCode.ElementNotFound
                or AgentDesktopFailureCode.SnapshotNotFound
                or AgentDesktopFailureCode.StaleRef => StaleRef,
            AgentDesktopFailureCode.AmbiguousTarget => AmbiguousTarget,
            AgentDesktopFailureCode.Timeout => TimeoutBeforeDelivery,
            AgentDesktopFailureCode.PolicyDenied => PolicyDenied,
            AgentDesktopFailureCode.ActionFailed => ActionFailed,
            AgentDesktopFailureCode.ActionNotSupported => NotSupported,
            AgentDesktopFailureCode.InvalidArguments => InvalidArguments,
            AgentDesktopFailureCode.SecureField => SecureField,
            AgentDesktopFailureCode.OutcomeUnknown => ActionOutcomeUnknown,
            AgentDesktopFailureCode.EngineFaulted => AdapterFaulted,
            AgentDesktopFailureCode.Cancelled => Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
    }

    /// <summary>
    /// True for the outcomes after which the desktop's state is unknown to
    /// the run: the runtime skips the rest of the proposal batch and requires
    /// a fresh <c>desktop.snapshot</c> before any further desktop action.
    /// </summary>
    public static bool RequiresReconciliation(string stableCode) =>
        stableCode is ActionOutcomeUnknown or AdapterFaulted or SelfTargetLaunched;
}
