using System.Text.Json;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class KubernetesManifestFormattingTests
{
    [Fact]
    public async Task CurrentManifestAndInitialEditorAreIndentedWithoutChangingRawResource()
    {
        const string compact = """{"apiVersion":"v1","kind":"Pod","metadata":{"name":"app-123","annotations":{"message":"line one\nline two"}},"spec":{"containers":[{"name":"app"}]}}""";
        using var panel = Create(new KubernetesUiSession { ListedResource = KubernetesUiSession.Pod with { Json = compact }, AllowPatching = true });
        await SelectFirstAsync(panel);
        string formatted = panel.FormattedManifest;
        Assert.Equal(compact, panel.Manifest);
        Assert.Contains("\n  \"apiVersion\": \"v1\"", formatted, StringComparison.Ordinal);
        Assert.Same(formatted, panel.FormattedManifest);
        Assert.Equal(formatted, panel.ManifestDraft);
        using var json = JsonDocument.Parse(formatted);
        Assert.Equal("line one\nline two", json.RootElement.GetProperty("metadata").GetProperty("annotations").GetProperty("message").GetString());
        panel.ManifestDraft = formatted;
        Assert.False(panel.HasUnsavedChanges);
        Assert.Equal(".json", panel.ManifestGrammarExtension);
    }

    [Fact]
    public async Task ExactUserJsonAndReviewedPayloadRemainUnchangedWhilePreviewIsFormatted()
    {
        var session = new KubernetesUiSession { AllowPatching = true };
        using var panel = Create(session);
        await SelectFirstAsync(panel);
        const string draft = "  {\"kind\":\"Pod\",\"spec\":{\"replicas\":2}}  ";
        panel.ManifestDraft = draft;
        await panel.DryRunAsync();
        Assert.Equal(draft, panel.ManifestDraft);
        Assert.Equal(draft, Assert.Single(session.Mutations).Json);
        Assert.Contains("\n  \"kind\": \"Pod\"", panel.PreviewManifest, StringComparison.Ordinal);
        Assert.True(panel.CanApplyManifest);
        panel.ManifestDraft = draft + "\n";
        Assert.False(panel.CanApplyManifest);
        await panel.ApplyManifestAsync();
        Assert.Single(session.Mutations);
        await panel.DryRunAsync();
        await panel.ApplyManifestAsync();
        Assert.Equal(draft + "\n", session.Mutations[^1].Json);
        Assert.False(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task YamlTypingRemainsExactAndGrammarChangesWithoutAffectingReviewedJson()
    {
        const string converted = """{"apiVersion":"v1","kind":"Pod","spec":{"activeDeadlineSeconds":90}}""";
        string? conversionInput = null;
        var session = new KubernetesUiSession
        {
            AllowPatching = true,
            ConvertManifest = text => { conversionInput = text; return converted; },
        };
        using var panel = Create(session);
        await SelectFirstAsync(panel);
        var notifications = new List<string?>();
        panel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        const string yaml = "# Keep this comment\napiVersion: v1\nkind: Pod\nspec:\n  activeDeadlineSeconds: 90\n";
        panel.ManifestDraft = yaml;
        Assert.Equal(".yaml", panel.ManifestGrammarExtension);
        Assert.Contains(nameof(panel.ManifestGrammarExtension), notifications, StringComparer.Ordinal);
        await panel.DryRunAsync();
        Assert.Equal(yaml, conversionInput);
        Assert.Equal(yaml, panel.ManifestDraft);
        Assert.Equal(converted, Assert.Single(session.Mutations).Json);
        Assert.Contains("\n  \"spec\": {", panel.PreviewManifest, StringComparison.Ordinal);
        await panel.ApplyManifestAsync();
        Assert.Equal(converted, session.Mutations[^1].Json);
        Assert.Equal(".json", panel.ManifestGrammarExtension);
        Assert.False(panel.HasUnsavedChanges);
    }

    [Fact]
    public async Task IncompleteJsonTypingIsNeverReformatted()
    {
        using var panel = Create(new KubernetesUiSession { AllowPatching = true });
        await SelectFirstAsync(panel);
        const string incomplete = "{\n    \"spec\": ";
        panel.ManifestDraft = incomplete;
        Assert.Equal(incomplete, panel.ManifestDraft);
        Assert.Equal(".json", panel.ManifestGrammarExtension);
        Assert.True(panel.HasUnsavedChanges);
        panel.DiscardManifestChanges();
        Assert.Equal(panel.FormattedManifest, panel.ManifestDraft);
        Assert.False(panel.HasUnsavedChanges);
    }

    private static async Task SelectFirstAsync(KubernetesRuntimePanelViewModel panel)
    {
        await panel.Initialization;
        panel.SelectedResource = panel.Resources[0];
        await panel.SelectionLoading;
    }

    private static KubernetesRuntimePanelViewModel Create(KubernetesUiSession session) => new(PanelInstanceId.New(), "Kubernetes",
        new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"), _ => ValueTask.FromResult<IKubernetesClientSession>(session));
}
