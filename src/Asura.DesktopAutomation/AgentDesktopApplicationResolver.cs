using System.Buffers;
using System.Text;
using Asura.Application;

namespace Asura.DesktopAutomation;

/// <summary>One entry of agent-desktop's running-application list.</summary>
internal readonly record struct AgentDesktopRunningApp(string Name, uint ProcessId, string? BundleId);

/// <summary>
/// Turns the application the model named into exactly one running, foreign
/// application, or a closed refusal.
///
/// agent-desktop resolves an application by name on every call, so Asura
/// cannot hand it a process ID. Instead Asura resolves the name itself against
/// the same list, proves the result is unique and is not Asura, and passes the
/// exact display name on. The second pass exists because agent-desktop matches
/// that display name against both names and bundle identifiers: if the name
/// would also match some other entry, the library could pick a process Asura
/// never inspected, so the request is refused as ambiguous.
///
/// Both passes therefore have to match at least everything the library
/// would. Under a narrower rule a same-account application could pick a
/// display name that Asura takes for that application alone and the library
/// takes for Asura as well.
/// </summary>
/// <remarks>
/// The listing can also be incomplete. agent-desktop merges AppKit's running
/// applications with <c>ps</c>, and a process <c>ps</c> reports before AppKit
/// has registered it, or while this process's AppKit view is stale, is listed
/// under its bundle directory's name with no bundle identifier. An application
/// copied into Asura's install root and opened right after the original quit
/// was listed exactly so: the bundle identifier matched nothing and was let
/// through, while the display name was refused. So the question "could the
/// identifier reach Asura or a process nobody can inspect" is answered on a
/// wider rule than "which process does it name" (<see cref="CouldAnswer"/>):
/// an entry without a listed bundle identifier may have the one asked for,
/// any entry answers to its executable's file name as the library's alias
/// rule allows, and a path or <c>.app</c> name answers for the application
/// it names. Any such entry that is Asura or cannot be inspected refuses the
/// identifier. Only the narrow rule picks a target.
/// </remarks>
internal static class AgentDesktopApplicationResolver
{
    private const string BundleSuffix = ".app";

    // The characters agent-desktop removes from both sides before it compares
    // application names (is_bidirectional_control at the pinned commit).
    private static readonly SearchValues<char> BidirectionalControls = SearchValues.Create(
        "؜‎‏‪‫‬‭‮⁦⁧⁨⁩");

    /// <returns>Null when <paramref name="application"/> is the resolved target.</returns>
    public static AgentDesktopFailureCode? Resolve(
        IReadOnlyList<AgentDesktopRunningApp> running,
        string identifier,
        AgentDesktopSelfTargetGuard guard,
        out AgentDesktopRunningApp application)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(guard);
        application = default;
        if (AgentDesktopSelfTargetGuard.IsAsuraIdentifier(identifier))
        {
            return AgentDesktopFailureCode.SelfTarget;
        }

        var named = Classify(running, identifier, guard);
        if (named.Refusal is { } refused)
        {
            return refused;
        }

        if (named.Foreign.Count != 1)
        {
            return named.Foreign.Count == 0
                ? AgentDesktopFailureCode.AppNotFound
                : AgentDesktopFailureCode.AmbiguousTarget;
        }

        var candidate = named.Foreign[0];
        var byDisplayName = Classify(running, candidate.Name, guard);
        if (byDisplayName.Refusal is { } refusedByDisplayName)
        {
            return refusedByDisplayName;
        }

        if (byDisplayName.Foreign.Count != 1)
        {
            return AgentDesktopFailureCode.AmbiguousTarget;
        }

