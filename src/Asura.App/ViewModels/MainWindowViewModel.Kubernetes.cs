using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<WorkspaceInstanceId, KubernetesForwardWorkspaceState> _kubernetesForwards = [];

    private KubernetesForwardWorkspaceState? KubernetesForwardsFor(WorkspaceInstanceId? workspaceId, IKubernetesPanelSessionFactory factory)
    {
        if (workspaceId is not { } id) { return null; }
        if (!_kubernetesForwards.TryGetValue(id, out var state))
        {
            var services = WorkspaceRuntimeServicesFor(id);
            state = new(factory, services?.NetworkConnector, services?.Backends.DatabasePanelClient?.Drivers.Where(driver => !driver.IsFileBased).ToArray()); _kubernetesForwards.Add(id, state);
        }
        return state;
    }

    private Task CleanupKubernetesForwardsAsync(WorkspaceInstanceId id) =>
        _kubernetesForwards.Remove(id, out var state) ? state.DisposeAsync().AsTask() : Task.CompletedTask;

    public async Task<bool> OpenKubernetesFilesAsync(KubernetesRuntimePanelViewModel source, CancellationToken cancellationToken)
    {
        if (source.Profile is not { } profile || source.ShellRequest is not { } request) { return false; }
        var workspace = _openWorkspaces.FirstOrDefault(item => item.Tabs.Any(tab => tab.Panels.Contains(source)));
        var tab = workspace?.Tabs.FirstOrDefault(item => item.Panels.Contains(source));
        var factory = workspace is null ? null : WorkspaceRuntimeServicesFor(workspace.Id)?.Backends.KubernetesPanelSessionFactory;
        if (workspace is null || tab is null || factory is null) { return false; }
        var target = new KubernetesFileTarget(profile, request.Pod, request.Container);
        var panel = new FileRuntimePanelViewModel(PanelInstanceId.New(), $"{request.Pod.Name} files",
            new KubernetesFilePanelClient(factory, target), deferInitialization: true, kubernetesTarget: target);
        return await AddRuntimePanelUnderReceiptAsync(workspace, tab, panel, "Kubernetes container files", () =>
        {
            _ = tab.SplitWithPanel(source.Id, PanelSplitOrientation.LeftRight, panel);
            StartTrackingRecovery(panel); _ = tab.ActivatePanel(panel.Id);
        }, cancellationToken, adjacentTo: source.Id);
    }

    public async Task<bool> OpenKubernetesShellAsync(KubernetesRuntimePanelViewModel source, CancellationToken cancellationToken)
    {
        if (source.Profile is not { } profile || source.ShellRequest is not { } request) { return false; }
        var workspace = _openWorkspaces.FirstOrDefault(item => item.Tabs.Any(tab => tab.Panels.Contains(source)));
        var tab = workspace?.Tabs.FirstOrDefault(item => item.Panels.Contains(source));
        if (workspace is null || tab is null) { return false; }
        var id = PanelInstanceId.New();
        var target = new KubernetesTerminalTarget(profile, request);
        var terminalProfile = ActiveTerminalProfile;
        var keymap = terminalProfile is null ? null : ResolveTerminalKeymap(_catalog.Snapshot, terminalProfile.KeymapId);
        var panel = new TerminalRuntimePanelViewModel(id, request.Pod.Name,
            new KubernetesTerminalConnectionRuntime(target), BuiltInConnections.Local,
            new(HostMode.Desktop, WindowId, workspace.Id, tab.Id, id), PanelStartupBehavior.None,
            terminalProfile is null ? null : TerminalRenderProfileSnapshot.FromProfile(terminalProfile),
            SessionClient, ClientId, _startupCommandDispatcher,
            keymap: keymap is null ? null : TerminalKeymapSnapshot.FromProfile(keymap),
            connectionDisplayName: $"{profile.ContextName} · {request.Pod.Namespace}/{request.Pod.Name} · {request.Container ?? "default container"}",
            kubernetesTarget: target);
        return await AddRuntimePanelUnderReceiptAsync(workspace, tab, panel, "Kubernetes pod terminal", () =>
        {
            _ = tab.SplitWithPanel(source.Id, PanelSplitOrientation.LeftRight, panel);
            StartTrackingRecovery(panel); _ = tab.ActivatePanel(panel.Id);
        }, cancellationToken, adjacentTo: source.Id);
    }

    public async Task<bool> OpenKubernetesForwardDatabaseAsync(KubernetesForwardViewModel forward, CancellationToken cancellationToken)
    {
        if (!forward.CanOpenDatabase || forward.DatabaseDriver is not { } driver || forward.EndpointHost is not { } host) { return false; }
        var state = _kubernetesForwards.FirstOrDefault(item => item.Value.Forwards.Contains(forward));
        var workspace = _openWorkspaces.FirstOrDefault(item => item.Id == state.Key);
        var tab = workspace?.ActiveTab;
        var client = workspace is null ? null : WorkspaceRuntimeServicesFor(workspace.Id)?.Backends.DatabasePanelClient;
        if (workspace is null || tab is null || client is null) { return false; }
        string connectionString;
        try
        {
            connectionString = client.BuildConnectionString(driver.Id, new(Host: host, Port: forward.EndpointPort,
            Database: forward.DatabaseName, Username: forward.DatabaseUsername));
        }
        catch (ArgumentException) { SetError("Review the database name and username for this driver."); return false; }
        var panel = new DatabaseRuntimePanelViewModel(PanelInstanceId.New(), "Forwarded database", client,
            driver.Id, connectionString, sessionPassword: forward.DatabasePassword, sqlLanguageService: _sqlLanguageService,
            persistedConnection: false, transientConnection: true);
        forward.DatabasePassword = string.Empty;
        return await AddRuntimePanelUnderReceiptAsync(workspace, tab, panel, "Kubernetes forward database", () =>
        {
            tab.AddPanel(panel); StartTrackingRecovery(panel); _ = tab.ActivatePanel(panel.Id);
        }, cancellationToken);
    }

    public async Task<bool> OpenKubernetesForwardBrowserAsync(KubernetesForwardViewModel forward, CancellationToken cancellationToken)
    {
        if (!forward.CanOpenBrowser || !BrowserAddress.TryParse(forward.BrowserAddress, out var address)) { return false; }
        var state = _kubernetesForwards.FirstOrDefault(item => item.Value.Forwards.Contains(forward));
        var workspace = _openWorkspaces.FirstOrDefault(item => item.Id == state.Key);
        var tab = workspace?.ActiveTab;
        if (workspace is null || tab is null) { return false; }
        var panel = CreateBrowserPanel(workspace.Id, tab.Id, PanelInstanceId.New(), "Pod forward", address);
        return await AddRuntimePanelUnderReceiptAsync(workspace, tab, panel, "Kubernetes forward browser", () =>
        {
            tab.AddPanel(panel); StartTrackingRecovery(panel); _ = tab.ActivatePanel(panel.Id);
        }, cancellationToken);
    }

    private ValueTask<KubernetesConfigurationReview> ReviewKubernetesConfigurationAsync(KubernetesConnectionProfile profile, CancellationToken token)
    {
        var factory = RuntimeWorkspace is { } workspace
            ? WorkspaceRuntimeServicesFor(workspace.Id)?.Backends.KubernetesPanelSessionFactory
            : _hostWorkspaceRuntimeServices.Backends.KubernetesPanelSessionFactory;
        return factory is null
            ? ValueTask.FromException<KubernetesConfigurationReview>(new NotSupportedException("Kubernetes is unavailable in this workspace."))
            : factory.ReviewAsync(profile, token);
    }

    public IEnumerable<PanelConnectionOptionViewModel> KubernetesPanelConnectionOptions =>
        _catalog.Snapshot.KubernetesConnections.Select(item => new PanelConnectionOptionViewModel(
            new PanelConnectionOptionViewModel.Target.Kubernetes(item.Value.Id), item.Value.Name,
            "Kubernetes", item.Value.ContextName, item.Value.IsEnabled));

    public KubernetesConnectionProfile? FindKubernetesConnection(KubernetesConnectionProfileId id) =>
        _catalog.Snapshot.KubernetesConnections.FirstOrDefault(item => item.Value.Id == id)?.Value;

    private RuntimePanelViewModel CreateKubernetesPanel(
        PanelInstanceId id, string title, KubernetesPanelTarget? target = null, WorkspaceInstanceId? workspaceId = null)
    {
        var factory = workspaceId is { } workspace
            ? WorkspaceRuntimeServicesFor(workspace)?.Backends.KubernetesPanelSessionFactory
            : _hostWorkspaceRuntimeServices.Backends.KubernetesPanelSessionFactory;
        var profile = target is null ? null : FindKubernetesConnection(target.ProfileId);
        if (target is not null && (profile is null || !profile.IsEnabled))
        {
            return new UnavailableRuntimePanelViewModel(id, PanelKind.Kubernetes, title, "Kubernetes",
                "The saved Kubernetes connection is no longer available.");
        }
        if (factory is null)
        {
            return new UnavailableRuntimePanelViewModel(id, PanelKind.Kubernetes, title, "Kubernetes",
                "Kubernetes is unavailable in this workspace execution environment.");
        }
        return new KubernetesRuntimePanelViewModel(id, title, profile,
            profile is null ? null : token => factory.OpenAsync(profile, token), target,
            profile is null ? 0 : _catalog.Snapshot.KubernetesConnections.Single(item => item.Value.Id == profile.Id).Revision)
        { ForwardState = KubernetesForwardsFor(workspaceId, factory) };
    }

    public async Task<KubernetesConnectionProfile?> SaveKubernetesConnectionAsync(
        KubernetesConnectionProfile profile, KubernetesConnectionProfileId? existingId, CancellationToken cancellationToken)
    {
        var existing = existingId is { } id
            ? _catalog.Snapshot.KubernetesConnections.FirstOrDefault(item => item.Value.Id == id) : null;
        if (existingId is not null && existing is null)
        {
            SetError("That Kubernetes connection no longer exists.");
            return null;
        }
        var result = await _catalog.SaveKubernetesConnectionAsync(profile, existing?.Revision, cancellationToken);
        if (!result.IsSuccess) { ApplyError(result.Error); return null; }
        OnPropertyChanged(nameof(KubernetesPanelConnectionOptions));
        return result.Value!.Value;
    }

    public bool ReplaceKubernetesPanelConnection(RuntimePanelViewModel panel, KubernetesConnectionProfileId id)
    {
        var profile = FindKubernetesConnection(id);
        var workspace = RuntimeWorkspace;
        var tab = workspace?.Tabs.FirstOrDefault(item => item.Panels.Contains(panel));
        if (profile is null || workspace is null || tab is null) { SetError("The Kubernetes panel or connection is no longer available."); return false; }
        var replacement = CreateKubernetesPanel(panel.Id, panel.Title, new(id, profile.DefaultNamespace), workspace.Id);
        if (!tab.ReplacePanel(panel, replacement)) { replacement.Dispose(); return false; }
        Notifications.Watch(workspace);
        StartTrackingRecovery(replacement);
        StartAcceptedRuntimePanel(replacement);
        QueueRuntimeRecoverySnapshot();
        return true;
    }

    public Task<bool> AddKubernetesTabAsync(CancellationToken cancellationToken = default) =>
        AddSinglePanelTabAsync(PanelKind.Kubernetes, null, cancellationToken);

    public async Task<bool> AddKubernetesPanelAsync(CancellationToken cancellationToken = default, KubernetesConnectionProfileId? profileId = null)
    {
        var workspace = RuntimeWorkspace;
        var tab = workspace?.ActiveTab;
        if (workspace is null || tab is null) { SetError("Open a workspace tab before adding a Kubernetes panel."); return false; }
        var profile = profileId is { } id ? FindKubernetesConnection(id) : null;
        if (profileId is not null && profile is null) { SetError("That Kubernetes connection no longer exists."); return false; }
        var panel = CreateKubernetesPanel(PanelInstanceId.New(), "Kubernetes",
            profile is null ? null : new(profile.Id, profile.DefaultNamespace), workspace.Id);
        return await AddRuntimePanelUnderReceiptAsync(workspace, tab, panel, "Kubernetes panel creation", () =>
        {
            tab.AddPanel(panel); StartTrackingRecovery(panel); _ = tab.ActivatePanel(panel.Id);
        }, cancellationToken);
    }

    public async Task<bool> LaunchSavedKubernetesAsync(KubernetesConnectionProfileId id, CancellationToken cancellationToken = default)
    {
        var profile = FindKubernetesConnection(id);
        if (profile is null) { SetError("That Kubernetes connection no longer exists."); return false; }
        if (RuntimeWorkspace is { } workspace)
        {
            return await AppendRuntimeTabAsync(workspace, runtime => CreateSavedKubernetesTab(profile, runtime.Id),
                "Kubernetes connection tab creation", cancellationToken);
        }
        var runtimeWorkspace = new RuntimeWorkspaceViewModel(WorkspaceInstanceId.New(), profile.Name,
            ThemePreference.BronzeFallback.ToString(), [], CurrentAgentPolicyProvenance());
        try
        {
            RegisterWorkspaceConnectionRuntime(runtimeWorkspace);
            var tab = CreateSavedKubernetesTab(profile, runtimeWorkspace.Id);
            runtimeWorkspace.Tabs.Add(tab);
            runtimeWorkspace.ActiveTab = tab;
            if (!await RegisterRuntimeWorkspaceAsync(runtimeWorkspace, cancellationToken)) { return false; }
            ActivateRuntimeWorkspace(runtimeWorkspace, null, runtimeWorkspace.Name);
            Route = ShellRoute.Workspace;
            QueueRuntimeRecoverySnapshot();
            return true;
        }
        finally { DisposeRuntimeWorkspaceUnlessOwned(runtimeWorkspace); }
    }

    private RuntimeTabViewModel CreateSavedKubernetesTab(KubernetesConnectionProfile profile, WorkspaceInstanceId workspaceId)
    {
        var tab = new RuntimeTabViewModel(TabInstanceId.New(), profile.Name, "Kubernetes");
        AddPanelOrDispose(tab, CreateKubernetesPanel(PanelInstanceId.New(), profile.Name,
            new(profile.Id, profile.DefaultNamespace), workspaceId));
        return tab;
    }
}
