using Asura.App.ViewModels;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    /// <summary>
    /// Git Settings…: the choices every Git panel shares are on the Git page
    /// of Settings, not in a form of this panel's own.
    /// </summary>
    private void OnGitSettings(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        (OwnerWindow as MainWindow)?.NavigateToSettings(SettingsPage.Git);
    }

    private async void OnCommitSpelling(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && OwnerWindow is { } window)
        {
            await new GitOutputDialog("Commit Spelling", await viewModel.CheckCommitSpellingAsync()).ShowDialog(window);
        }
    }
}
