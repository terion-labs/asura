using System.Globalization;
using System.Text;

namespace Asura.Application;

/// <summary>
/// An application as the model may name it: a display name or, on macOS, a
/// bundle identifier. Paths are rejected so the model can never name an
/// executable; the engine resolves the identifier against the live app list.
/// </summary>
public readonly record struct AgentDesktopApplicationId
{
    public const int MaximumBytes = 128;

    public AgentDesktopApplicationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!AgentDesktopText.IsBoundedSingleLine(value, MaximumBytes)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A desktop application identifier must be bounded single-line text without path separators.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// A snapshot-qualified element reference, <c>@&lt;snapshot_id&gt;:eN</c>. The
/// snapshot identifier pins the exact observation the ref came from; a bare
/// <c>@eN</c> is refused because it could resolve against any snapshot.
/// </summary>
public readonly record struct AgentDesktopElementRef
{
    // agent-desktop's own bounds (validate_snapshot_id at the pinned commit),
    // so an identifier the library would refuse never reaches a dispatch.
    public const int MinimumSnapshotIdLength = 3;
    public const int MaximumSnapshotIdLength = 64;
    private const int MaximumIndexDigits = 9;

    public AgentDesktopElementRef(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!TrySplit(value, out var snapshotId))
        {
            throw new ArgumentException(
                "A desktop element reference must be a snapshot-qualified ref such as @s8f3k2p9:e5.",
                nameof(value));
        }

        Value = value;
        SnapshotId = snapshotId;
    }

    public string Value { get; }

    public string SnapshotId { get; }

    public static bool IsValid(string? value) => value is not null && TrySplit(value, out _);

    /// <summary>True when <paramref name="snapshotId"/> can qualify a reference.</summary>
    public static bool IsValidSnapshotId(string? snapshotId) =>
        snapshotId is { Length: >= MinimumSnapshotIdLength and <= MaximumSnapshotIdLength }
        && !snapshotId.AsSpan().ContainsAnyExcept(SnapshotIdCharacters);

    public override string ToString() => Value;

    // Parsed by hand rather than by pattern: the grammar is three fixed
    // pieces, and every character is checked against an ASCII allowlist.
    private static bool TrySplit(string value, out string snapshotId)
    {
        snapshotId = string.Empty;
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (value.Length == 0 || value[0] != '@' || separator < 1)
        {
            return false;
        }

        var snapshot = value[1..separator];
        var element = value.AsSpan(separator + 1);
        if (!IsValidSnapshotId(snapshot) || element.Length < 2 || element[0] != 'e')
        {
            return false;
        }

        var index = element[1..];
        if (index.Length > MaximumIndexDigits
            || index.ContainsAnyExceptInRange('0', '9')
            || (index.Length > 1 && index[0] == '0'))
        {
            return false;
        }

        snapshotId = snapshot;
        return true;
    }

    private static readonly System.Buffers.SearchValues<char> SnapshotIdCharacters =
        System.Buffers.SearchValues.Create(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");
}

/// <summary>Which application-owned surface a snapshot reads.</summary>
public enum AgentDesktopSurface
{
    Window,
    Focused,
    Menu,
    Menubar,
    Sheet,
    Popover,
    Alert,
}

public enum AgentDesktopClickKind
{
    Single,
    Double,
    Right,
}

public enum AgentDesktopToggleKind
{
    Toggle,
    Check,
    Uncheck,
}

public enum AgentDesktopExpandKind
{
    Expand,
    Collapse,
}

public enum AgentDesktopScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

public enum AgentDesktopKeyModifier
{
    Command,
    Control,
    Alt,
    Shift,
}

/// <summary>How the engine identified the application behind a process.</summary>
public enum AgentDesktopApplicationIdentityKind
{
    /// <summary>
    /// The identifier agent-desktop's application listing reports: the bundle
    /// identifier on macOS, the desktop-entry identifier on Linux.
    /// </summary>
    BundleIdentifier,

    /// <summary>
    /// The resolved executable path, for a process the listing reports no
    /// identifier for.
    /// </summary>
    ExecutablePath,
}

/// <summary>
/// What an application is, apart from the process it currently runs as and
/// the name it displays. The engine establishes it for the process it
/// observed; it is bound into every proposal that acts on an observed target
/// and shown on the approval card, so an approval names more than a display
/// name that any program may use.
/// </summary>
/// <remarks>
/// A bundle identifier is the application's own declaration, not a verified
/// signature. It tells applications apart; it does not prove who made one.
/// </remarks>
public sealed record AgentDesktopApplicationIdentity
{
    public const int MaximumBytes = 1_024;

    public AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind kind, string value)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!AgentDesktopText.IsBoundedSingleLine(value, MaximumBytes))
        {
            throw new ArgumentException(
                "An application identity must be bounded single-line text.",
                nameof(value));
        }

        Kind = kind;
        Value = value;
    }

    public AgentDesktopApplicationIdentityKind Kind { get; }

    public string Value { get; }
}

