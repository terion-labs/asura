using System.Collections.ObjectModel;

namespace Asura.Application;

/// <summary>
/// What agent-desktop reported about input delivery. Only
/// <see cref="NotDelivered"/> makes a retry safe; everything else after a
/// mutation is an unknown outcome the host must not redispatch.
/// </summary>
public enum AgentDesktopDelivery
{
    Unknown,
    NotDelivered,
    Uncertain,
    DeliveredUnverified,
    DeliveredVerified,
}

public enum AgentDesktopFailureCode
{
    /// <summary>The native library is not loaded on this platform or build.</summary>
    Unavailable,

    /// <summary>The operating system has not granted Asura accessibility access.</summary>
    PermissionDenied,

    /// <summary>The target is Asura itself or a process that holds Asura's authority.</summary>
    SelfTarget,
    AppNotFound,
    WindowNotFound,
    ElementNotFound,
    StaleRef,
    AmbiguousTarget,
    SnapshotNotFound,
    Timeout,
    PolicyDenied,
    ActionFailed,
    ActionNotSupported,
    InvalidArguments,
    SecureField,

    /// <summary>Input may have reached the application; the effect is unknown.</summary>
    OutcomeUnknown,

    /// <summary>The library reported an internal fault; the run's adapter is poisoned.</summary>
    EngineFaulted,
    Cancelled,
}

public sealed record AgentDesktopFailure
{
    public AgentDesktopFailure(
        AgentDesktopFailureCode code,
        AgentDesktopDelivery delivery = AgentDesktopDelivery.NotDelivered)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (!Enum.IsDefined(delivery))
        {
            throw new ArgumentOutOfRangeException(nameof(delivery));
        }

        Code = code;
        Delivery = delivery;
    }

    public AgentDesktopFailureCode Code { get; }

    public AgentDesktopDelivery Delivery { get; }

    /// <summary>
    /// A retry is safe only when nothing reached the application and the
    /// refusal is one ADR 0061 lists as retryable, which excludes self-target,
    /// secure-field, and argument refusals.
    /// </summary>
    public bool Retryable =>
        Delivery == AgentDesktopDelivery.NotDelivered
        && Code is AgentDesktopFailureCode.StaleRef
            or AgentDesktopFailureCode.AmbiguousTarget
            or AgentDesktopFailureCode.Timeout
            or AgentDesktopFailureCode.AppNotFound
            or AgentDesktopFailureCode.WindowNotFound
            or AgentDesktopFailureCode.ElementNotFound
            or AgentDesktopFailureCode.SnapshotNotFound
            or AgentDesktopFailureCode.PermissionDenied
            or AgentDesktopFailureCode.PolicyDenied
            or AgentDesktopFailureCode.ActionNotSupported;
}

/// <param name="Identity">
/// The identity the engine established for the process, which a
/// <c>desktop.close_app</c> proposal binds. Null when none can be carried in
/// a proposal; such an entry is listed but cannot be closed.
/// </param>
public sealed record AgentDesktopApplicationEntry(
    AgentDesktopDisplayText Name,
    uint ProcessId,
    AgentDesktopDisplayText? BundleId,
    AgentDesktopApplicationIdentity? Identity = null);

public sealed record AgentDesktopWindowEntry(
    string Id,
    AgentDesktopDisplayText Title,
    AgentDesktopDisplayText Application,
    AgentDesktopApplicationIdentity ApplicationIdentity,
    uint ProcessId,
    string? ProcessInstance,
    bool IsFocused)
{
    public AgentDesktopApplicationIdentity ApplicationIdentity { get; } =
        ApplicationIdentity ?? throw new ArgumentNullException(nameof(ApplicationIdentity));
}

/// <summary>
/// One node of a flattened snapshot. Values are already redacted display
/// text; a secure field never carries a value.
/// </summary>
public sealed record AgentDesktopSnapshotNode
{
    public const int MaximumStates = 16;

