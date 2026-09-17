using System.ComponentModel;
using System.Threading.Channels;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesLiveSelectionTests
{
    [Fact]
    public async Task ReturningToFormattedBaselineLetsCleanDraftFollowWatchUpdates()
    {
        var watches = Channel.CreateUnbounded<KubernetesWatchEvent>();
        var client = new KubernetesUiSession { AllowPatching = true, ExtraFeatures = KubernetesSessionFeatures.Watch, WatchChanges = watches };
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
            new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
            _ => ValueTask.FromResult<IKubernetesClientSession>(client));
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        string baseline = panel.ManifestDraft;
        panel.ManifestDraft = baseline + "\n";
        panel.ManifestDraft = baseline;
        Assert.False(panel.HasUnsavedChanges);
        var updated = KubernetesUiSession.Pod with
        {
            Reference = KubernetesUiSession.Pod.Reference with { ResourceVersion = "11" },
            Json = """{"kind":"Pod","status":{"phase":"Running"}}""",
        };
        await ObserveAsync(panel, () => panel.Manifest == updated.Json,
            () => watches.Writer.TryWrite(new(KubernetesWatchEventKind.Modified, "11", updated)));
        Assert.Equal(panel.FormattedManifest, panel.ManifestDraft);
        Assert.Contains("\n  \"status\": {", panel.ManifestDraft, StringComparison.Ordinal);
        Assert.False(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task WatchVersionChangeKeepsLogsAndDraftButInvalidatesReviewedWrite()
    {
        var watches = Channel.CreateUnbounded<KubernetesWatchEvent>();
        var logs = Channel.CreateUnbounded<string>();
        var client = new KubernetesUiSession
        {
            AllowPatching = true,
            ExtraFeatures = KubernetesSessionFeatures.Watch | KubernetesSessionFeatures.FollowLogs,
            WatchChanges = watches,
            LogChanges = logs,
        };
        using var panel = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
            new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
            _ => ValueTask.FromResult<IKubernetesClientSession>(client));
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
        panel.ManifestDraft += "\n";
        var draft = panel.ManifestDraft;
        await panel.DryRunAsync();
        Assert.True(panel.CanApplyManifest);
        var following = panel.FollowLogsAsync();
        await ObserveAsync(panel, () => panel.Logs == "before\n", () => logs.Writer.TryWrite("before\n"));
        var updated = KubernetesUiSession.Pod with
        {
            Reference = KubernetesUiSession.Pod.Reference with { ResourceVersion = "11" },
            Json = "{\"kind\":\"Pod\",\"resourceVersion\":\"11\"}",
        };
        await ObserveAsync(panel, () => panel.SelectedResource?.Reference.ResourceVersion == "11",
            () => watches.Writer.TryWrite(new(KubernetesWatchEventKind.Modified, "11", updated)));
        Assert.True(panel.FollowingLogs);
        Assert.Equal("before\n", panel.Logs);
        Assert.Equal(draft, panel.ManifestDraft);
        Assert.False(panel.CanApplyManifest);
        Assert.Equal(KubernetesUiSession.Pod.Json, panel.Manifest);
        await panel.DryRunAsync();
        Assert.Equal("10", client.Mutations[^1].Resource.ResourceVersion);
        Assert.False(panel.CanApplyManifest);
        Assert.Equal(draft, panel.ManifestDraft);
        panel.DiscardManifestChanges();
        Assert.Equal(updated.Json, panel.Manifest);
        Assert.Equal(panel.FormattedManifest, panel.ManifestDraft);
        Assert.Contains("\n", panel.ManifestDraft, StringComparison.Ordinal);
        Assert.False(panel.HasUnsavedChanges);
        panel.ManifestDraft += "\n";
        await panel.DryRunAsync();
        Assert.Equal("11", client.Mutations[^1].Resource.ResourceVersion);
        await ObserveAsync(panel, () => panel.Logs == "before\nafter\n", () => logs.Writer.TryWrite("after\n"));
        panel.StopFollowingLogs();
        await following;
    }

    private static async Task ObserveAsync(KubernetesRuntimePanelViewModel panel, Func<bool> condition, Action trigger)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PropertyChangedEventArgs args) { if (condition()) { completion.TrySetResult(); } }
        panel.PropertyChanged += Changed;
        try { trigger(); if (condition()) { completion.TrySetResult(); } await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { panel.PropertyChanged -= Changed; }
    }
}