/// <summary>
/// Trusted-path context for the element a ref names: the application, process,
/// window, and element the engine observed when it minted the ref. It is
/// built from the run's own snapshot results, never from model arguments, so
/// the approval card can say what will be clicked without consulting the
/// model.
/// </summary>
public sealed record AgentDesktopElementTarget
{
    public AgentDesktopElementTarget(
        AgentDesktopElementRef reference,
        AgentDesktopDisplayText application,
        AgentDesktopApplicationIdentity applicationIdentity,
        uint processId,
        string? processInstance,
        string? windowId,
        AgentDesktopDisplayText? windowTitle,
        DateTimeOffset observedAt,
        AgentDesktopDisplayText role,
        AgentDesktopDisplayText? name,
        bool isSecure)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(applicationIdentity);
        ArgumentNullException.ThrowIfNull(role);
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (processInstance is not null
            && !AgentDesktopText.IsBoundedSingleLine(processInstance, 128))
        {
            throw new ArgumentException(
                "A process instance token must be bounded single-line text.",
                nameof(processInstance));
        }

        if (windowId is not null
            && (string.IsNullOrWhiteSpace(windowId)
                || !AgentDesktopText.IsBoundedSingleLine(windowId, AgentDesktopWindowTarget.MaximumWindowIdBytes)))
        {
            throw new ArgumentException(
                "A window identifier must be bounded single-line text.",
                nameof(windowId));
        }

        Reference = reference;
        Application = application;
        ApplicationIdentity = applicationIdentity;
        ProcessId = processId;
        ProcessInstance = processInstance;
        WindowId = windowId;
        WindowTitle = windowTitle;
        ObservedAt = observedAt;
        Role = role;
        Name = name;
        IsSecure = isSecure;
    }

    public AgentDesktopElementRef Reference { get; }

    public AgentDesktopDisplayText Application { get; }

    public AgentDesktopApplicationIdentity ApplicationIdentity { get; }

    public uint ProcessId { get; }

    /// <summary>agent-desktop's process-generation token, when the platform supplies one.</summary>
    public string? ProcessInstance { get; }

    /// <summary>
    /// The window the snapshot walked, as its envelope identified it. Null
    /// when the envelope named no window, as for a menu bar.
    /// </summary>
    public string? WindowId { get; }

    public AgentDesktopDisplayText? WindowTitle { get; }

    /// <summary>
    /// When the run received the snapshot the ref came from. The approval
    /// card shows it as an age. It is not part of the proposal digest: the
    /// same element observed a moment later is the same target.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    public AgentDesktopDisplayText Role { get; }

    public AgentDesktopDisplayText? Name { get; }

    /// <summary>
    /// True for secure text fields. Typing, setting, clearing and key presses
    /// are refused on them.
    /// </summary>
    public bool IsSecure { get; }
}

/// <summary>A window observed through <c>desktop.list_windows</c>.</summary>
public sealed record AgentDesktopWindowTarget
{
    public const int MaximumWindowIdBytes = 128;