        application = candidate;
        return null;
    }

    /// <summary>
    /// Finds the live entry of a process the run observed, by process ID, for
    /// a request the library will receive by name. Null when exactly one
    /// entry has that process ID, it still has the observed identity, and
    /// its display name resolves to that same process under
    /// <see cref="Resolve"/>; otherwise <c>StaleRef</c> for a process that is
    /// gone or changed identity, or whatever <see cref="Resolve"/> refused
    /// the name for.
    /// </summary>
    public static AgentDesktopFailureCode? ResolveObserved(
        IReadOnlyList<AgentDesktopRunningApp> running,
        uint processId,
        AgentDesktopApplicationIdentity identity,
        AgentDesktopSelfTargetGuard guard,
        out AgentDesktopRunningApp application)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(guard);
        application = default;
        var live = running.Where(app => app.ProcessId == processId).ToArray();
        if (live.Length != 1 || guard.IdentifyApplication(processId, live[0].BundleId) != identity)
        {
            return AgentDesktopFailureCode.StaleRef;
        }

        if (Resolve(running, live[0].Name, guard, out var resolved) is { } refused)
        {
            return refused;
        }

        if (resolved.ProcessId != processId)
        {
            return AgentDesktopFailureCode.AmbiguousTarget;
        }

        application = resolved;
        return null;
    }

    /// <summary>
    /// The refusal SessionHost gives before any approval is requested: the
    /// identifier, or the display name a unique match would be handed to the
    /// library under, could reach Asura (<c>SelfTarget</c>) or a process that
    /// cannot be inspected (<c>AmbiguousTarget</c>). Null when neither holds;
    /// an application that is not running or a choice between foreign
    /// applications is left to the dispatch-time resolution.
    /// </summary>
    public static AgentDesktopFailureCode? RefuseBeforeAuthority(
        IReadOnlyList<AgentDesktopRunningApp> running,
        string identifier,
        AgentDesktopSelfTargetGuard guard)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(guard);
        if (AgentDesktopSelfTargetGuard.IsAsuraIdentifier(identifier))
        {
            return AgentDesktopFailureCode.SelfTarget;
        }

        var named = Classify(running, identifier, guard);
        if (named.Refusal is { } refused)
        {
            return refused;
        }

        return named.Foreign.Count == 1
            ? Classify(running, named.Foreign[0].Name, guard).Refusal
            : null;
    }

    /// <summary>
    /// True when no running application other than
    /// <paramref name="processId"/> answers to <paramref name="displayName"/>
    /// under the library's matching rule. The process itself may be gone.
    /// Asked after a call that addressed the application by that name, to
    /// learn whether the library could have reached a different process.
    /// </summary>
    public static bool AnswersOnlyFor(
        IReadOnlyList<AgentDesktopRunningApp> running,
        string displayName,
        uint processId)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(displayName);
        return running.All(app => app.ProcessId == processId || !IsMatch(app, displayName));
    }

    private static Matches Classify(
        IReadOnlyList<AgentDesktopRunningApp> running,
        string identifier,
        AgentDesktopSelfTargetGuard guard)
    {
        var foreign = new List<AgentDesktopRunningApp>();
        var touchesAsura = false;
        var touchesUnverifiable = false;
        var namesAnyEntry = running.Any(app => IsNameMatch(app.Name, identifier));
        foreach (var app in running)
        {
            var ownership = guard.Classify(app.ProcessId, app.BundleId);
            if (ownership == AgentDesktopProcessOwnership.Foreign)
            {
                // Only the library's own rule picks a target.
                if (IsMatch(app, identifier))
                {
                    foreign.Add(app);
                }

                continue;
            }

            // Asura and processes that cannot be inspected are never a
            // target; whatever might let the identifier reach one refuses it.
            if (!CouldAnswer(app, identifier, guard.ExecutablePath(app.ProcessId), namesAnyEntry))
            {
                continue;
            }

            if (ownership == AgentDesktopProcessOwnership.Asura)
            {
                touchesAsura = true;
            }
            else
            {
                touchesUnverifiable = true;
            }
        }

        return new Matches(foreign, touchesAsura, touchesUnverifiable);
    }

    // agent-desktop's own rule (AppInfo::matches_identifier at the pinned
    // commit), or anything wider: the whole name or the whole bundle
    // identifier, ignoring case. Names are compared without bidirectional
    // control characters, and a name with a trailing ".exe" also answers to
    // the name without it.
    private static bool IsMatch(AgentDesktopRunningApp app, string identifier) =>
        IsNameMatch(app.Name, identifier)
        || (app.BundleId is not null && app.BundleId.Equals(identifier, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the library could take <paramref name="app"/> for
    /// <paramref name="identifier"/>, now or once the listing has caught up.
    /// Wider than <see cref="IsMatch"/> on purpose; see the type's remarks.
    /// </summary>
    /// <param name="executablePath">The process's resolved executable, or null when it cannot be read.</param>
    /// <param name="namesAnyEntry">Whether any listed application has the identifier as its display name.</param>
    private static bool CouldAnswer(
        AgentDesktopRunningApp app,
        string identifier,
        string? executablePath,
        bool namesAnyEntry)
    {
        if (IsMatch(app, identifier))
        {
            return true;
        }

        // "Notes.app" and "/Applications/Notes.app" name the application
        // "Notes" to Launch Services.
        if (BareName(identifier) is { } bare && IsNameMatch(app.Name, bare))
        {
            return true;
        }

        // An entry the listing took from ps, or one AppKit had not finished
        // registering, has no bundle identifier yet: it may be the one asked
        // for (the library's looks_like_bundle_id shape).
        if (app.BundleId is null && LooksLikeBundleIdentifier(identifier))
        {
            return true;
        }

        // The library also answers to the executable's file name, but only
        // when no application has the identifier as its display name. A
        // process whose executable cannot be read could have any.
        if (namesAnyEntry)
        {
            return false;
        }

        return executablePath is null
            || Path.GetFileName(executablePath).Equals(identifier, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The application name in a path or <c>.app</c> bundle name, or null
    /// when the identifier is neither.
    /// </summary>
    internal static string? BareName(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        var name = identifier.TrimEnd('/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(BundleSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^BundleSuffix.Length];
        }

        return name.Length > 0 && !string.Equals(name, identifier, StringComparison.Ordinal) ? name : null;
    }

    private static bool LooksLikeBundleIdentifier(string identifier) =>
        identifier.Contains('.', StringComparison.Ordinal)
        && !identifier.EndsWith(BundleSuffix, StringComparison.OrdinalIgnoreCase)
        && !identifier.Contains(' ', StringComparison.Ordinal)
        && !identifier.Contains('/', StringComparison.Ordinal);

    private static bool IsNameMatch(string name, string identifier)
    {
        var actual = WithoutBidirectionalControls(name);
        var expected = WithoutBidirectionalControls(identifier);
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || actual.Equals(expected + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutBidirectionalControls(string text)
    {
        if (!text.AsSpan().ContainsAny(BidirectionalControls))
        {
            return text;
        }

        var kept = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (!BidirectionalControls.Contains(character))
            {
                kept.Append(character);
            }
        }

        return kept.ToString();
    }

    private readonly record struct Matches(
        List<AgentDesktopRunningApp> Foreign,
        bool TouchesAsura,
        bool TouchesUnverifiable)
    {
        /// <summary>Asura first: it is the refusal the model must not work around.</summary>
        public AgentDesktopFailureCode? Refusal =>
            TouchesAsura ? AgentDesktopFailureCode.SelfTarget
            : TouchesUnverifiable ? AgentDesktopFailureCode.AmbiguousTarget
            : null;
    }
}
