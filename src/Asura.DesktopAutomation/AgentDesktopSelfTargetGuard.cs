using Asura.Application;

namespace Asura.DesktopAutomation;

internal enum AgentDesktopProcessOwnership
{
    /// <summary>Another application; desktop automation may address it.</summary>
    Foreign,

    /// <summary>Asura itself or a process that holds Asura's authority.</summary>
    Asura,

    /// <summary>
    /// The process could not be inspected. It is treated as not addressable:
    /// a process whose executable Asura cannot name might be Asura's own.
    /// </summary>
    Unverifiable,
}

/// <summary>Read-only facts about a running process, by process ID.</summary>
internal interface IAgentDesktopProcessInspector
{
    /// <summary>The resolved executable path, or null when it cannot be read.</summary>
    string? GetExecutablePath(uint processId);

    uint? GetParentProcessId(uint processId);
}

/// <summary>
/// Decides whether a process is Asura. Driving Asura's own window would let
/// the model click its own approval card, so every observation and action is
/// refused for these processes before policy and without an override
/// (ADR 0061, "Self-operation guard").
///
/// A process is Asura when any of these hold:
/// <list type="bullet">
/// <item>it is this process;</item>
/// <item>its executable lies inside Asura's bundle or install root, which
/// covers the CEF helpers, the workspace runtime, the sql-language worker and
/// the connection backend;</item>
/// <item>this process is its direct parent, on macOS only, which covers a
/// helper started from a path outside the install root;</item>
/// <item>its bundle identifier is Asura's or a child of it.</item>
/// </list>
///
/// The parent rule cannot hold on Linux: agent-desktop launches an
/// application there as a direct child of the host process, so every
/// application <c>desktop.launch_app</c> started would be judged Asura. On
/// macOS LaunchServices starts applications, and they are children of launchd.
/// </summary>
/// <remarks>
/// On Linux the guard is therefore the install-root rule and nothing else:
/// <list type="bullet">
/// <item>there is no parent rule, so a process Asura started from an
/// executable outside its install root (a system interpreter, a user-chosen
/// shell, a tool on <c>PATH</c>) is judged foreign;</item>
/// <item>there are no bundle identifiers. What agent-desktop lists as an
/// application's identifier is a desktop-entry name the application chose,
/// so the <c>sh.asura</c> rule only catches a process that declares it;</item>
/// <item>the executable path is the kernel's <c>/proc/&lt;pid&gt;/exe</c> link.
/// A helper whose file was replaced by an update reads as
/// <c>&lt;path&gt; (deleted)</c> and stays inside the install root, but one
/// whose install root was renamed before it was removed reports the renamed
/// directory and is judged foreign until Asura restarts.</item>
/// </list>
/// A second Asura installation in another directory is foreign on both
/// platforms unless it declares Asura's bundle identifier.
/// </remarks>
internal sealed class AgentDesktopSelfTargetGuard
{
    private const string BundleIdentifier = "sh.asura";
    private const string DisplayName = "Asura";

    private readonly uint _ownProcessId;
    private readonly string[] _installRoots;
    private readonly IAgentDesktopProcessInspector _inspector;
    private readonly StringComparison _pathComparison;
    private readonly bool _directChildrenAreAsura;