    public AgentDesktopWindowTarget(
        AgentDesktopApplicationId application,
        AgentDesktopApplicationIdentity applicationIdentity,
        string windowId,
        AgentDesktopDisplayText title,
        uint processId,
        string? processInstance,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(applicationIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        ArgumentNullException.ThrowIfNull(title);
        if (!AgentDesktopText.IsBoundedSingleLine(windowId, MaximumWindowIdBytes))
        {
            throw new ArgumentException(
                "A window identifier must be bounded single-line text.",
                nameof(windowId));
        }

        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (processInstance is not null
            && !AgentDesktopText.IsBoundedSingleLine(processInstance, 128))
        {
            throw new ArgumentException(
                "A process instance token must be bounded single-line text.",
                nameof(processInstance));
        }

        Application = application;
        ApplicationIdentity = applicationIdentity;
        WindowId = windowId;
        Title = title;
        ProcessId = processId;
        ProcessInstance = processInstance;
        ObservedAt = observedAt;
    }

    public AgentDesktopApplicationId Application { get; }

    public AgentDesktopApplicationIdentity ApplicationIdentity { get; }

    public string WindowId { get; }

    public AgentDesktopDisplayText Title { get; }

    public uint ProcessId { get; }

    public string? ProcessInstance { get; }

    /// <summary>
    /// When the run received the listing the window came from. Shown on the
    /// approval card as an age and left out of the proposal digest.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }
}

/// <summary>
/// A running application the run observed in a <c>desktop.list_apps</c> or
/// <c>desktop.list_windows</c> result or a snapshot: the process, its
/// generation token when one was observed, and its identity. It is built from
/// host results only, never from model arguments, so a close names the
/// process the user saw rather than whatever answers to a name at dispatch.
/// </summary>
public sealed record AgentDesktopApplicationTarget
{
    public AgentDesktopApplicationTarget(
        AgentDesktopDisplayText application,
        AgentDesktopApplicationIdentity applicationIdentity,
        uint processId,
        string? processInstance,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(applicationIdentity);
        if (processId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (processInstance is not null
            && !AgentDesktopText.IsBoundedSingleLine(processInstance, 128))
        {
            throw new ArgumentException(
                "A process instance token must be bounded single-line text.",
                nameof(processInstance));
        }

        Application = application;
        ApplicationIdentity = applicationIdentity;
        ProcessId = processId;
        ProcessInstance = processInstance;
        ObservedAt = observedAt;
    }

    /// <summary>The display name the observation reported; shown, not resolved.</summary>
    public AgentDesktopDisplayText Application { get; }

    public AgentDesktopApplicationIdentity ApplicationIdentity { get; }

    public uint ProcessId { get; }

    /// <summary>
    /// agent-desktop's process-generation token. Only a window carries one,
    /// so an application observed through the application list alone, or
    /// without a window, is bound by process ID and identity.
    /// </summary>
    public string? ProcessInstance { get; }

    /// <summary>
    /// When the run received the observation. Shown on the approval card as
    /// an age and left out of the proposal digest.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }
}

/// <summary>
/// One key with at most one modifier. The vocabulary is the subset of
/// agent-desktop's macOS key map that has a meaning on every platform; held
/// keys and multi-modifier chords are not representable.
/// </summary>
public sealed record AgentDesktopKeyPress
{
    private static readonly HashSet<string> NamedKeys = new(StringComparer.Ordinal)
    {
        "return", "escape", "tab", "space", "delete", "forwarddelete",
        "home", "end", "pageup", "pagedown", "left", "right", "down", "up",
        "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12",
    };

    public AgentDesktopKeyPress(string key, AgentDesktopKeyModifier? modifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!IsKnownKey(key))
        {
            throw new ArgumentException(
                "The key must be one lowercase letter, one digit, or a named key.",
                nameof(key));
        }

        if (modifier is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(modifier));
        }

        // Command-Q quits the application; agent-desktop refuses it as well,
        // and refusing here keeps it out of the approval card entirely.
        if (modifier == AgentDesktopKeyModifier.Command && key is "q")
        {
            throw new ArgumentException(
                "Quitting an application through a key chord is not available; use desktop.close_app.",
                nameof(key));
        }

        Key = key;
        Modifier = modifier;
    }

    public string Key { get; }

    public AgentDesktopKeyModifier? Modifier { get; }

    public static bool IsKnownKey(string key) =>
        (key.Length == 1 && (char.IsAsciiLetterLower(key[0]) || char.IsAsciiDigit(key[0])))
        || NamedKeys.Contains(key);

