using Asura.Application;
using Asura.Core;

namespace Asura.SessionHost;

public sealed partial class InMemorySessionHostClient
{
    private HostResult<AgentContextSnapshot>? AddWorkspacePanels(
        AgentTarget.Workspace target,
        IReadOnlyDictionary<SessionId, SessionDescriptor> sessions,
        IDictionary<WorkspaceInstanceId, WorkspaceGraphSnapshot> graphs,
        ICollection<AgentContextPanel> panels,
        int maximumPanelCount)
    {
        if (!TryGetAgentGraph(
                target.WindowId,
                target.WorkspaceId,
                graphs,
                out var graph,
                out var failure))
        {
            return failure;
        }

        foreach (var tab in graph.Workspace.Tabs)
        {
            foreach (var panel in tab.Panels)
            {
                failure = AddContextPanel(
                    graph,
                    tab.Id,
                    panel,
                    sessions,
                    panels,
                    maximumPanelCount);
                if (failure is not null)
                {
                    return failure;
                }
            }
        }

        return null;
    }

    private HostResult<AgentContextSnapshot>? AddTabPanels(
        AgentTarget.OpenTab target,
        IReadOnlyDictionary<SessionId, SessionDescriptor> sessions,
        IDictionary<WorkspaceInstanceId, WorkspaceGraphSnapshot> graphs,
        ICollection<AgentContextPanel> panels,
        int maximumPanelCount)
    {
        if (!TryGetAgentGraph(
                target.WindowId,
                target.WorkspaceId,
                graphs,
                out var graph,
                out var failure))
        {
            return failure;
        }

        var tab = graph.Workspace.Tabs.SingleOrDefault(candidate => candidate.Id == target.TabId);
        if (tab is null)
        {
            return NotFound<AgentContextSnapshot>("tab", graph.Revision);
        }

        foreach (var panel in tab.Panels)
        {
            failure = AddContextPanel(
                graph,
                tab.Id,
                panel,
                sessions,
                panels,
                maximumPanelCount);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private HostResult<AgentContextSnapshot>? AddSelectedPanels(
        AgentTarget.SelectedPanels target,
        IReadOnlyDictionary<SessionId, SessionDescriptor> sessions,
        IDictionary<WorkspaceInstanceId, WorkspaceGraphSnapshot> graphs,
        ICollection<AgentContextPanel> panels,
        int maximumPanelCount)
    {
        foreach (var selected in target.Panels)
        {
            var failure = AddExactPanel(
                selected,
                sessions,
                graphs,
                panels,
                maximumPanelCount);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private HostResult<AgentContextSnapshot>? AddExactPanel(
        AgentTarget.Panel target,
        IReadOnlyDictionary<SessionId, SessionDescriptor> sessions,
        IDictionary<WorkspaceInstanceId, WorkspaceGraphSnapshot> graphs,
        ICollection<AgentContextPanel> panels,
        int maximumPanelCount)
    {
        if (!TryGetAgentGraph(
                target.WindowId,
                target.WorkspaceId,
                graphs,
                out var graph,
                out var failure))
        {
            return failure;
        }

        var tab = graph.Workspace.Tabs.SingleOrDefault(candidate => candidate.Id == target.TabId);
        if (tab is null)
        {
            return NotFound<AgentContextSnapshot>("tab", graph.Revision);
        }

        var panel = tab.Panels.SingleOrDefault(candidate => candidate.Id == target.PanelId);
        return panel is null
            ? NotFound<AgentContextSnapshot>("panel", graph.Revision)
            : AddContextPanel(
                graph,
                tab.Id,
                panel,
                sessions,
                panels,
                maximumPanelCount);
    }

    private static HostResult<AgentContextSnapshot>? AddContextPanel(
        WorkspaceGraphSnapshot graph,
        TabInstanceId tabId,
        PanelInstance panel,
        IReadOnlyDictionary<SessionId, SessionDescriptor> sessions,
        ICollection<AgentContextPanel> panels,
        int maximumPanelCount)
    {
        if (panels.Count >= maximumPanelCount)
        {
            return InvalidAgentContext(
                "The target resolves to more panels than the request permits.",
                graph.Revision);
        }

        // A panel whose session ended (a shell that exited, a dropped
        // connection) keeps its link until the panel restarts or closes. It
        // is listed without a session, so it can't be acted on but doesn't
        // take the rest of its tab or workspace down with it.
        SessionDescriptor? session = null;
        if (panel.SessionId is { } sessionId
            && sessions.TryGetValue(sessionId, out var linked)
            && linked.Lifecycle is not (SessionLifecycle.Closed or SessionLifecycle.Failed))
        {
            session = linked;
        }

        try
        {
            panels.Add(session is null && panel.SessionId is not null
                ? AgentContextPanel.ForGraphPanelWhoseSessionEnded(
                    graph,
                    tabId,
                    panel.Id)
                : AgentContextPanel.ForGraphPanel(
                    graph,
                    tabId,
                    panel.Id,
                    session));
            return null;
        }
        catch (ArgumentException)
        {
            return InvalidAgentContext(
                "A live session does not exactly own its linked workspace panel.",
                graph.Revision);
        }
    }
}