    public AgentDesktopSnapshotNode(
        int depth,
        AgentDesktopDisplayText role,
        AgentDesktopDisplayText? name,
        AgentDesktopDisplayText? value,
        AgentDesktopElementRef? reference,
        IEnumerable<string> states,
        bool subtreeTruncated)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, AgentDesktopRequest.MaximumSnapshotDepth);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(states);
        var copies = states
            .Select(state => state ?? throw new ArgumentException("States cannot contain null.", nameof(states)))
            .Where(state => state.Length is > 0 and <= 32 && state.All(char.IsAsciiLetterLower))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumStates)
            .ToArray();
        IsSecure = copies.Contains("secure", StringComparer.Ordinal);
        if (IsSecure && value is not null)
        {
            throw new ArgumentException("A secure field cannot carry a value.", nameof(value));
        }

        Depth = depth;
        Role = role;
        Name = name;
        Value = value;
        Reference = reference;
        States = new ReadOnlyCollection<string>(copies);
        SubtreeTruncated = subtreeTruncated;
    }

    public int Depth { get; }

    public AgentDesktopDisplayText Role { get; }

    public AgentDesktopDisplayText? Name { get; }

    public AgentDesktopDisplayText? Value { get; }

    public AgentDesktopElementRef? Reference { get; }

    public IReadOnlyList<string> States { get; }

    public bool IsSecure { get; }

    public bool SubtreeTruncated { get; }
}

public abstract record AgentDesktopResult
{
    public const int MaximumListEntries = 128;
    public const int MaximumSnapshotNodes = 512;

    private AgentDesktopResult()
    {
    }

    public sealed record AppList : AgentDesktopResult
    {
        public AppList(IEnumerable<AgentDesktopApplicationEntry> apps, bool truncated)
        {
            ArgumentNullException.ThrowIfNull(apps);
            var copies = apps
                .Select(app => app ?? throw new ArgumentException("Apps cannot contain null.", nameof(apps)))
                .ToArray();
            if (copies.Length > MaximumListEntries)
            {
                throw new ArgumentException("Too many application entries.", nameof(apps));
            }

            Apps = new ReadOnlyCollection<AgentDesktopApplicationEntry>(copies);
            Truncated = truncated;
        }

        public IReadOnlyList<AgentDesktopApplicationEntry> Apps { get; }

        public bool Truncated { get; }
    }

    public sealed record WindowList : AgentDesktopResult
    {
        public WindowList(IEnumerable<AgentDesktopWindowEntry> windows, bool truncated)
        {
            ArgumentNullException.ThrowIfNull(windows);
            var copies = windows
                .Select(window => window ?? throw new ArgumentException("Windows cannot contain null.", nameof(windows)))
                .ToArray();
            if (copies.Length > MaximumListEntries)
            {
                throw new ArgumentException("Too many window entries.", nameof(windows));
            }

            Windows = new ReadOnlyCollection<AgentDesktopWindowEntry>(copies);
            Truncated = truncated;
        }

        public IReadOnlyList<AgentDesktopWindowEntry> Windows { get; }

        public bool Truncated { get; }
    }