    public static string ModifierToken(AgentDesktopKeyModifier modifier) => modifier switch
    {
        AgentDesktopKeyModifier.Command => "cmd",
        AgentDesktopKeyModifier.Control => "ctrl",
        AgentDesktopKeyModifier.Alt => "alt",
        AgentDesktopKeyModifier.Shift => "shift",
        _ => throw new ArgumentOutOfRangeException(nameof(modifier)),
    };
}

/// <summary>What <c>desktop.wait</c> polls for.</summary>
public abstract record AgentDesktopWaitCondition
{
    public const int MinimumTimeoutMilliseconds = 100;
    public const int MaximumTimeoutMilliseconds = 30_000;

    private AgentDesktopWaitCondition()
    {
    }

    /// <summary>The application whose windows and tree are polled.</summary>
    public abstract AgentDesktopApplicationId Application { get; }

    /// <summary>The text or title fragment being waited for.</summary>
    public abstract string Value { get; }

    /// <summary>Text appears anywhere in the application's current tree.</summary>
    public sealed record Text : AgentDesktopWaitCondition
    {
        public Text(AgentDesktopApplicationId application, string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!AgentDesktopText.IsBoundedSingleLine(value, 256))
            {
                throw new ArgumentException(
                    "Wait text must be bounded single-line text.",
                    nameof(value));
            }

            Application = application;
            Value = value;
        }

        public override AgentDesktopApplicationId Application { get; }

        public override string Value { get; }
    }

    /// <summary>
    /// A window whose title contains this text exists for the application.
    /// agent-desktop matches by substring, so the approval text says "contains".
    /// </summary>
    public sealed record WindowTitle : AgentDesktopWaitCondition
    {
        public WindowTitle(AgentDesktopApplicationId application, string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!AgentDesktopText.IsBoundedSingleLine(value, 256))
            {
                throw new ArgumentException(
                    "A window title must be bounded single-line text.",
                    nameof(value));
            }

            Application = application;
            Value = value;
        }

        public override AgentDesktopApplicationId Application { get; }

        public override string Value { get; }
    }
}

/// <summary>
/// Text the model wants typed into or set on a control. Strict Unicode, at
/// most 2,048 UTF-8 bytes, only tab and line-break control characters, and
/// never a likely literal secret: the user types credentials themselves.
/// </summary>
public sealed record AgentDesktopInputText
{
    public const int MaximumBytes = 2_048;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public AgentDesktopInputText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("Input text must be well-formed Unicode.", nameof(value));
        }

        if (value.Length == 0
            || byteCount > MaximumBytes
            || value.EnumerateRunes().Any(rune =>
                rune.Value is not ('\t' or '\n' or '\r')
                && Rune.GetUnicodeCategory(rune) is
                    UnicodeCategory.Control
                    or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator))
        {
            throw new ArgumentException(
                "Input text must be non-empty, at most 2,048 UTF-8 bytes, and free of control characters other than tab and line breaks.",
                nameof(value));
        }

        if (AgentLiteralSecretValidator.ContainsLikelyLiteralSecret(value))
        {
            throw new ArgumentException(
                "Input text that looks like a literal secret is refused.",
                nameof(value));
        }

        Value = value;
        ByteCount = byteCount;
    }

    public string Value { get; }

    public int ByteCount { get; }
}

/// <summary>
/// The closed set of desktop-automation operations that can be prepared for
/// agent authorization. Provider-defined names and arguments cannot extend it.
/// </summary>
public abstract record AgentDesktopRequest
{
    public const int MinimumSnapshotDepth = 1;
    public const int MaximumSnapshotDepth = 32;
    public const int DefaultSnapshotDepth = 16;
    public const int MinimumScrollAmount = 1;
    public const int MaximumScrollAmount = 50;
    public const int MinimumLaunchTimeoutMilliseconds = 1_000;
    public const int MaximumLaunchTimeoutMilliseconds = 15_000;

    private AgentDesktopRequest()
    {
    }

    public abstract string ToolName { get; }

    /// <summary>True when agent-desktop delivers input or changes an application.</summary>
    public abstract bool IsMutation { get; }

    public sealed record ListApps : AgentDesktopRequest
    {
        public override string ToolName => DesktopAgentToolNames.ListApps;

