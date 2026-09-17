using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesSavedTargetTests
{
    [Fact]
    public void SavedScreenEditorPreservesCustomResourceViewAndRepairsProfile()
    {
        var target = new KubernetesPanelTarget(new("missing"), "team", "widgets", "example.test", "v2");
        var panel = new ScreenPanelDefinition(new("panel"), new("slot"), ScreenPanelKind.Kubernetes,
            "Widgets", null, PanelStartupBehavior.None, KubernetesTarget: target);
        var choices = ScreenKubernetesOption.Build(
            [new(new("replacement"), 1, "Replacement", "/tmp/config", "context")], [panel]);
        var editor = new SavedScreenPanelEditorViewModel(panel, [], [], choices);
        Assert.True(editor.HasMissingKubernetes);
        Assert.Equal(target, editor.Build().KubernetesTarget);
        editor.SelectedKubernetes = choices.Single(option => option.IsAvailable);
        editor.KubernetesNamespace = "production";
        var repaired = editor.Build();
        Assert.False(editor.HasMissingDefinition);
        Assert.Equal(target with { ProfileId = new("replacement"), NamespaceName = "production" }, repaired.KubernetesTarget);
        Assert.Null(repaired.ConnectionId);
        Assert.Null(repaired.Startup.Location);
    }

    [Fact]
    public void ChangingPanelKindDropsKubernetesAuthority()
    {
        var panel = new ScreenPanelDefinition(new("panel"), new("slot"), ScreenPanelKind.Kubernetes,
            "Cluster", null, PanelStartupBehavior.None, KubernetesTarget: new(new("cluster")));
        var editor = new SavedScreenPanelEditorViewModel(panel, [], [], [new(new("cluster"), "Cluster", true)])
        {
            Kind = ScreenPanelKind.Statistics,
        };
        Assert.Null(editor.Build().KubernetesTarget);
    }

    [Fact]
    public void RuntimeRecoveryContainsOnlyViewIntent()
    {
        var profile = new KubernetesConnectionProfile(new("cluster"), 1, "Cluster", "/private/config", "private-context",
            trustedExecFingerprint: new string('a', 64));
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Cluster", profile, null,
            new(profile.Id, "team"));
        var workspace = new RuntimeWorkspaceViewModel(WorkspaceInstanceId.New(), "Workspace", "#123456", []);
        var tab = new RuntimeTabViewModel(TabInstanceId.New(), "Tab", "test");
        tab.AddPanel(panel);
        workspace.Tabs.Add(tab);
        workspace.ActiveTab = tab;
        var json = RuntimeWorkspaceRecoveryCodec.Serialize(workspace);
        Assert.DoesNotContain("/private/config", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-context", json, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), json, StringComparison.Ordinal);
        Assert.Contains("KubernetesTarget", json, StringComparison.OrdinalIgnoreCase);
    }
}