    /// <param name="installRoots">Resolved directories; symlinks already followed.</param>
    public AgentDesktopSelfTargetGuard(
        uint ownProcessId,
        IEnumerable<string> installRoots,
        IAgentDesktopProcessInspector inspector,
        bool caseInsensitivePaths,
        bool directChildrenAreAsura)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownProcessId);
        ArgumentNullException.ThrowIfNull(installRoots);
        ArgumentNullException.ThrowIfNull(inspector);
        _ownProcessId = ownProcessId;
        _installRoots =
        [
            .. installRoots
                .Select(root => root.TrimEnd(Path.DirectorySeparatorChar))
                .Where(root => root.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
        if (_installRoots.Length == 0)
        {
            throw new ArgumentException("At least one install root is required.", nameof(installRoots));
        }

        _inspector = inspector;
        _pathComparison = caseInsensitivePaths ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _directChildrenAreAsura = directChildrenAreAsura;
    }

    public static AgentDesktopSelfTargetGuard ForCurrentProcess() =>
        ForProcess(
            (uint)Environment.ProcessId,
            Environment.ProcessPath,
            AppContext.BaseDirectory,
            new AgentDesktopProcessInspector(),
            OperatingSystem.IsMacOS(),
            AgentDesktopProcessInspector.ResolvePath);

    /// <summary>
    /// The guard for a process with these facts. On macOS the shipped
    /// executable is <c>Asura.app/Contents/MacOS/Asura</c> and the CEF helpers
    /// are <c>Asura.app/Contents/Frameworks/Asura Helper*.app</c>, outside the
    /// application directory, so the bundle derived from
    /// <paramref name="processPath"/> is the root that covers them.
    /// </summary>
    /// <param name="resolvePath">Follows every symlink in a path, or returns null.</param>
    internal static AgentDesktopSelfTargetGuard ForProcess(
        uint ownProcessId,
        string? processPath,
        string? baseDirectory,
        IAgentDesktopProcessInspector inspector,
        bool isMacOS,
        Func<string, string?> resolvePath)
    {
        ArgumentNullException.ThrowIfNull(resolvePath);
        var roots = new List<string>();
        AddRoot(roots, GetInstallRoot(processPath), resolvePath);
        AddRoot(roots, baseDirectory, resolvePath);
        return new AgentDesktopSelfTargetGuard(
            ownProcessId,
            roots,
            inspector,
            caseInsensitivePaths: isMacOS,
            directChildrenAreAsura: isMacOS);
    }

    /// <summary>
    /// True when a model-supplied application identifier names Asura outright,
    /// as a name, a bundle identifier, an <c>.app</c> name or a path to one.
    /// It is a fast refusal only; the process checks in <see cref="Classify"/>
    /// are what the guard relies on.
    /// </summary>
    public static bool IsAsuraIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var trimmed = identifier.Trim();
        var bare = AgentDesktopApplicationResolver.BareName(trimmed) ?? trimmed;
        return bare.Equals(DisplayName, StringComparison.OrdinalIgnoreCase)
            || IsAsuraBundle(trimmed)
            || IsAsuraBundle(bare);
    }

    /// <summary>
    /// The process's resolved executable path, or null when it cannot be
    /// read. The application resolver needs the file name: the library also
    /// answers to it.
    /// </summary>
    public string? ExecutablePath(uint processId) =>
        processId == 0 ? null : _inspector.GetExecutablePath(processId);

    /// <summary>
    /// The refusal for addressing this process, or null when it is another
    /// application. A process that cannot be inspected is reported as not
    /// found: it cannot be shown to be something other than Asura.
    /// </summary>
    public AgentDesktopFailure? RefuseProcess(uint processId) => Classify(processId) switch
    {
        AgentDesktopProcessOwnership.Foreign => null,
        AgentDesktopProcessOwnership.Asura => new AgentDesktopFailure(AgentDesktopFailureCode.SelfTarget),
        _ => new AgentDesktopFailure(AgentDesktopFailureCode.AppNotFound),
    };

    public AgentDesktopProcessOwnership Classify(uint processId, string? bundleId = null)
    {
        if (processId == 0)
        {
            return AgentDesktopProcessOwnership.Unverifiable;
        }

        if (processId == _ownProcessId || (bundleId is not null && IsAsuraBundle(bundleId)))
        {
            return AgentDesktopProcessOwnership.Asura;
        }

        var path = _inspector.GetExecutablePath(processId);
        if (string.IsNullOrEmpty(path))
        {
            return AgentDesktopProcessOwnership.Unverifiable;
        }

        if (_installRoots.Any(root => IsInside(path, root))
            || (_directChildrenAreAsura && _inspector.GetParentProcessId(processId) == _ownProcessId))
        {
            return AgentDesktopProcessOwnership.Asura;
        }

        return AgentDesktopProcessOwnership.Foreign;
    }

    /// <summary>
    /// What a proposal binds as the application behind a process: the
    /// identifier agent-desktop's application listing reported for it, a
    /// bundle identifier on macOS and a desktop-entry identifier on Linux, or
    /// otherwise the process's resolved executable path. The guard answers
    /// because it is what reads process facts. Null when neither can be
    /// carried in a proposal, which leaves the process as unaddressable as
    /// one that cannot be inspected.
    /// </summary>
    public AgentDesktopApplicationIdentity? IdentifyApplication(uint processId, string? bundleId)
    {
        if (AgentDesktopNativeText.IsBoundedToken(bundleId, AgentDesktopApplicationIdentity.MaximumBytes))
        {
            return new AgentDesktopApplicationIdentity(
                AgentDesktopApplicationIdentityKind.BundleIdentifier,
                bundleId!);
        }

        var path = _inspector.GetExecutablePath(processId);
        return AgentDesktopNativeText.IsBoundedToken(path, AgentDesktopApplicationIdentity.MaximumBytes)
            ? new AgentDesktopApplicationIdentity(AgentDesktopApplicationIdentityKind.ExecutablePath, path!)
            : null;
    }

    /// <summary>
    /// The directory that holds everything Asura ships: the <c>.app</c>
    /// bundle when the executable sits in <c>Contents/MacOS</c>, otherwise the
    /// executable's own directory (<c>/opt/asura</c> for the Linux packages).
    /// </summary>
    internal static string? GetInstallRoot(string? executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var contents = Path.GetDirectoryName(directory);
        var bundle = Path.GetDirectoryName(contents);
        var isBundleExecutable = string.Equals(Path.GetFileName(directory), "MacOS", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal)
            && bundle is not null
            && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase);
        return isBundleExecutable ? bundle : directory;
    }

    private static void AddRoot(List<string> roots, string? path, Func<string, string?> resolvePath)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // The inspector reports resolved paths, so the roots are resolved the
        // same way; an unresolvable root is still kept verbatim.
        roots.Add(resolvePath(path) ?? Path.GetFullPath(path));
    }

    private static bool IsAsuraBundle(string bundleId) =>
        bundleId.Equals(BundleIdentifier, StringComparison.OrdinalIgnoreCase)
        || bundleId.StartsWith(BundleIdentifier + ".", StringComparison.OrdinalIgnoreCase);

    private bool IsInside(string path, string root) =>
        path.Equals(root, _pathComparison)
        || path.StartsWith(root + Path.DirectorySeparatorChar, _pathComparison);
}
