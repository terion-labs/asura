using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed partial class MainWindowRuntimeGraphIntegrationTests
{
    [Fact]
    public async Task External_agent_policy_resolves_exact_targets_without_mixed_tab_provider_gate()
    {
        var provider = CreateAgentProvider();
        using var profiles = new FixedAiProfileRuntime([provider]);
        var coordinator = Assert.IsType<AgentPolicyCoordinator>(CreateConfiguredPolicyCoordinator(profiles));
        var defaults = Assert.IsType<AgentPolicy>(coordinator.Policy);
        var factory = new RecordingAgentWorkspaceRuntimeFactory();
        var (client, _) = CreateSessionClient();
        using var viewModel = CreateViewModel(client, CreateCatalogSnapshot(), aiProfiles: profiles,
            browserRendererFactory: new RecordingBrowserRendererViewFactory(), agentRuntimeFactory: factory,
            agentPolicyCoordinator: coordinator);
        Assert.True(await viewModel.OpenLocalBrowserWorkspaceAsync());
        var workspace = Assert.IsType<RuntimeWorkspaceViewModel>(viewModel.RuntimeWorkspace);
        var port = Assert.IsAssignableFrom<IAgentWorkspaceLayoutMutationPort>(Assert.Single(factory.CreatedRuntimes).LayoutPort);
        var inheritedTab = Assert.Single(workspace.Tabs);
        var inheritedPanel = Assert.Single(inheritedTab.Panels);
        var overridePolicy = defaults with
        {
            Provider = "other-saved-provider",
            Model = "other-saved-model",
            Permissions = defaults.Permissions.SetItem(AgentCapability.BrowserData, AgentPermission.Off),
        };
        var savedTab = new RuntimeTabViewModel(TabInstanceId.New(), "Saved policy tab", "SAVED SCREEN",
            agentPolicy: RuntimeAgentPolicyProvenance.Unconfigured.WithOverride(overridePolicy,
                new DefinitionKey(DefinitionKind.Screen, "external-policy-screen"), revision: 1));
        var savedPanel = new UnavailableRuntimePanelViewModel(PanelInstanceId.New(), PanelKind.Browser,
            "Saved panel", "BROWSER", "Not connected.");
        savedTab.AddPanel(savedPanel);
        workspace.Tabs.Add(savedTab);
        var capturedOverride = Assert.IsType<AgentPolicy>(savedTab.AgentPolicy.EffectivePolicy);
        var broadTarget = new AgentTarget.Workspace(port.WindowId, workspace.Id);
        var exactSaved = new AgentTarget.Panel(port.WindowId, workspace.Id, savedTab.Id, savedPanel.Id);
        var exactInherited = new AgentTarget.Panel(port.WindowId, workspace.Id, inheritedTab.Id, inheritedPanel.Id);

        Assert.Equal(defaults, await port.ResolveExternalPolicyAsync(broadTarget, [], CancellationToken.None));
        Assert.Equal(capturedOverride, await port.ResolveExternalPolicyAsync(exactSaved, [], CancellationToken.None));
        Assert.Equal(defaults, await port.ResolveExternalPolicyAsync(exactInherited, [], CancellationToken.None));
        Assert.Equal(capturedOverride, await port.ResolveExternalPolicyAsync(broadTarget,
            [new AgentApprovalArgument("panel_id", savedPanel.Id.Value)], CancellationToken.None));
        Assert.Null(await port.ResolveExternalPolicyAsync(broadTarget,
            [new AgentApprovalArgument("tab_id", inheritedTab.Id.Value), new AgentApprovalArgument("panel_id", savedPanel.Id.Value)],
            CancellationToken.None));
        Assert.Null(await port.ResolveExternalPolicyAsync(new AgentTarget.Panel(port.WindowId, workspace.Id,
            inheritedTab.Id, savedPanel.Id), [], CancellationToken.None));

        var changedDefaults = defaults with
        {
            Permissions = defaults.Permissions.SetItem(AgentCapability.BrowserData, AgentPermission.Auto),
        };
        Assert.True((await coordinator.SaveAsync(changedDefaults, CancellationToken.None)).IsSuccess);
        Assert.Equal(coordinator.Policy, await port.ResolveExternalPolicyAsync(broadTarget, [], CancellationToken.None));
        Assert.Equal(coordinator.Policy, await port.ResolveExternalPolicyAsync(exactInherited, [], CancellationToken.None));
        Assert.Equal(capturedOverride, await port.ResolveExternalPolicyAsync(exactSaved, [], CancellationToken.None));
        Assert.Single(factory.CreatedRuntimes);
    }
}
