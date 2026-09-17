using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class MainWindow
{
    private async void OnKubernetesFilesRequested(object? sender, KubernetesRuntimePanelViewModel source) =>
        _ = await ViewModel.OpenKubernetesFilesAsync(source, _lifetime.Token);

    private async void OnKubernetesShellRequested(object? sender, KubernetesRuntimePanelViewModel source) =>
        _ = await ViewModel.OpenKubernetesShellAsync(source, _lifetime.Token);

    private async void OnKubernetesForwardDatabaseRequested(object? sender, KubernetesForwardViewModel forward) =>
        _ = await ViewModel.OpenKubernetesForwardDatabaseAsync(forward, _lifetime.Token);

    private async void OnKubernetesForwardBrowserRequested(object? sender, KubernetesForwardViewModel forward) =>
        _ = await ViewModel.OpenKubernetesForwardBrowserAsync(forward, _lifetime.Token);

    private async Task CreateAndBindKubernetesConnectionAsync(RuntimePanelViewModel panel)
    {
        if (!await ConfirmDiscardDatabaseChangesAsync([panel])) { return; }
        var editor = ViewModel.CreateUnifiedConnectionEditor(SavedConnectionFamily.Kubernetes,
            initialFamily: SavedConnectionFamily.Kubernetes);
        var result = await new ConnectionEditorDialog(editor).ShowDialog<UnifiedConnectionEditorResult?>(this);
        if (result is not UnifiedConnectionEditorResult.Kubernetes kubernetes) { return; }
        var saved = await ViewModel.SaveKubernetesConnectionAsync(kubernetes.Profile, kubernetes.ExistingId, _lifetime.Token);
        if (saved is not null) { _ = ViewModel.ReplaceKubernetesPanelConnection(panel, saved.Id); }
    }

    private async void OnAddKubernetesPanelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CloseOverlay();
        _ = await ViewModel.AddKubernetesPanelAsync(_lifetime.Token);
    }

    private async void OnPlaceholderKubernetesClick(object? sender, RoutedEventArgs e) =>
        await ChoosePlaceholderAsync(sender, () => ViewModel.AddKubernetesPanelAsync(_lifetime.Token));

    private async void OnNewKubernetesClick(object? sender, RoutedEventArgs e) =>
        await RequestNewAdapterTabAsync(PanelKind.Kubernetes);
    private async Task RequestNewKubernetesAsync()
    {
        ViewModel.CloseOverlay();
        var editor = ViewModel.CreateUnifiedConnectionEditor(SavedConnectionFamily.Kubernetes,
            initialFamily: SavedConnectionFamily.Kubernetes);
        var result = await new ConnectionEditorDialog(editor).ShowDialog<UnifiedConnectionEditorResult?>(this);
        if (result is not UnifiedConnectionEditorResult.Kubernetes kubernetes) { return; }
        var saved = await ViewModel.SaveKubernetesConnectionAsync(kubernetes.Profile, kubernetes.ExistingId, _lifetime.Token);
        if (saved is not null) { await LaunchTargetAsync(token => ViewModel.LaunchSavedKubernetesAsync(saved.Id, token)); }
    }
}
