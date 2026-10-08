using Asura.App.Controls;
using Asura.App.ViewModels;
using Avalonia.Interactivity;

namespace Asura.App.Views;

/// <summary>
/// The connection-aware repository chooser. It browses the panel's target —
/// local or SSH — through the Git client's folder probe and closes with
/// the chosen path, or null when cancelled.
/// </summary>
public sealed partial class GitRepositoryPickerDialog : DialogWindow
{
    public GitRepositoryPickerDialog()
    {
        InitializeComponent();
    }

    public GitRepositoryPickerDialog(GitRepositoryPickerViewModel picker)
        : this()
    {
        ArgumentNullException.ThrowIfNull(picker);
        DataContext = picker;
        Opened += async (_, _) => await picker.LoadInitialAsync();
    }

    /// <summary>
    /// The same browser choosing a folder for something else — a clone's
    /// destination over SSH — under <paramref name="title"/>, with
    /// <paramref name="verb"/> on its button. Double-clicking a folder steps
    /// into it, a repository's too, so only the button chooses.
    /// </summary>
    public GitRepositoryPickerDialog(GitRepositoryPickerViewModel picker, string title, string verb)
        : this(picker)
    {
        picker.ChoosesFolder = true;
        Shell.Title = title;
        OpenButton.Content = verb;
    }

    private GitRepositoryPickerViewModel? Picker => DataContext as GitRepositoryPickerViewModel;

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (Picker?.ResolveSelection() is { } path)
        {
            Close(path);
        }
    }

    private void OnRepositoryChosen(object? sender, string path)
    {
        _ = sender;
        Close(path);
    }
}
