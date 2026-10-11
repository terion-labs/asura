using System.Globalization;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Approval and run cards name where an action runs the way a person knows
/// it: by the panel, tab, or workspace title, or by a plain noun such as
/// "Kubernetes". The trusted runtime title can also carry the exact panel and
/// session identifiers that bind the decision, or name the action's category
/// instead of its place; the raw title remains available in Details.
/// </summary>
internal static class AgentTargetPresentation
{
    private static readonly string[] IdentifierSuffixes =
        [" — panel ", " — session ", " · panel ", " · session "];

    // Composer titles that describe what kind of action this is rather than
    // where it runs. They read as jargon after "In" and "Running in".
    private static readonly Dictionary<string, string> CategoryNouns =
        new(StringComparer.Ordinal)
        {
            ["Kubernetes mutation"] = "Kubernetes",
            ["Kubernetes observation"] = "Kubernetes",
            ["Docker observation"] = "Docker",
            ["Database read"] = "Database",
            ["Git repository change"] = "Git repository",
            ["Git repository observation"] = "Git repository",
            ["Fetch HTTP resource"] = "Web",
            ["Read web page"] = "Web",
            ["Google search"] = "Google Search",
        };

    // HumanTitle's stand-ins for a place the run's context does not name.
    // They read well after "In" on a card's primary line; under a "Panel" or
    // "Workspace" label in Details they only repeat the label.
    private const string ThisWorkspace = "this workspace";
    private const string ThisTab = "this tab";
    private const string ThisPanel = "this panel";
    private const string ThisConnection = "this connection";
    private const string SelectedPanels = "selected panels";

    private static readonly HashSet<string> PlaceholderTitles =
        new([ThisWorkspace, ThisTab, ThisPanel, ThisConnection, SelectedPanels], StringComparer.Ordinal);

    private enum ScopeKind
    {
        SelectedPanels,
        Workspace,
        Tab,
        Panel,
        ConnectionSession,
    }

    // Longest prefix first: "Selected panels in workspace" must win over a
    // shorter match.
    private static readonly (string Prefix, ScopeKind Kind)[] IdentifiedScopes =
    [
        ("Selected panels in workspace ", ScopeKind.SelectedPanels),
        ("Connection session ", ScopeKind.ConnectionSession),
        ("Workspace ", ScopeKind.Workspace),
        ("Panel ", ScopeKind.Panel),
        ("Tab ", ScopeKind.Tab),
    ];

    public static string ReadableTitle(string targetTitle)
    {
        ArgumentNullException.ThrowIfNull(targetTitle);
        var end = IdentifierSuffixes
            .Select(suffix => targetTitle.IndexOf(suffix, StringComparison.Ordinal))
            .Where(index => index > 0)
            .DefaultIfEmpty(targetTitle.Length)
            .Min();
        return targetTitle[..end];
    }

