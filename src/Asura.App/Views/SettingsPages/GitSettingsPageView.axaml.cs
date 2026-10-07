using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Asura.App.Views.SettingsPages;

public sealed partial class GitSettingsPageView : UserControl
{
    private GitSettingsPageViewModel? _followed;

    public GitSettingsPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow();
    }

    private GitSettingsPageViewModel? ViewModel => DataContext as GitSettingsPageViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _followed?.StopFollowing();
        _followed = null;
    }

    /// <summary>
    /// The page follows the store only while it is in a window, so a closed
    /// Settings window does not keep listening to every Git panel.
    /// </summary>
    private void Follow()
    {
        if (ReferenceEquals(_followed, ViewModel) && _followed is not null)
        {
            return;
        }

        _followed?.StopFollowing();
        _followed = null;
        if (VisualRoot is null || ViewModel is not { } viewModel)
        {
            return;
        }

        _followed = viewModel;
        _ = viewModel.StartFollowingAsync();
    }

    /// <summary>The dictionary is read on this computer, so the Open panel picks it.</summary>
    private async void OnChooseSpellingDictionaryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var selected = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Spelling Dictionary",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.TextPlain, FilePickerFileTypes.All],
        });
        if (selected.Count > 0 && selected[0].TryGetLocalPath() is { } path)
        {
            viewModel.SpellingDictionary = path;
        }
    }

    private void OnRemoveCustomCommandClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel?.RemoveSelectedCustomCommand();
    }

    /// <summary>A row's context menu acts on that row, whichever is selected.</summary>
    private void OnDeleteCustomCommandClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (ViewModel is { } viewModel
            && sender is Control { DataContext: GitCustomCommandItemViewModel item })
        {
            viewModel.SelectedCustomCommand = item;
            viewModel.RemoveSelectedCustomCommand();
        }
    }

    /// <summary>Delete or Backspace on a selected row removes it, as – does.</summary>
    private void OnCustomCommandListKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Meta
            && e.Key is Key.Delete or Key.Back
            && ViewModel is { CanRemoveCustomCommand: true } viewModel)
        {
            e.Handled = true;
            viewModel.RemoveSelectedCustomCommand();
        }
    }
}
