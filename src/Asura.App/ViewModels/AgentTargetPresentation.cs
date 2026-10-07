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

    private static string ScopeTitle(
        ScopeKind kind,
        string identifier,
        IReadOnlyCollection<GovernedAgentContextItem> context) =>
        kind switch
        {
            ScopeKind.Workspace => WorkspaceTitle(context, identifier) ?? "this workspace",
            ScopeKind.SelectedPanels => WorkspaceTitle(context, identifier) is { } workspace
                ? $"selected panels in {workspace}"
                : "selected panels",
            ScopeKind.Tab => context
                .FirstOrDefault(item => Same(item.TabId.Value, identifier))?.TabTitle
                    is { Length: > 0 } tab
                ? tab
                : "this tab",
            ScopeKind.Panel => context
                .FirstOrDefault(item => Same(item.PanelId.Value, identifier))?.PanelTitle
                    is { Length: > 0 } panel
                ? panel
                : "this panel",
            _ => context
                .FirstOrDefault(item => Same(item.SessionId.Value, identifier))?.PanelTitle
                    is { Length: > 0 } session
                ? session
                : "this connection",
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