    /// <summary>
    /// The place an action runs, for the primary line of a card, its
    /// accessible name, and the live announcement. Never an identifier and
    /// never a category word such as "observation" or "mutation".
    /// </summary>
    /// <param name="targetTitle">The trusted runtime title.</param>
    /// <param name="actionTarget">The exact target of an approval, if known.</param>
    /// <param name="panelId">The panel a running action works in, if known.</param>
    /// <param name="context">The run's panels, which carry their titles.</param>
    public static string HumanTitle(
        string targetTitle,
        AgentTarget? actionTarget = null,
        PanelInstanceId? panelId = null,
        IReadOnlyCollection<GovernedAgentContextItem>? context = null)
    {
        var readable = ReadableTitle(targetTitle);
        context ??= [];
        if (CategoryNouns.TryGetValue(readable, out var noun))
        {
            return PanelTitle(context, panelId, actionTarget) ?? noun;
        }

        foreach (var (prefix, kind) in IdentifiedScopes)
        {
            if (!readable.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var identifier = readable[prefix.Length..];
            if (identifier.Length > 0
                && !identifier.Any(char.IsWhiteSpace)
                && (IsKnownIdentifier(kind, identifier, context, actionTarget)
                    || LooksLikeRuntimeIdentifier(identifier)))
            {
                return ScopeTitle(kind, identifier, context);
            }
        }

        return readable;
    }

    /// <summary>
    /// Where an approval's action runs, one fact per level a person can see:
    /// the panel, its tab, its workspace, and the window, each by its title.
    /// A level the run's context does not name is left out rather than shown
    /// as an identifier; the identifiers themselves live under Technical
    /// Details. When no level can be named, or without an exact target, the
    /// single fact is the human title.
    /// </summary>
    /// <param name="displayTitle">
    /// The human title, the innermost level's fallback unless it is only a
    /// stand-in such as "this panel".
    /// </param>
    /// <param name="actionTarget">The exact target of the approval.</param>
    /// <param name="context">The run's panels, which carry their titles.</param>
    public static IReadOnlyList<AgentApprovalFactViewModel> Place(
        string displayTitle,
        AgentTarget? actionTarget,
        IReadOnlyCollection<GovernedAgentContextItem>? context)
    {
        ArgumentNullException.ThrowIfNull(displayTitle);
        context ??= [];
        var fallback = PlaceholderTitles.Contains(displayTitle) ? null : displayTitle;
        var facts = new List<AgentApprovalFactViewModel>();
        switch (actionTarget)
        {
            case AgentTarget.Panel panel:
                if ((PanelTitle(context, panel.PanelId, null) ?? fallback) is { } panelTitle)
                {
                    facts.Add(new("Panel", panelTitle));
                }

                AddTab(panel.TabId);
                AddWorkspace(panel.WorkspaceId);
                AddWindow(panel.WindowId);
                break;
            case AgentTarget.SelectedPanels selected:
                facts.Add(new(
                    "Panels",
                    string.Join(
                        ", ",
                        selected.Panels.Select(item =>
                            PanelTitle(context, item.PanelId, null) ?? "Untitled panel"))));
                AddWorkspace(selected.Panels[0].WorkspaceId);
                AddWindow(selected.Panels[0].WindowId);
                break;
            case AgentTarget.OpenTab tab:
                AddTab(tab.TabId, fallback);
                AddWorkspace(tab.WorkspaceId);
                AddWindow(tab.WindowId);
                break;
            case AgentTarget.Workspace workspace:
                AddWorkspace(workspace.WorkspaceId, fallback);
                AddWindow(workspace.WindowId);
                break;
            case AgentTarget.ConnectionSession session:
                var item = context.FirstOrDefault(candidate => candidate.SessionId == session.SessionId);
                if ((item?.PanelTitle is { Length: > 0 } title ? title : fallback) is { } connection)
                {
                    facts.Add(new("Connection", connection));
                }

                if (item is not null)
                {
                    AddTab(item.TabId);
                    AddWorkspace(item.WorkspaceId);
                    AddWindow(item.WindowId);
                }

                break;
        }

        // A stand-in leads the line here, so it starts with a capital.
        if (facts.Count == 0)
        {
            facts.Add(new(
                "Target",
                fallback ?? string.Concat(
                    displayTitle[..1].ToUpperInvariant(),
                    displayTitle.AsSpan(1))));
        }

        return facts;

        void AddTab(TabInstanceId id, string? orElse = null)
        {
            if (context.FirstOrDefault(item => item.TabId == id)?.TabTitle is { Length: > 0 } title)
            {
                facts.Add(new("Tab", title));
            }
            else if (orElse is not null)
            {
                facts.Add(new("Tab", orElse));
            }
        }

        void AddWorkspace(WorkspaceInstanceId id, string? orElse = null)
        {
            if (WorkspaceTitle(context, id.Value) is { } title)
            {
                facts.Add(new("Workspace", title));
            }
            else if (orElse is not null)
            {
                facts.Add(new("Workspace", orElse));
            }
        }

        // A window has no title of its own to show. The run's context lists
        // the panels of the window the agent works in, so that one is "This
        // window"; any other is named as such rather than by its identifier.
        // With no context there is nothing to tell them apart by.
        void AddWindow(WindowInstanceId id)
        {
            if (context.Count > 0)
            {
                facts.Add(new(
                    "Window",
                    context.Any(item => item.WindowId == id) ? "This window" : "Another window"));
            }
        }
    }

    private static string ScopeTitle(
        ScopeKind kind,
        string identifier,
        IReadOnlyCollection<GovernedAgentContextItem> context) =>
        kind switch
        {
            ScopeKind.Workspace => WorkspaceTitle(context, identifier) ?? ThisWorkspace,
            ScopeKind.SelectedPanels => WorkspaceTitle(context, identifier) is { } workspace
                ? $"selected panels in {workspace}"
                : SelectedPanels,
            ScopeKind.Tab => context
                .FirstOrDefault(item => Same(item.TabId.Value, identifier))?.TabTitle
                    is { Length: > 0 } tab
                ? tab
                : ThisTab,
            ScopeKind.Panel => context
                .FirstOrDefault(item => Same(item.PanelId.Value, identifier))?.PanelTitle
                    is { Length: > 0 } panel
                ? panel
                : ThisPanel,
            _ => context
                .FirstOrDefault(item => Same(item.SessionId.Value, identifier))?.PanelTitle
                    is { Length: > 0 } session
                ? session
                : ThisConnection,
        };

    private static string? WorkspaceTitle(
        IReadOnlyCollection<GovernedAgentContextItem> context,
        string identifier) =>
        context.FirstOrDefault(item => Same(item.WorkspaceId.Value, identifier))?.WorkspaceTitle
            is { Length: > 0 } title
            ? title
            : null;

    private static string? PanelTitle(
        IReadOnlyCollection<GovernedAgentContextItem> context,
        PanelInstanceId? panelId,
        AgentTarget? actionTarget)
    {
        var item = (panelId, actionTarget) switch
        {
            ({ } id, _) => context.FirstOrDefault(item => item.PanelId == id),
            (_, AgentTarget.Panel panel) =>
                context.FirstOrDefault(item => item.PanelId == panel.PanelId),
            (_, AgentTarget.ConnectionSession session) =>
                context.FirstOrDefault(item => item.SessionId == session.SessionId),
            _ => null,
        };
        return item?.PanelTitle is { Length: > 0 } title ? title : null;
    }

    private static bool IsKnownIdentifier(
        ScopeKind kind,
        string identifier,
        IReadOnlyCollection<GovernedAgentContextItem> context,
        AgentTarget? actionTarget)
    {
        var fromTarget = (kind, actionTarget) switch
        {
            (ScopeKind.Workspace, AgentTarget.Workspace workspace) =>
                workspace.WorkspaceId.Value,
            (ScopeKind.SelectedPanels, AgentTarget.SelectedPanels selected) =>
                selected.Panels[0].WorkspaceId.Value,
            (ScopeKind.Tab, AgentTarget.OpenTab tab) => tab.TabId.Value,
            (ScopeKind.Panel, AgentTarget.Panel panel) => panel.PanelId.Value,
            (ScopeKind.ConnectionSession, AgentTarget.ConnectionSession session) =>
                session.SessionId.Value,
            _ => null,
        };
        if (string.Equals(fromTarget, identifier, StringComparison.Ordinal))
        {
            return true;
        }

        return context.Any(item => kind switch
        {
            ScopeKind.Workspace or ScopeKind.SelectedPanels =>
                Same(item.WorkspaceId.Value, identifier),
            ScopeKind.Tab => Same(item.TabId.Value, identifier),
            ScopeKind.Panel => Same(item.PanelId.Value, identifier),
            _ => Same(item.SessionId.Value, identifier),
        });
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    // Runtime identifiers are GUIDs printed without separators. A title a
    // person chose, such as "Tab 3", never matches.
    private static bool LooksLikeRuntimeIdentifier(string value) =>
        value.Length >= 16
        && (value.All(char.IsAsciiHexDigit)
            || Guid.TryParse(value, CultureInfo.InvariantCulture, out _));
}