        public override bool IsMutation => false;
    }

    public sealed record ListWindows(AgentDesktopApplicationId? Application) : AgentDesktopRequest
    {
        public override string ToolName => DesktopAgentToolNames.ListWindows;

        public override bool IsMutation => false;
    }

    public sealed record Snapshot : AgentDesktopRequest
    {
        public Snapshot(
            AgentDesktopApplicationId application,
            AgentDesktopSurface surface = AgentDesktopSurface.Window,
            bool interactiveOnly = true,
            bool compact = true,
            int maximumDepth = DefaultSnapshotDepth)
        {
            if (!Enum.IsDefined(surface))
            {
                throw new ArgumentOutOfRangeException(nameof(surface));
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(maximumDepth, MinimumSnapshotDepth);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth, MaximumSnapshotDepth);
            Application = application;
            Surface = surface;
            InteractiveOnly = interactiveOnly;
            Compact = compact;
            MaximumDepth = maximumDepth;
        }

        public AgentDesktopApplicationId Application { get; }

        public AgentDesktopSurface Surface { get; }

        public bool InteractiveOnly { get; }

        public bool Compact { get; }

        public int MaximumDepth { get; }

        public override string ToolName => DesktopAgentToolNames.Snapshot;

        public override bool IsMutation => false;
    }

    public sealed record Wait : AgentDesktopRequest
    {
        public Wait(AgentDesktopWaitCondition condition, int timeoutMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(condition);
            ArgumentOutOfRangeException.ThrowIfLessThan(
                timeoutMilliseconds,
                AgentDesktopWaitCondition.MinimumTimeoutMilliseconds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                timeoutMilliseconds,
                AgentDesktopWaitCondition.MaximumTimeoutMilliseconds);
            Condition = condition;
            TimeoutMilliseconds = timeoutMilliseconds;
        }

        public AgentDesktopWaitCondition Condition { get; }

        public int TimeoutMilliseconds { get; }

        public override string ToolName => DesktopAgentToolNames.Wait;

        public override bool IsMutation => false;
    }

    /// <summary>Shared shape of every action that targets one observed element.</summary>
    public abstract record ElementAction : AgentDesktopRequest
    {
        private protected ElementAction(AgentDesktopElementTarget target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public AgentDesktopElementTarget Target { get; }

        public override bool IsMutation => true;
    }

    public sealed record Click : ElementAction
    {
        public Click(AgentDesktopElementTarget target, AgentDesktopClickKind kind = AgentDesktopClickKind.Single)
            : base(target)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
        }

        public AgentDesktopClickKind Kind { get; }

        public override string ToolName => DesktopAgentToolNames.Click;
    }

    public sealed record SetValue : ElementAction
    {
        public SetValue(AgentDesktopElementTarget target, AgentDesktopInputText text)
            : base(target)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            RejectSecureTarget(target);
        }

        public AgentDesktopInputText Text { get; }

