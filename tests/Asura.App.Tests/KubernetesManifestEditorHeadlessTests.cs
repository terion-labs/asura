using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class KubernetesManifestEditorHeadlessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManifestEditorShowsIndentedCodeAndRespectsEditAuthority(bool allowEditing)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.True(await session.Dispatch(async () =>
        {
            var client = new KubernetesUiSession { AllowPatching = allowEditing };
            using var model = new KubernetesRuntimePanelViewModel(PanelInstanceId.New(), "Kubernetes",
                new(KubernetesConnectionProfileId.New(), 1, "Cluster", "/config", "context"),
                _ => ValueTask.FromResult<IKubernetesClientSession>(client));
            await model.Initialization;
            model.SelectedResource = model.Resources[0];
            await model.SelectionLoading;
            var view = new KubernetesRuntimePanelView { DataContext = model };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            try
            {
                window.Show();
                view.FindControl<TabControl>("InspectorTabs")!.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                var code = view.FindControl<CodeEditBox>("ManifestEditor")!;
                var editor = code.EditorForTesting;
                Assert.Equal("{\n  \"kind\": \"Pod\"\n}", editor.Text.ReplaceLineEndings("\n"));
                Assert.Equal(".json", code.GrammarExtension);
                Assert.Equal(!allowEditing, editor.IsReadOnly);
                Assert.True(editor.ShowLineNumbers);
                Assert.False(editor.WordWrap);
                Assert.Equal(ScrollBarVisibility.Auto, editor.HorizontalScrollBarVisibility);
                Assert.False(model.HasUnsavedChanges);

                code.FocusEditor(caretToEnd: true);
                window.KeyTextInput(" ");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(allowEditing, model.HasUnsavedChanges);
                Assert.Equal(allowEditing ? model.FormattedManifest + " " : model.FormattedManifest, model.ManifestDraft);
                Assert.Equal(model.ManifestDraft, editor.Text);
                Assert.Empty(client.Mutations);
            }
            finally { window.Close(); }
            return true;
        }, timeout.Token));
    }
}