    public sealed record Snapshot : AgentDesktopResult
    {
        public Snapshot(
            string snapshotId,
            AgentDesktopDisplayText application,
            AgentDesktopApplicationIdentity applicationIdentity,
            uint processId,
            string? processInstance,
            string? windowId,
            AgentDesktopDisplayText? windowTitle,
            bool complete,
            bool identityUnproven,
            IEnumerable<AgentDesktopSnapshotNode> nodes,
            bool nodesTruncated,
            int refCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(applicationIdentity);
            ArgumentNullException.ThrowIfNull(nodes);
            if (!AgentDesktopElementRef.IsValidSnapshotId(snapshotId))
            {
                throw new ArgumentException("A snapshot identifier must be a bounded token.", nameof(snapshotId));
            }

            if (processId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(refCount);
            var copies = nodes
                .Select(node => node ?? throw new ArgumentException("Nodes cannot contain null.", nameof(nodes)))
                .ToArray();
            if (copies.Length > MaximumSnapshotNodes)
            {
                throw new ArgumentException("Too many snapshot nodes.", nameof(nodes));
            }

            if (copies.Any(node => node.Reference is { } reference
                && !string.Equals(reference.SnapshotId, snapshotId, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "Every node reference must belong to this snapshot.",
                    nameof(nodes));
            }

            SnapshotId = snapshotId;
            Application = application;
            ApplicationIdentity = applicationIdentity;
            ProcessId = processId;
            ProcessInstance = processInstance;
            WindowId = windowId;
            WindowTitle = windowTitle;
            Complete = complete;
            IdentityUnproven = identityUnproven;
            Nodes = new ReadOnlyCollection<AgentDesktopSnapshotNode>(copies);
            NodesTruncated = nodesTruncated;
            RefCount = refCount;
        }

        public string SnapshotId { get; }

        public AgentDesktopDisplayText Application { get; }

        /// <summary>
        /// The identity the engine established for the process it walked. It
        /// is carried into every target minted from this snapshot.
        /// </summary>
        public AgentDesktopApplicationIdentity ApplicationIdentity { get; }

        public uint ProcessId { get; }

        public string? ProcessInstance { get; }

        public string? WindowId { get; }

        public AgentDesktopDisplayText? WindowTitle { get; }

        /// <summary>False when agent-desktop ran out of observation budget.</summary>
        public bool Complete { get; }

        /// <summary>True when refs from this snapshot cannot be proven unique.</summary>
        public bool IdentityUnproven { get; }

        public IReadOnlyList<AgentDesktopSnapshotNode> Nodes { get; }

        /// <summary>True when Asura dropped nodes beyond its own ceiling.</summary>
        public bool NodesTruncated { get; }

        public int RefCount { get; }
    }

    public sealed record WaitCompleted(bool Satisfied, int ElapsedMilliseconds) : AgentDesktopResult;

    /// <summary>A delivered element action or window operation.</summary>
    public sealed record ActionDelivered : AgentDesktopResult
    {
        public ActionDelivered(
            AgentDesktopDelivery delivery,
            AgentDesktopDisplayText? postRole,
            AgentDesktopDisplayText? postValue,
            IEnumerable<string>? postStates)
        {
            if (!Enum.IsDefined(delivery))
            {
                throw new ArgumentOutOfRangeException(nameof(delivery));
            }

            Delivery = delivery;
            PostRole = postRole;
            PostValue = postValue;
            PostStates = new ReadOnlyCollection<string>(
            [
                .. (postStates ?? [])
                    .Select(state => state ?? throw new ArgumentException(
                        "States cannot contain null.",
                        nameof(postStates)))
                    .Where(state => state.Length is > 0 and <= 32 && state.All(char.IsAsciiLetterLower))
                    .Distinct(StringComparer.Ordinal)
                    .Take(AgentDesktopSnapshotNode.MaximumStates),
            ]);
        }

        public AgentDesktopDelivery Delivery { get; }

        public bool Verified => Delivery == AgentDesktopDelivery.DeliveredVerified;

        public AgentDesktopDisplayText? PostRole { get; }

        public AgentDesktopDisplayText? PostValue { get; }

        public IReadOnlyList<string> PostStates { get; }
    }

    public sealed record AppLaunched(
        uint ProcessId,
        string? WindowId,
        AgentDesktopDisplayText? WindowTitle) : AgentDesktopResult;

    public sealed record AppCloseRequested(AgentDesktopDelivery Delivery) : AgentDesktopResult;
}

/// <summary>The engine's answer to one governed request.</summary>
public abstract record AgentDesktopExecutionResult
{
    private AgentDesktopExecutionResult()
    {
    }

    public sealed record Succeeded(AgentDesktopResult Result) : AgentDesktopExecutionResult
    {
        public AgentDesktopResult Result { get; } = Result ?? throw new ArgumentNullException(nameof(Result));
    }

    public sealed record Failed(AgentDesktopFailure Failure) : AgentDesktopExecutionResult
    {
        public AgentDesktopFailure Failure { get; } = Failure ?? throw new ArgumentNullException(nameof(Failure));
    }
}
