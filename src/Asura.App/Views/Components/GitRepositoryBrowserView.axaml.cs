using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// Browses the panel's or connection's target — local or SSH — through the
/// Git client's folder probe. Choosing is the host's: double-tapping a
/// repository raises <see cref="RepositoryChosen"/>, and the host's own
/// button reads <see cref="GitRepositoryPickerViewModel.ResolveSelection"/>.
/// </summary>
public sealed partial class GitRepositoryBrowserView : UserControl
{
    public GitRepositoryBrowserView() => InitializeComponent();

    /// <summary>A repository was double-tapped: the host should take it.</summary>
    public event EventHandler<string>? RepositoryChosen;

    private GitRepositoryPickerViewModel? Picker => DataContext as GitRepositoryPickerViewModel;

    private async void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (Picker is { } picker)
        {
            await picker.NavigateUpAsync();
        }
    }

    /// <summary>Return in the path goes there; it does not choose.</summary>
    private async void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Enter && Picker is { } picker)
        {
            e.Handled = true;
            await picker.NavigateToInputAsync();
        }
    }

    // Double-tapping a repository chooses it; double-tapping a plain folder
    // steps into it, because the repository may live deeper. A browser
    // choosing a destination folder steps into every folder instead.
    private async void OnDirectoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (Picker is not { SelectedDirectory: { } entry } picker)
        {
            return;
        }

        if (entry.IsRepository && !picker.ChoosesFolder)
        {
            RepositoryChosen?.Invoke(this, entry.FullPath);
        }
        else
        {
            await picker.EnterAsync(entry);
        }
    }
}
