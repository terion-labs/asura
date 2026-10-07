using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>
/// Reopen Closed Tab. A closed tab is remembered as its definition: title,
/// icon, layout and what each panel was showing. Reopening starts fresh
/// sessions from that definition; the processes that ran in the tab ended
/// when it closed and are not brought back.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const int MaximumClosedTabs = 20;

    private readonly List<ClosedTab> _closedTabs = [];

    /// <summary>
    /// Whether the active workspace has a closed tab to reopen.
    /// </summary>
    public bool CanReopenClosedTab => RuntimeWorkspace is { } workspace
        && _closedTabs.Exists(closed => closed.WorkspaceId == workspace.Id);

    /// <summary>
    /// Reopens the most recently closed tab of the active workspace. Tabs are
    /// reopened where they were closed: their connections belong to that
    /// workspace's runtime.
    /// </summary>
    public async Task<bool> ReopenClosedTabAsync(CancellationToken cancellationToken = default)
    {
        ClearError();
        var workspace = RuntimeWorkspace;
        var index = workspace is null
            ? -1
            : _closedTabs.FindLastIndex(closed => closed.WorkspaceId == workspace.Id);
        if (workspace is null || index < 0)
        {
            SetError("There is no closed tab to reopen in this workspace.");
            return false;
        }

        var closed = _closedTabs[index];
        _closedTabs.RemoveAt(index);

        // Closing a workspace's last tab left the launcher in its place; the
        // reopened tab takes that place back.
        var launcher = workspace.Tabs is [var only] && IsLauncherTab(only) ? only : null;
        var reopened = launcher is null
            ? await AppendRuntimeTabAsync(
                workspace,
                target => RestoreTab(target.Id, closed.Definition),
                "closed tab reopen",
                cancellationToken)
            : await ReplaceRuntimeTabAsync(
                workspace,
                launcher,
                target => RestoreTab(target.Id, closed.Definition),
                "closed tab reopen",
                cancellationToken);
        if (!reopened)
        {
            _closedTabs.Insert(Math.Min(index, _closedTabs.Count), closed);
        }

        OnPropertyChanged(nameof(CanReopenClosedTab));
        return reopened;
    }

    private async Task<bool> RemoveTabUnderGateAsync(
        RuntimeWorkspaceViewModel workspace,
        RuntimeTabViewModel tab,
        CancellationToken cancellationToken,
        bool retryAfterGraphChange = true)
    {
        // Captured before removal disposes the panels it describes.
        var definition = IsLauncherTab(tab)
            ? null
            : CaptureClosedTabDefinition(tab);
        var removed = await RemoveTabUnderGateCoreAsync(
            workspace,
            tab,
            cancellationToken,
            retryAfterGraphChange);
        if (removed && definition is not null)
        {
            RememberClosedTab(new ClosedTab(workspace.Id, definition));
        }

        return removed;
    }

    private void RememberClosedTab(ClosedTab closed)
    {
        _closedTabs.Add(closed);
        if (_closedTabs.Count > MaximumClosedTabs)
        {
            _closedTabs.RemoveAt(0);
        }

        OnPropertyChanged(nameof(CanReopenClosedTab));
    }

    /// <summary>
    /// The tab's definition without the identity of its live sessions. A
    /// multiplexer session belongs to the tab that closed; reattaching to it
    /// would bring back a process rather than a definition.
    /// </summary>
    private static RuntimeTabRecoveryPayload CaptureClosedTabDefinition(RuntimeTabViewModel tab)
    {
        var captured = RuntimeWorkspaceRecoveryCodec.CaptureTab(tab);
        return captured with
        {
            Panels = [.. captured.Panels.Select(panel => panel with { Multiplexer = null })],
        };
    }

    private sealed record ClosedTab(
        WorkspaceInstanceId WorkspaceId,
        RuntimeTabRecoveryPayload Definition);
}