        public override string ToolName => DesktopAgentToolNames.SetValue;
    }

    public sealed record TypeText : ElementAction
    {
        public TypeText(AgentDesktopElementTarget target, AgentDesktopInputText text)
            : base(target)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            RejectSecureTarget(target);
        }

        public AgentDesktopInputText Text { get; }

        public override string ToolName => DesktopAgentToolNames.TypeText;
    }

    public sealed record Clear : ElementAction
    {
        public Clear(AgentDesktopElementTarget target)
            : base(target)
        {
            RejectSecureTarget(target);
        }

        public override string ToolName => DesktopAgentToolNames.Clear;
    }

    /// <summary>
    /// Chooses one option of a pop-up, combo box or list by its visible name.
    /// agent-desktop matches the option name itself; Asura never sends an index.
    /// </summary>
    public sealed record Select : ElementAction
    {
        public Select(AgentDesktopElementTarget target, AgentDesktopInputText option)
            : base(target)
        {
            Option = option ?? throw new ArgumentNullException(nameof(option));
        }

        public AgentDesktopInputText Option { get; }

        public override string ToolName => DesktopAgentToolNames.Select;
    }

    public sealed record Toggle : ElementAction
    {
        public Toggle(AgentDesktopElementTarget target, AgentDesktopToggleKind kind = AgentDesktopToggleKind.Toggle)
            : base(target)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
        }

        public AgentDesktopToggleKind Kind { get; }

        public override string ToolName => DesktopAgentToolNames.Toggle;
    }

    public sealed record Expand : ElementAction
    {
        public Expand(AgentDesktopElementTarget target, AgentDesktopExpandKind kind = AgentDesktopExpandKind.Expand)
            : base(target)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
        }

        public AgentDesktopExpandKind Kind { get; }

        public override string ToolName => DesktopAgentToolNames.Expand;
    }

    /// <summary>
    /// Scrolls a container by direction and amount, or, with no direction,
    /// scrolls the element itself into view.
    /// </summary>
    public sealed record Scroll : ElementAction
    {
        public Scroll(
            AgentDesktopElementTarget target,
            AgentDesktopScrollDirection? direction = null,
            int amount = MinimumScrollAmount)
            : base(target)
        {
            if (direction is { } value && !Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(amount, MinimumScrollAmount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, MaximumScrollAmount);
            Direction = direction;
            Amount = amount;
        }

        public AgentDesktopScrollDirection? Direction { get; }

        public int Amount { get; }

        public bool IsScrollIntoView => Direction is null;

        public override string ToolName => DesktopAgentToolNames.Scroll;
    }

    public sealed record PressKey : ElementAction
    {
        public PressKey(AgentDesktopElementTarget target, AgentDesktopKeyPress key)
            : base(target)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            RejectSecureTarget(target);
        }

        public AgentDesktopKeyPress Key { get; }

        public override string ToolName => DesktopAgentToolNames.PressKey;
    }

    public sealed record FocusWindow(AgentDesktopWindowTarget Window) : AgentDesktopRequest
    {
        public override string ToolName => DesktopAgentToolNames.FocusWindow;

        public override bool IsMutation => true;
    }

    /// <summary>
    /// Starts an application, or brings a running one forward, by the name
    /// or bundle identifier the model supplied. Unlike a close it binds no
    /// observed process: an application that is not yet running has nothing
    /// to observe, so the name is the whole target and the engine classifies
    /// the started process after the fact.
    /// </summary>
    public sealed record LaunchApp : AgentDesktopRequest
    {
        public LaunchApp(AgentDesktopApplicationId application, int timeoutMilliseconds = 10_000)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, MinimumLaunchTimeoutMilliseconds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMilliseconds, MaximumLaunchTimeoutMilliseconds);
            Application = application;
            TimeoutMilliseconds = timeoutMilliseconds;
        }

        public AgentDesktopApplicationId Application { get; }

        public int TimeoutMilliseconds { get; }

        public override string ToolName => DesktopAgentToolNames.LaunchApp;

        public override bool IsMutation => true;
    }

    /// <summary>
    /// A graceful close of one observed application; force-quit is
    /// deliberately not representable. The engine dispatches it only while
    /// the live process with that ID still has the observed identity and
    /// generation token.
    /// </summary>
    public sealed record CloseApp : AgentDesktopRequest
    {
        public CloseApp(AgentDesktopApplicationTarget target)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public AgentDesktopApplicationTarget Target { get; }

        public override string ToolName => DesktopAgentToolNames.CloseApp;

        public override bool IsMutation => true;
    }

    // A secure field takes nothing from the agent that changes or probes
    // its contents: no text, no clearing, and no key presses, which would
    // otherwise spell a password one approved key at a time.
    private static void RejectSecureTarget(AgentDesktopElementTarget target)
    {
        if (target.IsSecure)
        {
            throw new ArgumentException(
                "A secure field accepts no text, clearing or key presses from the agent; the user types credentials themselves.",
                nameof(target));
        }
    }
}

internal static class AgentDesktopText
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static bool IsBoundedSingleLine(string value, int maximumBytes)
    {
        try
        {
            return StrictUtf8.GetByteCount(value) <= maximumBytes
                && value.EnumerateRunes().All(rune =>
                    Rune.GetUnicodeCategory(rune) is not (
                        UnicodeCategory.Control
                        or UnicodeCategory.Format
                        or UnicodeCategory.LineSeparator
                        or UnicodeCategory.ParagraphSeparator));
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
